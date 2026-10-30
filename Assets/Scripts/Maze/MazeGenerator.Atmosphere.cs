using System.Collections;
using System.Collections.Generic;
using StarterAssets;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

// Partial of MazeGenerator: player/AI/Stalker placement, scene ambience takeover, SetUpAtmosphere composition root, and intro banner text.
public partial class MazeGenerator
{
    private RunContext _runContext;

    /// <summary>Composition-root context populated by <see cref="SetUpAtmosphere"/>.</summary>
    public RunContext Context => _runContext;

    /// <summary>F74 DyingGrid run rule: kills one random still-powered, non-locker, non-dead lamp every 60 s, for the rest of the floor. Started once, from SetUpAtmosphere; stops itself when the GameObject does.</summary>
    private IEnumerator DyingGridRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(60f);
            if (!GameFlow.IsRunActive || GameOutcome.IsOver) continue;
            KillRandomPoweredLamp();
        }
    }

    private void KillRandomPoweredLamp()
    {
        List<int> candidates = new List<int>();
        for (int i = 0; i < _lamps.Count; i++)
        {
            WallLamp lamp = _lamps[i];
            if (lamp == null || lamp.IsDead || lamp.IsLockerLamp || !lamp.IsPowered) continue;

            Vector2Int cell = i < _lampCells.Count ? _lampCells[i] : default;
            bool nearStar = false;
            foreach (Vector2Int star in _starCells)
            {
                if (Mathf.Abs(cell.x - star.x) + Mathf.Abs(cell.y - star.y) <= 1) { nearStar = true; break; }
            }
            if (nearStar) continue;

            candidates.Add(i);
        }
        if (candidates.Count == 0) return;

        // A cosmetic pick, not maze-affecting - UnityEngine.Random rather than a seeded stream is fine
        // here (see CLAUDE.md/D15: the RNG-offset discipline is about the maze layout, not this).
        int pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        _lamps[pick].Kill(2.5f);
    }

    private void PlacePlayer()
    {
        Transform player = FindPlayer();
        if (player == null)
        {
            Debug.LogWarning("MazeGenerator: no Player-tagged object with a CharacterController.", this);
            return;
        }

        // Facing the middle of the maze from the start cell - computed so it holds on any rectangle
        // (F40 slice A decision 7), not just the square grids this used to be a constant 45f for.
        Vector3 mazeCentre = new Vector3(origin.x + width * cellSize * 0.5f, 0f, origin.z + height * cellSize * 0.5f);
        Vector3 startPos = CellCenter(_startCell.x, _startCell.y);
        Vector3 toCentre = mazeCentre - startPos;
        float startYaw = toCentre.sqrMagnitude > 0.001f ? Mathf.Atan2(toCentre.x, toCentre.z) * Mathf.Rad2Deg : 45f;

        // The CharacterController overwrites transform.position, so it has to be off while teleporting.
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;

        player.position = startPos;
        player.rotation = Quaternion.Euler(0f, startYaw, 0f);

        if (controller != null) controller.enabled = true;

        if (player.GetComponent<PlayerStealthState>() == null)
        {
            player.gameObject.AddComponent<PlayerStealthState>();
        }

        if (player.GetComponent<PlayerStamina>() == null)
        {
            player.gameObject.AddComponent<PlayerStamina>();
        }

        if (firstPerson && player.GetComponent<FirstPersonRig>() == null)
        {
            // AddComponent runs its Awake synchronously, so the rig reparents the camera right here -
            // after the teleport above, and before the ResetCameraRotation below, which is the order
            // it needs. (Its execution-order attribute only matters if it is ever placed in a scene
            // by hand instead.)
            player.gameObject.AddComponent<FirstPersonRig>();
        }

        ThirdPersonController thirdPerson = player.GetComponent<ThirdPersonController>();
        if (thirdPerson != null)
        {
            thirdPerson.ResetCameraRotation(startYaw);

            if (disablePlayerJump)
            {
                thirdPerson.JumpEnabled = false;
            }
        }
    }

    private void PlaceAI()
    {
        if (aiFollower == null)
        {
            aiFollower = FindAnyObjectByType<AIFollower>();
        }

        if (aiFollower == null) return;

        Vector2Int cell = FarthestCell();
        Vector3 position = CellCenter(cell.x, cell.y);
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, cellSize, NavMesh.AllAreas))
        {
            position = hit.position;
        }

        // AIFollower.Awake has not run yet (this component is at execution order -100), so the agent
        // is fetched here rather than through the follower.
        NavMeshAgent agent = aiFollower.GetComponent<NavMeshAgent>();
        if (agent == null || !agent.Warp(position))
        {
            // Move it anyway, so AIFollower.Start finds the maze navmesh within its search radius
            aiFollower.transform.position = position;
            Debug.LogWarning($"MazeGenerator: could not warp the AI onto the navmesh at {position}.", this);
        }

        // The scene ships a 5 m bidirectional NavMeshLink under the follower. It is registered in its
        // own OnEnable, i.e. after this Awake, so inside the maze it would span two walls and let the
        // AI walk straight through them.
        foreach (NavMeshLink link in aiFollower.GetComponentsInChildren<NavMeshLink>(true))
        {
            link.enabled = false;
        }

        aiFollower.SetWanderPoints(_cellCenters);
        aiFollower.SetPatrolTargets(_stars);
        aiFollower.ApplyDifficulty(_profile);

        List<Vector3> hidingFronts = new List<Vector3>();
        foreach (Locker locker in _lockers)
        {
            hidingFronts.Add(locker.FrontPosition);
        }
        aiFollower.SetHidingSpots(hidingFronts);
        ApplyWeldedLockers(aiFollower); // F122: WELDED LOCKERS (MazeGenerator.Campaign.cs)
        aiFollower.SetAmbushProvider(this);

        AIPresence presence = aiFollower.GetComponent<AIPresence>();
        if (presence == null) presence = aiFollower.gameObject.AddComponent<AIPresence>();

        // The hunter's footsteps are the player's own, pitched down - and the star material is the
        // only emissive one to hand, so the eyes are built from a copy of it.
        presence.Configure(FindPlayerFootsteps(), TakeOverSceneAmbience(), FindStarMaterial());
    }

    /// <summary>
    /// F75 (+16, post-PlaceAI): clones InteractableKit.stalker onto floors whose entity set (EntityRoster) has the Stalker
    /// (4 and 5), warped onto the navmesh at least 8 BFS steps from the start and at least 4 from the
    /// hunter's own cell (FarthestCell - the same cell PlaceAI warped the hunter onto). Falls back to the
    /// single farthest-from-start reachable cell if no cell satisfies both, rather than skip the floor's
    /// stalker outright on a small maze.
    /// </summary>
    private void BuildStalker(System.Random rng)
    {
        if ((_entitySet & EntityKind.Stalker) == 0) return; // F107 D1: the roster decides (was FloorProfile.HasStalker)

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasStalker)
        {
            Debug.LogError("MazeGenerator: no InteractableKit (stalker) in the scene - skipping the stalker this run. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        Vector2Int start = _startCell;
        Vector2Int hunterCell = FarthestCell();

        HashSet<Vector2Int> reachable = ReachableCells(start, null);
        Dictionary<Vector2Int, int> distFromStart = BfsWithin(start, reachable);
        Dictionary<Vector2Int, int> distFromHunter = BfsWithin(hunterCell, reachable);

        List<Vector2Int> candidates = new List<Vector2Int>();
        foreach (Vector2Int cell in reachable)
        {
            if (!distFromStart.TryGetValue(cell, out int ds) || ds < 8) continue;
            if (!distFromHunter.TryGetValue(cell, out int dh) || dh < 4) continue;
            candidates.Add(cell);
        }

        if (candidates.Count == 0)
        {
            Vector2Int best = start;
            int bestDistance = -1;
            foreach (KeyValuePair<Vector2Int, int> entry in distFromStart)
            {
                if (entry.Value > bestDistance) { bestDistance = entry.Value; best = entry.Key; }
            }
            candidates.Add(best);
        }

        Shuffle(candidates, rng);
        Vector2Int chosenCell = candidates[0];
        Vector3 position = CellCenter(chosenCell.x, chosenCell.y);
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, cellSize, NavMesh.AllAreas))
        {
            position = hit.position;
        }

        GameObject instance = Instantiate(interactableKit.Stalker.gameObject, position, Quaternion.identity, _mazeRoot);
        instance.name = "Stalker";
        instance.SetActive(true);

        NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
        if (agent == null) agent = instance.AddComponent<NavMeshAgent>();
        agent.radius = 0.3f;
        agent.speed = 3.4f;
        agent.acceleration = 40f;
        agent.angularSpeed = 720f;
        if (!agent.Warp(position))
        {
            Debug.LogWarning($"MazeGenerator: could not warp the stalker onto the navmesh at {position}.", this);
        }

        _stalker = instance.GetComponent<Stalker>();
    }

    /// <summary>
    /// The scene ships a cheerful 2D sci-fi ambience on a loop, which is exactly wrong here - and its
    /// clip is a perfect mechanical hum for the hunter once it is pitched down and made positional.
    /// Silences the source and hands back its clip.
    /// </summary>
    private AudioClip TakeOverSceneAmbience()
    {
        AudioClip clip = null;

        foreach (AudioSource source in FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude))
        {
            if (!source.loop || !source.playOnAwake || source.clip == null) continue;

            clip = source.clip;
            source.Stop();
            source.enabled = false;
        }

        return clip;
    }

    private AudioClip[] FindPlayerFootsteps()
    {
        Transform player = FindPlayer();
        if (player == null) return null;

        ThirdPersonController controller = player.GetComponent<ThirdPersonController>();
        return controller != null ? controller.FootstepAudioClips : null;
    }

    private void SetUpAtmosphere()
    {
        // Hoisted so the dread wiring below (added after `outcome` exists) can still reach it -
        // it is only built at all when darkness is on.
        HorrorAtmosphere atmosphere = null;
        if (darkness)
        {
            atmosphere = FindAnyObjectByType<HorrorAtmosphere>();
            if (atmosphere == null) atmosphere = gameObject.AddComponent<HorrorAtmosphere>();
            if (_profile != null)
            {
                const float ambientScale = 0.55f;
                const float fogScale = 0.75f;
                if (_theme != null)
                {
                    Color themeAmbient = _theme.Ambient;
                    float themeMax = Mathf.Max(themeAmbient.r, themeAmbient.g, themeAmbient.b);
                    float profileMax = Mathf.Max(_profile.Ambient.r, _profile.Ambient.g, _profile.Ambient.b) * ambientScale;
                    if (themeMax > 0.0001f)
                    {
                        float s = profileMax / themeMax;
                        themeAmbient = new Color(themeAmbient.r * s, themeAmbient.g * s, themeAmbient.b * s, themeAmbient.a);
                    }
                    atmosphere.ApplyProfile(themeAmbient, _profile.FogDensity * _theme.FogDensityScale * fogScale, _theme.FogColor);
                }
                else
                {
                    Color a = _profile.Ambient;
                    atmosphere.ApplyProfile(new Color(a.r * ambientScale, a.g * ambientScale, a.b * ambientScale, a.a), _profile.FogDensity * fogScale);
                }
            }
        }

        HorrorAudioDirector director = FindAnyObjectByType<HorrorAudioDirector>();
        if (director == null) director = gameObject.AddComponent<HorrorAudioDirector>();

        AudioClip music = PickMazeMusic();
        director.Bind(FindPlayer(), aiFollower, stingClip, music != null ? music : droneClip, panicClip, droneIsMusic: music != null, spotted: spottedClip);
        director.ApplyTerror(terror); // F103 A9

        TensionDirector tension = FindAnyObjectByType<TensionDirector>();
        if (tension == null) tension = gameObject.AddComponent<TensionDirector>();

        tension.Configure(aiFollower, director);

        Transform player = FindPlayer();
        FirstPersonRig rig = player != null ? player.GetComponent<FirstPersonRig>() : null;
        Flashlight flashlight = rig != null ? rig.Flashlight : null;
        PlayerStamina stamina = player != null ? player.GetComponent<PlayerStamina>() : null;
        PlayerStealthState stealth = player != null ? player.GetComponent<PlayerStealthState>() : null;

        PlayerHud hud = FindAnyObjectByType<PlayerHud>();
        if (hud == null) hud = gameObject.AddComponent<PlayerHud>();
        hud.Configure(flashlight, stamina);
        tension.BindHud(hud);

        // F73: PlayerStealthState now reads LightPool.ExposureAt directly - every WallLamp registers
        // itself in WallLamp.Configure, so it no longer needs this maze's lamp list handed to it.
        if (_profile != null && stamina != null) stamina.SetSprintSeconds(_profile.SprintSeconds);

        // F105 C3: the squeeze was built before the bake; it needs the player's controller, stamina and torch to slow, tire and dim them.
        BindSqueeze(player, player != null ? player.GetComponent<ThirdPersonController>() : null, stamina, flashlight);

        // F72: the one interaction system every door/button/vault pickup goes through. Built here
        // (rather than FirstPersonRig) because it needs the camera, the HUD and the stealth state, all
        // of which exist by this point in SetUpAtmosphere.
        PlayerInteractor interactor = player != null ? player.GetComponent<PlayerInteractor>() : null;
        if (interactor == null && player != null) interactor = player.gameObject.AddComponent<PlayerInteractor>();
        if (interactor != null) interactor.Configure(rig != null ? rig.PlayerCamera : null, hud, stealth);

        foreach (Locker locker in _lockers)
        {
            locker.Bind(player, stealth, flashlight, hud, interactor);
            locker.ApplyTerror(terror); // F104 B3
            locker.BindHunter(aiFollower);
        }

        // F72: doors/buttons/vault loot were built before the flashlight, HUD and interactor existed
        // (BuildDoors runs in the middle of Awake, well before this method), so they are late-bound
        // here - the same pattern BuildLocker/Locker.Bind already uses.
        foreach (WallButton button in _buttons)
        {
            button.BindHud(hud);
        }
        foreach (BatteryCellPickup pickup in _batteryPickups)
        {
            pickup.BindPlayerSystems(flashlight, hud);
        }

        // Must exist before MazeEscape.Configure receives it
        GameOutcome outcome = FindAnyObjectByType<GameOutcome>();
        if (outcome == null) outcome = gameObject.AddComponent<GameOutcome>();

        // Tier 3: relocation, phantoms, F27's beats and the subtitles they all share. Both need
        // `outcome` for IsEnding, which is why they are wired here rather than beside TensionDirector.
        AIPresence presence = aiFollower != null ? aiFollower.GetComponent<AIPresence>() : null;
        Camera camera = rig != null ? rig.PlayerCamera : null;

        // Ground fog: its soft edges (where a bank meets the floor or a wall) read the camera depth texture, which the URP asset leaves off.
        if (_fogBuilt && camera != null && camera.TryGetComponent(out UnityEngine.Rendering.Universal.UniversalAdditionalCameraData cameraData))
            cameraData.requiresDepthOption = UnityEngine.Rendering.Universal.CameraOverrideOption.On;

        // F74: throwables, noisy floors, lantern, lore, theme interactables. All built earlier in Awake,
        // before the camera/HUD/interactor existed - late-bound here, same pattern as the block above.
        // Placed after `camera` exists (Terminal/ThrowController both need it).
        ThrowController throwController = player != null ? player.GetComponent<ThrowController>() : null;
        if (throwController == null && player != null) throwController = player.gameObject.AddComponent<ThrowController>();
        if (throwController != null)
        {
            throwController.Configure(camera, aiFollower, stealth, _mazeRoot,
                interactableKit != null ? interactableKit.ThrowableBottle : null,
                interactableKit != null ? interactableKit.ThrowableCan : null,
                interactableKit != null ? interactableKit.GlassPatch : null);
        }
        hud.BindThrowController(throwController);

        // F77: the keys held this floor - an instance component on the player (like ThrowController), so
        // a scene reload recreates it empty. Built earlier in Awake (BuildKeys, +18), before the player/
        // HUD/hunter existed - late-bound here, same pattern as the block above.
        KeyRing keyRing = player != null ? player.GetComponent<KeyRing>() : null;
        if (keyRing == null && player != null) keyRing = player.gameObject.AddComponent<KeyRing>();
        keyRing?.Configure(_keys);
        foreach (KeyPickup key in _keys)
        {
            key.Bind(keyRing, hud, aiFollower);
        }
        tension.BindKeyRing(keyRing);

        LoreReadingUi loreReader = FindAnyObjectByType<LoreReadingUi>();
        if (loreReader == null) loreReader = gameObject.AddComponent<LoreReadingUi>();
        loreReader.Configure(aiFollower);
        ApplyArchivistLamp(); // F122
        foreach (LoreNote note in _loreNotes)
        {
            note.Bind(loreReader, hud);
        }

        foreach (Lantern lantern in _lanterns)
        {
            lantern.BindPlayerSystems(player, stamina, stealth, aiFollower, hud);
        }

        foreach (DescentItem cache in _itemCaches)
        {
            cache.Bind(hud, aiFollower, throwController, flashlight); // F107 C
        }

        foreach (ThemeInteractable theme in _themeInteractables)
        {
            theme.BindHud(hud);
        }
        foreach (ThemeInteractable terminal in _pendingTerminals)
        {
            terminal.ConfigureTerminal(aiFollower, camera);
        }

        // F74 WeakBattery run rule: applied once here, right after the flashlight exists and before the
        // player has had a chance to touch it.
        // F122: the recharge multiplier now COMPOSES (WeakBattery x difficulty x the Dynamo card) instead of one rule overwriting it, and the
        // drain multiplier is new. With nothing active these are the very same calls as before (no call at all for a factor of 1).
        if (flashlight != null)
        {
            if (RunRules.Has(RunRules.Rule.WeakBattery)) flashlight.SetStartFraction(RunRules.BatteryStartFraction);
            float recharge = (RunRules.Has(RunRules.Rule.WeakBattery) ? RunRules.BatteryRechargeMultiplier : 1f)
                * CampaignRules.RechargeScale * (PlayerInventory.HasActive(ShopItem.Dynamo) ? 1.75f : 1f);
            if (!Mathf.Approximately(recharge, 1f)) flashlight.SetRechargeMultiplier(recharge);
            if (!Mathf.Approximately(CampaignRules.DrainScale, 1f)) flashlight.SetDrainMultiplier(CampaignRules.DrainScale);
        }

        // F74 DyingGrid run rule: a random still-powered lamp loses power every minute, for the rest of
        // the floor. Lives on this component (not DreadDirector) since it is a plain interval effect,
        // not something that keys off star progress.
        if (RunRules.Has(RunRules.Rule.DyingGrid)) StartCoroutine(DyingGridRoutine());

        // F73: lets the hunter notice the torch beam itself, not just what it lights up.
        aiFollower?.SetDormantForSoloEntity((_entitySet & EntityKind.Elias) != 0);
        aiFollower?.BindTorch(flashlight);
        flashlight?.ApplyTerror(terror); // F103 A4

        // F82: the hunter talks in speech bubbles too (same engine as Stan's). KEEP IT LIT > Build Phrase
        // Books puts a HunterVoice with its phrase book on the scene hunter; added here if missing.
        if (aiFollower != null)
        {
            HunterVoice hunterVoice = aiFollower.GetComponent<HunterVoice>();
            if (hunterVoice == null) hunterVoice = aiFollower.gameObject.AddComponent<HunterVoice>();
            hunterVoice.Configure(aiFollower, stealth, outcome);
            hunterVoice.ShowBubbles = creatureSpeechBubbles;
            // 10 Oct 2026: and in a real voice - the Orderly's PA lines while it patrols.
            if (aiFollower.GetComponent<OrderlyPa>() == null) aiFollower.gameObject.AddComponent<OrderlyPa>();
        }
        // 10 Oct 2026: wind gusts and far-off sounds through the maze (Scary Sound FX pack, GameAudioLibrary.mazeWind / mazeFar).
        if (GetComponent<MazeAmbience>() == null) gameObject.AddComponent<MazeAmbience>();
        // F106: the floor map the loop puzzle rewards (instance state, so a reload locks it again); configured once the pause menu exists, below.
        FloorMapUi floorMap = FindAnyObjectByType<FloorMapUi>();
        if (floorMap == null) floorMap = gameObject.AddComponent<FloorMapUi>();
        BindLoop(player, player != null ? player.GetComponent<ThirdPersonController>() : null, camera, flashlight, director, GetComponent<MazeAmbience>(), outcome, hud, floorMap, throwController); // F105 C4, F106

        // F75: the second creature (see EntityRoster). Built earlier in Awake
        // (BuildStalker, +16), before the player/camera/flashlight/hunter/HUD/outcome existed -
        // late-bound here, same pattern as the lantern/lore notes above.
        _stalker?.Configure(this, player, flashlight, camera != null ? camera.transform : null, stealth, aiFollower, hud, outcome);
        _stalker?.ApplyTerror(terror); // F105 C7
        if (_stalker != null && _stalker.TryGetComponent(out StalkerVoice stanVoice)) stanVoice.ShowBubbles = creatureSpeechBubbles;

        // F75 shop depth: Focus Lens widens the focused beam and drops its drain penalty for this
        // floor; Fuse Map lights up every fuse box, button, vault door and (F77) key with a small beacon
        // and lets the Star Compass (ConsumableController.NearestCompassTarget) point at them too.
        if (flashlight != null && PlayerInventory.HasActive(ShopItem.FocusLens))
        {
            flashlight.SetRangeMultiplier(1.35f);
            flashlight.SetFocusedDrainMultiplier(1f);
        }
        if (PlayerInventory.HasActive(ShopItem.FuseMap))
        {
            foreach (FuseBox box in _fuseBoxes) box?.EnableBeacon();
            foreach (WallButton button in _buttons) button?.EnableBeacon();
            foreach (MazeDoor door in _doors)
            {
                if (door != null && door.IsVaultDoor) door.EnableBeacon();
            }
            foreach (KeyPickup key in _keys) key?.EnableBeacon();
        }

        RunContext ctx = new RunContext
        {
            Maze = this,
            Profile = _profile,
            Theme = _theme,
            Terror = terror,
            Seed = UsedSeed,
            Player = player,
            Camera = camera != null ? camera.transform : null,
            Rig = rig,
            Torch = flashlight,
            Stamina = stamina,
            Stealth = stealth,
            Controller = player != null ? player.GetComponent<ThirdPersonController>() : null,
            Interactor = interactor,
            ThrowController = throwController,
            KeyRing = keyRing,
            Hunter = aiFollower,
            Presence = presence,
            Lamps = _lamps,
            Lockers = _lockers,
            Doors = _doors,
            Hud = hud,
            Atmosphere = atmosphere,
            Audio = director,
            Ambience = GetComponent<MazeAmbience>(),
            FloorMap = floorMap,
            LoreReader = loreReader,
            Tension = tension,
            Outcome = outcome
        };
        _runContext = ctx;

        DreadDirector dread = FindAnyObjectByType<DreadDirector>();
        if (dread == null) dread = gameObject.AddComponent<DreadDirector>();
        ctx.Dread = dread;
        dread.Configure(ctx);
        dread.ApplyTerror(terror); // F103
        presence?.SetEyesLitAtRest(_profile == null || _profile.HunterEyesLitAtRest); // F103 A3
        presence?.ApplyTerror(terror);
        aiFollower?.ApplyTerror(terror); // F103 A7
        // F74 Restless run rule: relocations can chain tighter. PhantomInterval's own x0.6 is already
        // folded into _profile by RunRules.ApplyToProfile, read by PhantomDirector.Configure below.
        if (RunRules.Has(RunRules.Rule.Restless)) dread.SetHardMinimumCooldownMultiplier(0.7f);

        PhantomDirector phantoms = FindAnyObjectByType<PhantomDirector>();
        if (phantoms == null) phantoms = gameObject.AddComponent<PhantomDirector>();
        ctx.Phantoms = phantoms;
        Material phantomWallMat = _theme != null && _theme.WallMaterial != null ? _theme.WallMaterial : _wallMat;
        phantoms.Configure(ctx, phantomWallMat, FindStarMaterial(), FindPlayerFootsteps());
        phantoms.ApplyTerror(terror); // F103 A2

        // F104 B1: the hidden fear value. Wired after the directors it feeds exist; with the flag off the readers fall back to their old behaviour.
        DreadMeter dreadMeter = null; // hoisted (F123): the wing nightmares below read it
        if (aiFollower != null && player != null)
        {
            dreadMeter = FindAnyObjectByType<DreadMeter>();
            if (dreadMeter == null) dreadMeter = gameObject.AddComponent<DreadMeter>();
            dreadMeter.Configure(aiFollower, player, stealth, stamina, flashlight, this, outcome);
            dreadMeter.ApplyTerror(terror);
            director.BindDread(dreadMeter);
            atmosphere?.BindDread(dreadMeter, director);
            phantoms.BindDread(dreadMeter);
            tension.BindDread(dreadMeter);
            dread.BindDread(dreadMeter);
            dread.BindAmbience(GetComponent<MazeAmbience>());
        }
        ctx.DreadMeter = dreadMeter;

        // F104 B9/B10: displacement and lying landmarks - dressing quietly changes while unobserved.
        WorldShiftDirector worldShift = null; // hoisted (F123)
        if (player != null && camera != null)
        {
            worldShift = FindAnyObjectByType<WorldShiftDirector>();
            if (worldShift == null) worldShift = gameObject.AddComponent<WorldShiftDirector>();
            worldShift.Configure(this, player, camera, aiFollower, outcome);
            worldShift.ApplyTerror(terror);
        }
        ctx.WorldShift = worldShift;

        // F123: each story floor is its wing patient's nightmare. A DESCENT gets floor 0, i.e. nothing (and no banks, ampoules or notices were built).
        if (player != null && camera != null)
        {
            WingNightmareDirector nightmares = FindAnyObjectByType<WingNightmareDirector>();
            if (nightmares == null) nightmares = gameObject.AddComponent<WingNightmareDirector>();
            nightmares.Configure(ctx);
            nightmares.ApplyTerror(terror);
            BindNightmares(nightmares, hud, dreadMeter);
        }

        // Tier 4: the room between floors. Unlike everything else here it is authored in the scene
        // (Assets/Prefabs/ShopRoom.prefab, first generated by KEEP IT LIT > Build > Shop Room (regenerate)), so it can be inspected and edited in the
        // Editor. Only its panel is runtime UI.
        ShopMenu shopMenu = FindAnyObjectByType<ShopMenu>();
        if (shopMenu == null) shopMenu = gameObject.AddComponent<ShopMenu>();
        shopMenu.Configure(player, hud, flashlight);
        ctx.ShopMenu = shopMenu;

        ShopRoom shop = FindAnyObjectByType<ShopRoom>();
        if (shop != null && !shop.IsComplete)
        {
            Debug.LogError("MazeGenerator: the ShopRoom in the scene has no Arrival Point or zones. Run KEEP IT LIT > Build Missing Pieces in the Editor (it places Assets/Prefabs/ShopRoom.prefab).", shop);
            shop = null;
        }
        else if (shop == null)
        {
            Debug.LogError("MazeGenerator: there is no ShopRoom in the scene, so clearing floors 1-4 will end the run as a win. Run KEEP IT LIT > Build Missing Pieces in the Editor (it places Assets/Prefabs/ShopRoom.prefab), then save the scene.", this);
        }
        if (shop != null) shop.Configure(player, hud, shopMenu);
        ctx.Shop = shop;

        // F86: the Intake - the playable tutorial START GAME opens before floor 1. Authored in the scene like the shop
        // (Assets/Prefabs/IntakeRoom.prefab; KEEP IT LIT > Check Project); without it START GAME falls back to the old rules screen.
        IntakeRoom intake = FindAnyObjectByType<IntakeRoom>(FindObjectsInactive.Include);
        if (intake != null && !intake.IsComplete)
        {
            Debug.LogError("MazeGenerator: the IntakeRoom in the scene is incomplete. Fix Assets/Prefabs/IntakeRoom.prefab, or run KEEP IT LIT > Check Project in the Editor.", intake);
            intake = null;
        }
        else if (intake == null && mainMenu)
        {
            Debug.LogError("MazeGenerator: there is no IntakeRoom in the scene, so START GAME shows the rules screen instead of the tutorial. Run KEEP IT LIT > Build Missing Pieces in the Editor (it places Assets/Prefabs/IntakeRoom.prefab), then save the scene.", this);
        }
        if (intake != null) intake.Configure(player, camera, hud, flashlight, stealth, interactor, throwController, loreReader, PickBaseMusic(), panicClip);
        ctx.Intake = intake;

        // F92: the Asylum hub - the hospital the player walks through between mazes (Assets/Prefabs/AsylumHub.prefab; KEEP IT LIT > Check
        // Project). Without it, or with an incomplete one, the Intake and the shop door hand straight over to the maze as they always did.
        AsylumHub asylum = FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (asylum != null && !asylum.IsComplete)
        {
            Debug.LogError("MazeGenerator: the AsylumHub in the scene is incomplete, so the run skips the asylum. Run KEEP IT LIT > Build Missing Pieces in the Editor (or fix Assets/Prefabs/AsylumHub.prefab), then save the scene.", asylum);
            asylum = null;
        }
        else if (asylum == null && mainMenu)
        {
            Debug.LogError("MazeGenerator: there is no AsylumHub in the scene, so the run goes straight from the Intake and the shop into the mazes. Run KEEP IT LIT > Build Missing Pieces in the Editor (it places Assets/Prefabs/AsylumHub.prefab), then save the scene.", this);
        }
        if (asylum != null) asylum.Configure(player, camera, hud, flashlight, PickBaseMusic(), loreReader, panicClip, shopMenu, UsedSeed);
        ctx.Asylum = asylum;

        MazeEscape escape = null;
        if (escapeSequence)
        {
            escape = FindAnyObjectByType<MazeEscape>();
            if (escape == null) escape = gameObject.AddComponent<MazeEscape>();

            escape.Configure(this, player, FindStarMaterial(), outcome, aiFollower);
            escape.ApplyTerror(terror); // F104 B4
            if (_profile != null)
            {
                escape.SetEscapeSeconds(_profile.EscapeSeconds);
                escape.SetGateLeverSeconds(_profile.GateLeverSeconds); // F111
                escape.SetGateEscapeSeconds(_profile.GateEscapeSeconds);
                if (HasExitGates) escape.SetGates(_exitGates);
            }
            // F77: the padlock - the hatch/gates refuse to complete while keys are still missing.
            escape.BindKeyRing(keyRing, hud);
        }
        ctx.Escape = escape;

        outcome.Configure(player, aiFollower, director, escape, flashlight, UsedSeed);
        outcome.BindShop(shop, this, hud, stealth);
        ConfigureEntities(player); // F107 D1: the Listener, Tenant and living bodies, now that the HUD, outcome and escape exist

        MainMenu menu = null;
        if (mainMenu)
        {
            menu = FindAnyObjectByType<MainMenu>();
            if (menu == null) menu = gameObject.AddComponent<MainMenu>();

            menu.Configure(player, menuMusic);
        }
        else
        {
            // No title screen means nothing calls MainMenu.StartGame, which is what starts the run
            GameFlow.BeginRun();
        }
        ctx.Menu = menu;

        PauseMenu pause = FindAnyObjectByType<PauseMenu>();
        if (pause == null) pause = gameObject.AddComponent<PauseMenu>();
        ctx.Pause = pause;

        pause.Configure(player, flashlight, menu, outcome, UsedSeed, shopMenu, floorMap);
        floorMap.Configure(this, hud, outcome, pause);
        if (debugFullMap)
        {
            floorMap.Unlock();
            floorMap.RevealAll();
            floorMap.OpenWhenAvailable();
        }

        // F87: checks the contract taken in the shop for this floor (an instance component, like KeyRing: a reload recreates it).
        ContractTracker contracts = player != null ? player.GetComponent<ContractTracker>() : null;
        if (contracts == null && player != null) contracts = player.gameObject.AddComponent<ContractTracker>();
        if (contracts != null) contracts.Configure(player, stealth, flashlight, aiFollower, hud, menu);
        outcome.BindContracts(contracts);
        ctx.Contracts = contracts;

        // F120: the microphone (opt-in, desktop only). An instance component on the player; HabitTracker reads its HeardThisFloor.
        PlayerVoice voice = player != null ? player.GetComponent<PlayerVoice>() : null;
        if (voice == null && player != null) voice = player.gameObject.AddComponent<PlayerVoice>();
        if (voice != null) voice.Configure(player, stealth, aiFollower, outcome, terror);
        hud?.BindVoice(voice);
        ctx.Voice = voice;

        // F105 C6: counts the player's habits and, at floor clear, teaches the hunter (an instance component like ContractTracker).
        HabitTracker habits = player != null ? player.GetComponent<HabitTracker>() : null;
        if (habits == null && player != null) habits = player.gameObject.AddComponent<HabitTracker>();
        if (habits != null) habits.Configure(stealth, flashlight, stamina, throwController, hud, terror);
        outcome.BindHabits(habits);
        ctx.Habits = habits;

        // F120: the survivable grab (first hunter catch per floor is a shove). An instance component, so a reload gives a fresh "unused".
        HunterGrab grab = FindAnyObjectByType<HunterGrab>();
        if (grab == null) grab = gameObject.AddComponent<HunterGrab>();
        grab.Configure(player, player != null ? player.GetComponent<StarterAssets.ThirdPersonController>() : null, stamina, flashlight, aiFollower, hud,
            (interactableKit != null ? interactableKit : FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include))?.DroppedTorch, terror);
        outcome.BindGrab(grab);
        ctx.Grab = grab;

        // F120: the death replay's ring buffer (20 Hz, ~8.5 s). Records only while a run is active; the view exists only on the end screen.
        DeathReplayRecorder replay = FindAnyObjectByType<DeathReplayRecorder>();
        if (replay == null) replay = gameObject.AddComponent<DeathReplayRecorder>();
        replay.Configure(player, aiFollower, flashlight, stealth, player != null ? player.Find("PlayerCameraRoot") : null);
        outcome.BindReplay(replay, terror);
        ctx.Replay = replay;

        ConsumableController items = FindAnyObjectByType<ConsumableController>();
        if (items == null) items = gameObject.AddComponent<ConsumableController>();
        items.Configure(player, flashlight, stealth, this, hud, escape, outcome, menu, pause);
        ctx.Consumables = items;

        // F122: counts the attempt (chases, Stan, spare batteries, Second Winds) and grades the floor at its clear.
        FloorGradeTracker gradeTracker = GetComponent<FloorGradeTracker>();
        if (gradeTracker == null) gradeTracker = gameObject.AddComponent<FloorGradeTracker>();
        gradeTracker.Configure(this, aiFollower, items, grab);
        outcome.BindGrades(gradeTracker);
        ctx.GradeTracker = gradeTracker;

        // F76: the on-screen controls for the Web build on a tablet, and the mobile render-scale/shadow
        // trim. Built last of the two, once every system it drives (interactor, throwController, hud,
        // menu, pause, shopMenu, outcome, loreReader) already exists.
        StarterAssetsInputs starterInputs = player != null ? player.GetComponent<StarterAssetsInputs>() : null;
        TouchControls touchControls = FindAnyObjectByType<TouchControls>();
        if (touchControls == null) touchControls = gameObject.AddComponent<TouchControls>();
        touchControls.Configure(starterInputs, stamina, interactor, throwController, hud, menu, pause, shopMenu, outcome, loreReader, debugForceTouchControls);

        MobilePerformance mobilePerf = FindAnyObjectByType<MobilePerformance>();
        if (mobilePerf == null) mobilePerf = gameObject.AddComponent<MobilePerformance>();
        mobilePerf.Configure(flashlight);

        // S2: settings. The router sweeps unrouted AudioSources into the Effects mixer group; the menu is built lazily on first Open.
        if (FindAnyObjectByType<AudioRouter>() == null) gameObject.AddComponent<AudioRouter>();
        if (SettingsMenu.Active == null) gameObject.AddComponent<SettingsMenu>();
        GameSettings.SweepToEffects();

        // Campaign cosmetics (F35), applied on every scene build so the choice survives a reload.
        if (flashlight != null)
        {
            flashlight.SetBeamColor(ShopCatalogue.TorchColours[PlayerInventory.TorchColourIndex].Colour);
            if (_profile != null && _profile.CellSize > 4.5f) flashlight.SetBeamBoost(1.5f, 1.25f);
        }
        if (hud != null) hud.SetTint(ShopCatalogue.HudTints[0].Colour); // F110: the HUD tint is retired, always the default
        tension.SetCalmColor(ShopCatalogue.HudTints[0].Colour);
        if (_profile != null && stealth != null) stealth.NoiseScale = _profile.NoiseScale;

        // F74: the "FLOOR n - THE X" card. Built last, once every other system (theme,
        // RunRules, FloorProfile) is settled - its own coroutine waits out the title screen itself.
        FloorIntroBanner banner = FindAnyObjectByType<FloorIntroBanner>();
        if (banner == null) banner = gameObject.AddComponent<FloorIntroBanner>();
        banner.Show(FloorTitleText(), ObjectiveLineText(), RuleLineText(), _profile != null ? _profile.TemperamentText : "", DescentRecordLineText());

        // Debug affordance: open the floor as if the hatch had just been reached.
        if (debugStartInShop) StartCoroutine(DebugEnterShop(outcome));
    }

    private static IEnumerator DebugEnterShop(GameOutcome outcome)
    {
        yield return null;
        outcome.FloorCleared();
    }

    /// <summary>F74 FloorIntroBanner title line. The primitive fallback has no FloorTheme to name, so it gets a generic text.</summary>
    private string FloorTitleText()
    {
        if (GameFlow.IsDescent)
        {
            if (_profile != null && _profile.Shape != FloorProfile.FloorShape.Rectangle)
            {
                string shapeKey = _profile.Shape switch
                {
                    FloorProfile.FloorShape.Ring => "hud.descent.shape.ring",
                    FloorProfile.FloorShape.TwinWings => "hud.descent.shape.twin_wings",
                    FloorProfile.FloorShape.Cross => "hud.descent.shape.cross",
                    FloorProfile.FloorShape.LChasm => "hud.descent.shape.l_chasm",
                    _ => null
                };
                if (shapeKey != null && L10n.Has("hud.descent.level_shape") && L10n.Has(shapeKey))
                {
                    return L10n.T("hud.descent.level_shape", ("n", DescentRun.Depth), ("shape", L10n.T(shapeKey)));
                }
            }
            return L10n.T("hud.descent.level", ("n", DescentRun.Depth)); // F107
        }
        int floor = GameFlow.CurrentFloor;
        if (_theme != null)
        {
            // S9: the look's name is a Shop-table key by the profile's theme number (D10); the scene's serialized name is the English fallback.
            string themeKey = _profile != null ? "shop.theme." + _profile.ThemeIndex : null;
            string themeName = themeKey != null && L10n.Has(themeKey) ? L10n.T(themeKey) : _theme.DisplayName.ToUpperInvariant();
            themeName = WingNightmares.BannerName(floor) ?? themeName; // F123: the wing's own name on a story floor (DESCENT returned above)
            return L10n.T("sub.banner.floor_theme", ("floor", floor), ("theme", themeName));
        }
        return L10n.T("sub.banner.floor", ("floor", floor));
    }

    /// <summary>F74/F77 FloorIntroBanner objective line, from FloorProfile.Objectives (a flag set since F77).</summary>
    private string ObjectiveLineText()
    {
        if (_profile == null) return "";

        List<string> parts = new List<string> { L10n.T("sub.banner.find_stars", ("n", _profile.StarCount)) };
        if (_profile.Has(FloorProfile.Objective.Blackout)) parts.Add(L10n.T("sub.banner.restore_fuses", ("n", _profile.FuseCount)));
        if (_profile.Has(FloorProfile.Objective.KeyHunt)) parts.Add(L10n.T(HasExitGates ? "sub.banner.find_keys_gates" : "sub.banner.find_keys", ("n", _profile.KeyCount)));
        if (_profile.Has(FloorProfile.Objective.Lockdown)) parts.Add(L10n.T("sub.banner.lockdown"));
        return string.Join(" · ", parts);
    }

    /// <summary>F74 FloorIntroBanner rule line. Empty on floor 1 or when RunRules picked nothing.</summary>
    private static string RuleLineText()
    {
        string oaths = OathBannerText(); // F122
        string rule = RunRules.Current == RunRules.Rule.None ? "" : L10n.T("sub.banner.rule", ("name", RunRules.Name), ("description", RunRules.Description));
        return rule.Length == 0 ? oaths : oaths.Length == 0 ? rule : rule + " · " + oaths;
    }

    /// <summary>
    /// Picks the background music bed for the maze itself: when the maze has Elias (<see cref="EntityKind.Elias"/>),
    /// plays his dedicated soundtrack ("Arya of Terror - Dolls Of Nagoro" from <see cref="GameAudioLibrary.eliasMazeMusic"/>);
    /// otherwise returns <see cref="PickBaseMusic"/>.
    /// </summary>
    private AudioClip PickMazeMusic()
    {
        if ((_entitySet & EntityKind.Elias) != 0)
        {
            AudioClip eliasTrack = GameAudio.Library.eliasMazeMusic.First();
            if (eliasTrack != null) return eliasTrack;
        }
        return PickBaseMusic();
    }

    /// <summary>One of baseMusic for this floor, from the seed (a Retry of the same maze keeps it), or null if none are set.</summary>
    private AudioClip PickBaseMusic()
    {
        if (baseMusic == null || baseMusic.Length == 0) return null;
        // Salted ("MUSI") so it is its own draw, independent of RunRules' and the objective roll's.
        int hash = RunRules.HashSeedFloor(UsedSeed ^ unchecked((int)0x4D555349), GameFlow.CurrentFloor);
        int index = ((hash % baseMusic.Length) + baseMusic.Length) % baseMusic.Length;
        if (baseMusic[index] != null) return baseMusic[index];
        foreach (AudioClip clip in baseMusic) if (clip != null) return clip;
        return null;
    }

    private static Transform FindPlayer()
    {
        // Same rule as AIFollower.FindTarget: two objects carry the Player tag and only one moves.
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject player in players)
        {
            if (player.GetComponent<CharacterController>() != null)
            {
                return player.transform;
            }
        }

        return players.Length > 0 ? players[0].transform : null;
    }
}
