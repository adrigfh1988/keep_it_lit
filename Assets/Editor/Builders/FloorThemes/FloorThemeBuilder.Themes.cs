using System.Collections.Generic;
using UnityEngine;

public static partial class FloorThemeBuilder
{
    // ---------------------------------------------------------------- theme 1: the Ward

    private static ThemeAssets BuildWard()
    {
        const string folder = ThemesRoot + "/1_Ward";
        const string tiles = "Assets/SourceFiles/Textures/Repeating Tiles";

        Material wallMat = LoadOrCreateMat(folder, "Ward_Wall", new Color(0.55f, 0.62f, 0.58f), "Assets/SourceFiles/Textures/Grid/Grid_02_BaseMap.png", null, new Vector2(2f, 1f), null);
        Material pillarMat = LoadOrCreateMat(folder, "Ward_Pillar", new Color(0.56f, 0.64f, 0.60f), null, null, Vector2.one, null); // F90: the wall's tile set at a column's tiling (ThemeSurfaceUpgrade)
        Material floorMat = LoadOrCreateMat(folder, "Ward_Floor", new Color(0.40f, 0.41f, 0.39f), $"{tiles}/Tile_CheckerBoard_Albedo.png", $"{tiles}/Tile_CheckerBoard_normal.png", new Vector2(4f, 4f), null);
        Material ceilingMat = LoadOrCreateMat(folder, "Ward_Ceiling", new Color(0.50f, 0.50f, 0.48f), null, null, Vector2.one, null);
        Material metalMat = LoadOrCreateMat(folder, "Ward_Metal", new Color(0.70f, 0.72f, 0.70f), null, null, Vector2.one, null);
        Color glassColor = new Color(0.85f, 1.0f, 0.95f);
        Material glassMat = LoadOrCreateMat(folder, "Ward_Glass", glassColor, null, null, Vector2.one, glassColor * 2.5f);
        Material crossMat = LoadOrCreateMat(folder, "Ward_Cross", new Color(0.6f, 0.1f, 0.1f), null, null, Vector2.one, null);

        // Wall: just the textured body (the dado band, skirting and cornice were untextured trims - F90).
        GameObject wallRoot = new GameObject("Ward_Wall");
        Box(wallRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(5f, 4f, 0.5f), wallMat, keepCollider: true);
        WallPiece wall = FinishWall(wallRoot, folder);

        // Pillar: a plain textured column (the metal cap was an untextured trim - F90).
        GameObject pillarRoot = new GameObject("Ward_Pillar");
        Box(pillarRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(0.7f, 4f, 0.7f), pillarMat, keepCollider: true);
        GameObject pillar = SaveAsPrefab(pillarRoot, folder, "Ward_Pillar");

        // FloorTile: slab (the old primitive drain disc is gone - F89).
        GameObject floorRoot = new GameObject("Ward_FloorTile");
        Box(floorRoot.transform, "Tile", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), floorMat, keepCollider: true);
        GameObject floorTile = SaveAsPrefab(floorRoot, folder, "Ward_FloorTile");

        // CeilingTile: slab.
        GameObject ceilingRoot = new GameObject("Ward_CeilingTile");
        Box(ceilingRoot.transform, "Tile", new Vector3(0f, 0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), ceilingMat, keepCollider: true);
        GameObject ceilingTile = SaveAsPrefab(ceilingRoot, folder, "Ward_CeilingTile");

        // Lamp: fluorescent tube.
        GameObject lampRoot = new GameObject("Ward_Lamp");
        Box(lampRoot.transform, "Housing", new Vector3(0f, 0f, 0.06f), new Vector3(1.2f, 0.08f, 0.12f), metalMat);
        GameObject glassGo = Box(lampRoot.transform, "Glass", new Vector3(0f, -0.04f, 0.09f), new Vector3(1.1f, 0.05f, 0.08f), glassMat);
        GameObject lightHolder = new GameObject("Light");
        lightHolder.transform.SetParent(lampRoot.transform, false);
        lightHolder.transform.localPosition = new Vector3(0f, 0f, 0.15f);
        Light lampLight = lightHolder.AddComponent<Light>();
        lampLight.type = LightType.Point;
        lampLight.shadows = LightShadows.None;
        WallLamp lamp = FinishLamp(lampRoot, folder, lampLight, glassGo.GetComponent<Renderer>());

        // Locker: hospital cabinet with a red cross on the bottom slat.
        Locker locker = BuildLockerShell(folder, "Ward_Locker", metalMat,
            doorPivot =>
            {
                GameObject[] slats = BuildDoorSlats(doorPivot, 1.06f, 2.06f, 0.04f, metalMat);
                float crossX = 0.53f;
                Box(doorPivot, "CrossH", new Vector3(crossX, 0.9f, 0.045f), new Vector3(0.3f, 0.06f, 0.01f), crossMat);
                Box(doorPivot, "CrossV", new Vector3(crossX, 0.9f, 0.045f), new Vector3(0.06f, 0.3f, 0.01f), crossMat);
                _ = slats;
            });

        // Props.
        List<PropPiece> props = new List<PropPiece>();

        // ---- F70: additional props (plan section 3.3, Ward table) ----

        props.AddRange(BuildCommonFloorProps());
        props.AddRange(BuildAlchemistProps(ThemeSets[0])); // F79
        props.AddRange(BuildPackProps(PackWard)); // F85

        CommonDecalMaterials wardCommonDecals = GetCommonDecalMaterials();
        Material wardGrimeMat = LoadOrCreateDecalMat(folder, "Ward_Grime", EnsureDecalTexture("Decal_Grime"), new Color(0.1f, 0.14f, 0.1f), 0.1f);
        Material wardDripMat = LoadOrCreateDecalMat(folder, "Ward_Drip", EnsureDecalTexture("Decal_Drip"), new Color(0.14f, 0.16f, 0.12f), 0.15f);
        Material wardFloorGrimeMat = LoadOrCreateDecalMat(folder, "Ward_FloorGrime", EnsureDecalTexture("Decal_Grime"), new Color(0.1f, 0.11f, 0.1f), 0.1f);
        props.AddRange(BuildThemeDecals(folder, "Ward", wardCommonDecals, wardGrimeMat, wardDripMat, wardFloorGrimeMat));

        // ---- F70: set pieces (plan section 3.4) ----

        List<SetPiece> setPieces = new List<SetPiece>
        {
            BuildFallenRunnerSetPiece(wardCommonDecals)
        };
        setPieces.AddRange(BuildPackSetPieces(PackWard)); // F85

        DustMotes dust = BuildDustPrefab(folder, "Ward_Dust", new Color(0.8f, 0.8f, 0.75f), 25f, 0.015f, 0.03f);

        return new ThemeAssets
        {
            DisplayName = "The Ward",
            Ambient = new Color(0.10f, 0.12f, 0.11f),
            FogColor = new Color(0.02f, 0.03f, 0.025f),
            FogDensityScale = 1.0f,
            LampColor = glassColor,
            FaultyLampChance = 0.25f,
            WallPropChance = 0.35f,
            CeilingPropChance = 0.15f,
            WallMaterial = wallMat,
            Wall = wall,
            Pillar = pillar,
            FloorTile = floorTile,
            CeilingTile = ceilingTile,
            Lamp = lamp,
            Locker = locker,
            Props = props.ToArray(),
            FloorPropChance = 0.3f,
            WallDecalChance = 0.45f,
            FloorDecalChance = 0.3f,
            MaxDecalsPerCell = 2,
            SetPieces = setPieces.ToArray(),
            SetPieceCount = 3,
            Dust = dust
        };
    }

    // ---------------------------------------------------------------- theme 3: the Crypt
    // (declared here, ahead of theme 2 below, purely by editing order - Build()'s builder array is what
    // actually fixes the floor order: Ward, BoilerDeck, Crypt, Lab, Hollow.)

    private static ThemeAssets BuildCrypt()
    {
        const string folder = ThemesRoot + "/3_Crypt";
        const string tiles = "Assets/SourceFiles/Textures/Repeating Tiles";

        Material stoneMat = LoadOrCreateMat(folder, "Crypt_Stone", new Color(0.38f, 0.34f, 0.30f), $"{tiles}/Moon_Albedo.png", $"{tiles}/Moon_Normal.png", new Vector2(2f, 2f), null);
        Material floorMat = LoadOrCreateMat(folder, "Crypt_Floor", new Color(0.22f, 0.18f, 0.15f), $"{tiles}/SandTroddenRed_Albedo.tif", $"{tiles}/SandTrodden_Normal.tif", new Vector2(3f, 3f), null);
        Material ceilingMat = LoadOrCreateMat(folder, "Crypt_Ceiling", new Color(0.20f, 0.18f, 0.16f), $"{tiles}/Moon_Albedo.png", $"{tiles}/Moon_Normal.png", new Vector2(2f, 2f), null);
        Material woodMat = LoadOrCreateMat(folder, "Crypt_Wood", new Color(0.20f, 0.15f, 0.11f), null, null, Vector2.one, null);
        Material ironMat = LoadOrCreateMat(folder, "Crypt_Iron", new Color(0.10f, 0.10f, 0.10f), null, null, Vector2.one, null);
        Color flameColor = new Color(1.0f, 0.55f, 0.20f);
        Material flameMat = LoadOrCreateMat(folder, "Crypt_Flame", flameColor, null, null, Vector2.one, flameColor * 3f);

        GameObject wallRoot = new GameObject("Crypt_Wall");
        Box(wallRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(5f, 4f, 0.5f), stoneMat, keepCollider: true);
        Box(wallRoot.transform, "Ledge", new Vector3(0f, 2.4f, 0f), new Vector3(5f, 0.12f, 0.6f), stoneMat);
        WallPiece wall = FinishWall(wallRoot, folder);

        GameObject pillarRoot = new GameObject("Crypt_Pillar");
        CylinderRH(pillarRoot.transform, "Shaft", new Vector3(0f, 2f, 0f), 0.4f, 4f, stoneMat, keepCollider: true);
        CylinderRH(pillarRoot.transform, "Base", new Vector3(0f, 0.075f, 0f), 0.5f, 0.15f, stoneMat);
        CylinderRH(pillarRoot.transform, "Capital", new Vector3(0f, 3.925f, 0f), 0.5f, 0.15f, stoneMat);
        GameObject pillar = SaveAsPrefab(pillarRoot, folder, "Crypt_Pillar");

        GameObject floorRoot = new GameObject("Crypt_FloorTile");
        Box(floorRoot.transform, "Tile", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), floorMat, keepCollider: true);
        GameObject floorTile = SaveAsPrefab(floorRoot, folder, "Crypt_FloorTile");

        GameObject ceilingRoot = new GameObject("Crypt_CeilingTile");
        Box(ceilingRoot.transform, "Tile", new Vector3(0f, 0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), ceilingMat, keepCollider: true);
        GameObject ceilingTile = SaveAsPrefab(ceilingRoot, folder, "Crypt_CeilingTile");

        // Lamp: iron torch, tilted toward the corridor.
        GameObject lampRoot = new GameObject("Crypt_Lamp");
        Box(lampRoot.transform, "Bracket", new Vector3(0f, 0f, 0.03f), new Vector3(0.06f, 0.3f, 0.06f), ironMat);
        CylinderBetween(lampRoot.transform, "Handle", new Vector3(0f, 0f, 0.05f), new Vector3(0f, 0.2f, 0.2f), 0.03f, ironMat);
        GameObject flameGo = Capsule(lampRoot.transform, "Flame", new Vector3(0f, 0.2f, 0.2f), new Vector3(0.12f, 0.2f, 0.12f), flameMat);
        GameObject cryptLightHolder = new GameObject("Light");
        cryptLightHolder.transform.SetParent(lampRoot.transform, false);
        cryptLightHolder.transform.localPosition = new Vector3(0f, 0.25f, 0.22f);
        Light cryptLight = cryptLightHolder.AddComponent<Light>();
        cryptLight.type = LightType.Point;
        cryptLight.shadows = LightShadows.None;
        WallLamp lamp = FinishLamp(lampRoot, folder, cryptLight, flameGo.GetComponent<Renderer>());

        // Locker: standing sarcophagus. Door is two slabs with a look-out gap instead of three slats,
        // but the hinge pivot and its swing are identical to every other theme.
        Locker locker = BuildLockerShell(folder, "Crypt_Locker", stoneMat,
            doorPivot =>
            {
                const float doorWidth = 1.06f;
                const float lowerHeight = 1.5f;
                const float gap = 0.06f;
                const float upperHeight = 2.06f - lowerHeight - gap;
                DoorSlat(doorPivot, "Slab_Lower", doorWidth, lowerHeight, 0.05f, lowerHeight * 0.5f, woodMat);
                DoorSlat(doorPivot, "Slab_Upper", doorWidth, upperHeight, 0.05f, lowerHeight + gap + upperHeight * 0.5f, woodMat);
            },
            root =>
            {
                Box(root.transform, "BevelL", new Vector3(-0.53f, 1.05f, 0.62f), new Vector3(0.04f, 2.1f, 0.02f), woodMat);
                Box(root.transform, "BevelR", new Vector3(0.53f, 1.05f, 0.62f), new Vector3(0.04f, 2.1f, 0.02f), woodMat);
            });

        List<PropPiece> props = new List<PropPiece>();

        // ---- F70: additional props (plan section 3.3, Crypt table) ----

        Material cobwebMat = LoadOrCreateDecalMat(folder, "Crypt_Cobweb", EnsureDecalTexture("Decal_Grime"), new Color(0.5f, 0.5f, 0.48f), 0.05f);
        GameObject cobwebRoot = new GameObject("Cobweb");
        for (int wi = 0; wi < 4; wi++)
        {
            CreateDecalQuad(cobwebRoot.transform, $"Strand{wi}", cobwebMat, true, new Vector3(0f, -0.05f, 0f), Quaternion.Euler(0f, wi * 45f, 0f), 0.5f);
        }
        props.Add(FinishProp(cobwebRoot, folder, PropPiece.MountKind.Ceiling, 0.5f, 1f));

        props.AddRange(BuildCommonFloorProps());
        props.AddRange(BuildAlchemistProps(ThemeSets[2])); // F79
        props.AddRange(BuildPackProps(PackCrypt)); // F85

        CommonDecalMaterials cryptCommonDecals = GetCommonDecalMaterials();
        Material cryptGrimeMat = LoadOrCreateDecalMat(folder, "Crypt_Grime", EnsureDecalTexture("Decal_Grime"), new Color(0.08f, 0.1f, 0.06f), 0.05f);
        Material cryptDripMat = LoadOrCreateDecalMat(folder, "Crypt_Drip", EnsureDecalTexture("Decal_Drip"), new Color(0.1f, 0.12f, 0.07f), 0.1f);
        Material cryptFloorGrimeMat = LoadOrCreateDecalMat(folder, "Crypt_FloorGrime", EnsureDecalTexture("Decal_Grime"), new Color(0.07f, 0.09f, 0.05f), 0.05f);
        props.AddRange(BuildThemeDecals(folder, "Crypt", cryptCommonDecals, cryptGrimeMat, cryptDripMat, cryptFloorGrimeMat));

        // ---- F70: set pieces (plan section 3.4) ----

        SetPiece BuildCryptShrine()
        {
            GameObject root = new GameObject("Crypt_Shrine");
            DressShrine(root); // pack altar, candles and skulls (F89)
            CreateDecalQuad(root.transform, "DripDecal", cryptCommonDecals.BloodPool, false, new Vector3(0f, 2.1f, WallDecalInset), Quaternion.identity, 0.9f);

            GameObject lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.1f, 0.4f);
            Light shrineLight = lightGo.AddComponent<Light>();
            Color candleFlameColor = new Color(1.0f, 0.55f, 0.2f);
            shrineLight.type = LightType.Point;
            shrineLight.range = 3.5f;
            shrineLight.intensity = 0.7f;
            shrineLight.color = candleFlameColor;
            shrineLight.shadows = LightShadows.None;
            ConfigureFlicker(root, FlickerLight.FlickerMode.Candle, shrineLight, null);

            return FinishSetPiece(root, folder, width: 1.6f, depth: 0.9f, weight: 1f);
        }

        List<SetPiece> setPieces = new List<SetPiece>
        {
            BuildCryptShrine(),
            BuildFallenRunnerSetPiece(cryptCommonDecals)
        };
        setPieces.AddRange(BuildPackSetPieces(PackCrypt)); // F85

        DustMotes dust = BuildDustPrefab(folder, "Crypt_Dust", new Color(0.7f, 0.68f, 0.6f), 50f, 0.015f, 0.03f);

        return new ThemeAssets
        {
            DisplayName = "The Crypt",
            Ambient = new Color(0.07f, 0.06f, 0.05f),
            FogColor = new Color(0.02f, 0.015f, 0.01f),
            FogDensityScale = 1.1f,
            LampColor = flameColor,
            FaultyLampChance = 0.50f,
            WallPropChance = 0.4f,
            CeilingPropChance = 0.25f,
            WallMaterial = stoneMat,
            Wall = wall,
            Pillar = pillar,
            FloorTile = floorTile,
            CeilingTile = ceilingTile,
            Lamp = lamp,
            Locker = locker,
            Props = props.ToArray(),
            FloorPropChance = 0.35f,
            WallDecalChance = 0.5f,
            FloorDecalChance = 0.35f,
            MaxDecalsPerCell = 2,
            SetPieces = setPieces.ToArray(),
            SetPieceCount = 3,
            Dust = dust
        };
    }

    // ---------------------------------------------------------------- theme 2: Boiler Deck

    private static ThemeAssets BuildBoilerDeck()
    {
        const string folder = ThemesRoot + "/2_BoilerDeck";
        const string tiles = "Assets/SourceFiles/Textures/Repeating Tiles";

        Material wallMat = LoadOrCreateMat(folder, "Boiler_Wall", new Color(0.42f, 0.30f, 0.24f), $"{tiles}/Tile_Hexagon_Grey_Albedo.png", $"{tiles}/Tile_Hexagon_normal.png", new Vector2(2f, 2f), null);
        Material floorMat = LoadOrCreateMat(folder, "Boiler_Floor", new Color(0.22f, 0.20f, 0.18f), $"{tiles}/Tile_Hexagon_Albedo.png", $"{tiles}/Tile_Hexagon_normal.png", new Vector2(3f, 3f), null);
        Material ceilingMat = LoadOrCreateMat(folder, "Boiler_Ceiling", new Color(0.12f, 0.10f, 0.09f), null, null, Vector2.one, null);
        Material ironMat = LoadOrCreateMat(folder, "Boiler_Iron", new Color(0.20f, 0.19f, 0.18f), null, null, Vector2.one, null, smoothness: 0.3f);
        Material rustMat = LoadOrCreateMat(folder, "Boiler_Rust", new Color(0.45f, 0.24f, 0.14f), null, null, Vector2.one, null);
        Material pillarMat = LoadOrCreateMat(folder, "Boiler_Pillar", new Color(0.42f, 0.30f, 0.24f), null, null, Vector2.one, null); // F90: wall plates at a column's tiling
        Color glassColor = new Color(1.0f, 0.62f, 0.30f);
        Material glassMat = LoadOrCreateMat(folder, "Boiler_Glass", glassColor, null, null, Vector2.one, glassColor * 2.5f);

        GameObject wallRoot = new GameObject("Boiler_Wall");
        Box(wallRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(5f, 4f, 0.5f), wallMat, keepCollider: true);
        WallPiece wall = FinishWall(wallRoot, folder);

        GameObject pillarRoot = new GameObject("Boiler_Pillar");
        Box(pillarRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(0.7f, 4f, 0.7f), pillarMat, keepCollider: true);
        GameObject pillar = SaveAsPrefab(pillarRoot, folder, "Boiler_Pillar");

        GameObject floorRoot = new GameObject("Boiler_FloorTile");
        Box(floorRoot.transform, "Tile", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), floorMat, keepCollider: true);
        GameObject floorTile = SaveAsPrefab(floorRoot, folder, "Boiler_FloorTile");

        GameObject ceilingRoot = new GameObject("Boiler_CeilingTile");
        Box(ceilingRoot.transform, "Tile", new Vector3(0f, 0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), ceilingMat, keepCollider: true);
        GameObject ceilingTile = SaveAsPrefab(ceilingRoot, folder, "Boiler_CeilingTile");

        // Lamp: caged bulkhead.
        GameObject lampRoot = new GameObject("Boiler_Lamp");
        CylinderRH(lampRoot.transform, "Base", new Vector3(0f, 0f, 0.03f), 0.14f, 0.06f, ironMat);
        GameObject glassGo = Sphere(lampRoot.transform, "Glass", new Vector3(0f, 0f, 0.16f), Vector3.one * 0.22f, glassMat);
        for (int i = 0; i < 3; i++)
        {
            float angle = i * 60f;
            Vector3 offset = Quaternion.Euler(0f, 0f, angle) * new Vector3(0.12f, 0f, 0f);
            GameObject bar = Box(lampRoot.transform, "CageBar", new Vector3(offset.x, offset.y, 0.16f), new Vector3(0.02f, 0.3f, 0.02f), ironMat);
            bar.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
        GameObject boilerLightHolder = new GameObject("Light");
        boilerLightHolder.transform.SetParent(lampRoot.transform, false);
        boilerLightHolder.transform.localPosition = new Vector3(0f, 0f, 0.16f);
        Light boilerLight = boilerLightHolder.AddComponent<Light>();
        boilerLight.type = LightType.Point;
        boilerLight.shadows = LightShadows.None;
        WallLamp lamp = FinishLamp(lampRoot, folder, boilerLight, glassGo.GetComponent<Renderer>());

        // Locker: steel locker with vents and a handle.
        Locker locker = BuildLockerShell(folder, "Boiler_Locker", ironMat,
            doorPivot =>
            {
                BuildDoorSlats(doorPivot, 1.06f, 2.06f, 0.04f, ironMat);
                Box(doorPivot, "Vent1", new Vector3(0.53f, 0.4f, 0.045f), new Vector3(0.5f, 0.05f, 0.02f), ironMat);
                Box(doorPivot, "Vent2", new Vector3(0.53f, 0.5f, 0.045f), new Vector3(0.5f, 0.05f, 0.02f), ironMat);
                Box(doorPivot, "Handle", new Vector3(0.98f, 1.0f, 0.05f), new Vector3(0.03f, 0.12f, 0.02f), rustMat);
            });

        List<PropPiece> props = new List<PropPiece>();

        // ---- F70: additional props (plan section 3.3, Boiler Deck table) ----

        props.AddRange(BuildCommonFloorProps());
        props.AddRange(BuildAlchemistProps(ThemeSets[1])); // F79
        props.AddRange(BuildPackProps(PackBoiler)); // F85

        CommonDecalMaterials boilerCommonDecals = GetCommonDecalMaterials();
        Material boilerGrimeMat = LoadOrCreateDecalMat(folder, "Boiler_Grime", EnsureDecalTexture("Decal_Grime"), new Color(0.05f, 0.04f, 0.03f), 0.2f);
        Material boilerDripMat = LoadOrCreateDecalMat(folder, "Boiler_Drip", EnsureDecalTexture("Decal_Drip"), new Color(0.08f, 0.05f, 0.02f), 0.3f);
        Material boilerFloorGrimeMat = LoadOrCreateDecalMat(folder, "Boiler_FloorGrime", EnsureDecalTexture("Decal_Grime"), new Color(0.04f, 0.03f, 0.02f), 0.9f);
        props.AddRange(BuildThemeDecals(folder, "Boiler", boilerCommonDecals, boilerGrimeMat, boilerDripMat, boilerFloorGrimeMat));

        // ---- F70: set pieces (plan section 3.4) ----

        SetPiece BuildBoilerSparks()
        {
            GameObject root = new GameObject("Boiler_Sparks");
            DressSparks(root); // Socket Pack fuse box (F89)
            Color sparkColor = new Color(1.0f, 0.75f, 0.2f);
            AddLocalParticles(root.transform, folder, "Boiler_Sparks_FX", sparkColor, 3f, 0.15f, 0.35f, 0.02f, 0.04f, 1.5f, new Vector3(0.05f, 0.05f, 0.05f));

            GameObject lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0.1f, 1.3f, 0.2f);
            Light sparkLight = lightGo.AddComponent<Light>();
            sparkLight.type = LightType.Point;
            sparkLight.range = 3f;
            sparkLight.intensity = 0.7f;
            sparkLight.color = sparkColor;
            sparkLight.shadows = LightShadows.None;
            ConfigureFlicker(root, FlickerLight.FlickerMode.Spark, sparkLight, null);

            Material scorchMat = LoadOrCreateDecalMat(folder, "Boiler_Scorch", EnsureDecalTexture("Decal_Grime"), new Color(0.02f, 0.02f, 0.02f), 0.05f);
            CreateDecalQuad(root.transform, "ScorchDecal", scorchMat, false, new Vector3(0f, 1.2f, 0.21f), Quaternion.identity, 0.9f);
            return FinishSetPiece(root, folder, width: 1.2f, depth: 0.4f, weight: 1f);
        }

        List<SetPiece> setPieces = new List<SetPiece>
        {
            BuildBoilerSparks(),
            BuildFallenRunnerSetPiece(boilerCommonDecals)
        };
        setPieces.AddRange(BuildPackSetPieces(PackBoiler)); // F85

        DustMotes dust = BuildDustPrefab(folder, "Boiler_Dust", new Color(0.6f, 0.5f, 0.4f), 35f, 0.015f, 0.03f,
            extraChildren: dustRoot => AddLocalParticles(dustRoot.transform, folder, "Boiler_Embers", new Color(1.0f, 0.55f, 0.2f), 1.5f, 4f, 7f, 0.01f, 0.01f, 0.2f, new Vector3(7f, 3f, 7f)));

        return new ThemeAssets
        {
            DisplayName = "Boiler Deck",
            Ambient = new Color(0.09f, 0.07f, 0.06f),
            FogColor = new Color(0.03f, 0.02f, 0.015f),
            FogDensityScale = 1.0f,
            LampColor = glassColor,
            FaultyLampChance = 0.30f,
            WallPropChance = 0.45f,
            CeilingPropChance = 0.3f,
            WallMaterial = wallMat,
            Wall = wall,
            Pillar = pillar,
            FloorTile = floorTile,
            CeilingTile = ceilingTile,
            Lamp = lamp,
            Locker = locker,
            Props = props.ToArray(),
            FloorPropChance = 0.3f,
            WallDecalChance = 0.45f,
            FloorDecalChance = 0.3f,
            MaxDecalsPerCell = 2,
            SetPieces = setPieces.ToArray(),
            SetPieceCount = 3,
            Dust = dust
        };
    }

    // ---------------------------------------------------------------- theme 4: the Lab

    private static ThemeAssets BuildLab()
    {
        const string folder = ThemesRoot + "/4_Lab";
        const string tiles = "Assets/SourceFiles/Textures/Repeating Tiles";
        const string grid = "Assets/SourceFiles/Textures/Grid";

        Material wallMat = LoadOrCreateMat(folder, "Lab_Wall", new Color(0.15f, 0.16f, 0.18f), $"{tiles}/Circuit_Albedo.png", $"{tiles}/Circuit_Normal.png", new Vector2(2f, 2f), null);
        Material floorMat = LoadOrCreateMat(folder, "Lab_Floor", new Color(0.12f, 0.12f, 0.14f), $"{grid}/Grid_01_BaseMap.png", $"{grid}/Grid_01_Normal.png", new Vector2(2f, 2f), null);
        Material ceilingMat = LoadOrCreateMat(folder, "Lab_Ceiling", new Color(0.10f, 0.10f, 0.11f), null, null, Vector2.one, null);
        Material metalMat = LoadOrCreateMat(folder, "Lab_Metal", new Color(0.22f, 0.23f, 0.26f), null, null, Vector2.one, null, smoothness: 0.35f);
        Material pillarMat = LoadOrCreateMat(folder, "Lab_Pillar", new Color(0.15f, 0.16f, 0.18f), null, null, Vector2.one, null); // F90: wall panels at a column's tiling
        Color glassColor = new Color(1.0f, 0.45f, 0.20f);
        Material glassMat = LoadOrCreateMat(folder, "Lab_Glass", glassColor, null, null, Vector2.one, glassColor * 2.5f);
        Color ledColor = new Color(0.2f, 1.0f, 0.9f);
        Material ledMat = LoadOrCreateMat(folder, "Lab_Led", ledColor, null, null, Vector2.one, ledColor * 2f);
        Material screenMat = LoadOrCreateMat(folder, "Lab_Screen", new Color(0.02f, 0.02f, 0.03f), null, null, Vector2.one, null, smoothness: 0.6f);

        GameObject wallRoot = new GameObject("Lab_Wall");
        Box(wallRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(5f, 4f, 0.5f), wallMat, keepCollider: true);
        WallPiece wall = FinishWall(wallRoot, folder);

        GameObject pillarRoot = new GameObject("Lab_Pillar");
        Box(pillarRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(0.7f, 4f, 0.7f), pillarMat, keepCollider: true);
        Box(pillarRoot.transform, "LedStrip", new Vector3(0f, 2f, 0.36f), new Vector3(0.02f, 3.6f, 0.02f), ledMat);
        GameObject pillar = SaveAsPrefab(pillarRoot, folder, "Lab_Pillar");

        GameObject floorRoot = new GameObject("Lab_FloorTile");
        Box(floorRoot.transform, "Tile", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), floorMat, keepCollider: true);
        GameObject floorTile = SaveAsPrefab(floorRoot, folder, "Lab_FloorTile");

        GameObject ceilingRoot = new GameObject("Lab_CeilingTile");
        Box(ceilingRoot.transform, "Tile", new Vector3(0f, 0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), ceilingMat, keepCollider: true);
        GameObject ceilingTile = SaveAsPrefab(ceilingRoot, folder, "Lab_CeilingTile");

        // Lamp: red emergency box.
        GameObject lampRoot = new GameObject("Lab_Lamp");
        Box(lampRoot.transform, "Housing", new Vector3(0f, 0f, 0.07f), new Vector3(0.32f, 0.16f, 0.14f), metalMat);
        GameObject glassGo = Box(lampRoot.transform, "Glass", new Vector3(0f, 0f, 0.15f), new Vector3(0.26f, 0.1f, 0.02f), glassMat);
        GameObject labLightHolder = new GameObject("Light");
        labLightHolder.transform.SetParent(lampRoot.transform, false);
        labLightHolder.transform.localPosition = new Vector3(0f, 0f, 0.15f);
        Light labLight = labLightHolder.AddComponent<Light>();
        labLight.type = LightType.Point;
        labLight.shadows = LightShadows.None;
        labLight.range = 14f;
        labLight.intensity = 1.35f;
        WallLamp lamp = FinishLamp(lampRoot, folder, labLight, glassGo.GetComponent<Renderer>());

        // Locker: containment pod with an LED strip and a small screen.
        Locker locker = BuildLockerShell(folder, "Lab_Locker", metalMat,
            doorPivot =>
            {
                BuildDoorSlats(doorPivot, 1.06f, 2.06f, 0.04f, metalMat);
                Box(doorPivot, "LedStrip", new Vector3(1.02f, 1.0f, 0.045f), new Vector3(0.02f, 1.4f, 0.01f), ledMat);
                Box(doorPivot, "Screen", new Vector3(0.53f, 1.3f, 0.045f), new Vector3(0.2f, 0.12f, 0.01f), screenMat);
            });

        List<PropPiece> props = new List<PropPiece>();

        // ---- F70: additional props (plan section 3.3, Lab table) ----

        props.AddRange(BuildCommonFloorProps());
        props.AddRange(BuildAlchemistProps(ThemeSets[3])); // F79
        props.AddRange(BuildPackProps(PackLab)); // F85

        CommonDecalMaterials labCommonDecals = GetCommonDecalMaterials();
        Material labGrimeMat = LoadOrCreateDecalMat(folder, "Lab_Grime", EnsureDecalTexture("Decal_Grime"), new Color(0.1f, 0.15f, 0.13f), 0.2f);
        Material labDripMat = LoadOrCreateDecalMat(folder, "Lab_Drip", EnsureDecalTexture("Decal_Drip"), new Color(0.1f, 0.3f, 0.15f), 0.4f);
        Material labFloorGrimeMat = LoadOrCreateDecalMat(folder, "Lab_FloorGrime", EnsureDecalTexture("Decal_Grime"), new Color(0.08f, 0.09f, 0.09f), 0.2f);
        // Lab halves blood weights: clean-room with rare violence (decision, plan section 3.2).
        props.AddRange(BuildThemeDecals(folder, "Lab", labCommonDecals, labGrimeMat, labDripMat, labFloorGrimeMat, bloodWeightScale: 0.5f));

        // ---- F70: set pieces (plan section 3.4) ----

        List<SetPiece> setPieces = new List<SetPiece>
        {
            BuildFallenRunnerSetPiece(labCommonDecals)
        };
        setPieces.AddRange(BuildPackSetPieces(PackLab)); // F85

        DustMotes dust = BuildDustPrefab(folder, "Lab_Dust", new Color(0.85f, 0.9f, 0.9f), 12f, 0.015f, 0.03f);

        return new ThemeAssets
        {
            DisplayName = "The Lab",
            Ambient = new Color(0.036f, 0.032f, 0.038f),
            FogColor = new Color(0.038f, 0.034f, 0.040f),
            FogDensityScale = 0.40f,
            LampColor = glassColor,
            FaultyLampChance = 0.40f,
            WallPropChance = 0.4f,
            CeilingPropChance = 0.3f,
            WallMaterial = wallMat,
            Wall = wall,
            Pillar = pillar,
            FloorTile = floorTile,
            CeilingTile = ceilingTile,
            Lamp = lamp,
            Locker = locker,
            Props = props.ToArray(),
            FloorPropChance = 0.22f,
            WallDecalChance = 0.35f,
            FloorDecalChance = 0.2f,
            MaxDecalsPerCell = 2,
            SetPieces = setPieces.ToArray(),
            SetPieceCount = 3,
            Dust = dust
        };
    }

    // ---------------------------------------------------------------- theme 5: the Hollow

    private static ThemeAssets BuildHollow()
    {
        const string folder = ThemesRoot + "/5_Hollow";
        const string tiles = "Assets/SourceFiles/Textures/Repeating Tiles";

        Material wallMat = LoadOrCreateMat(folder, "Hollow_Wall", new Color(0.10f, 0.09f, 0.11f), $"{tiles}/Runes_Albedo.png", $"{tiles}/Runes_Normal.png", new Vector2(2f, 2f), new Color(0.06f, 0.02f, 0.10f));
        Material floorMat = LoadOrCreateMat(folder, "Hollow_Floor", new Color(0.04f, 0.04f, 0.05f), null, null, Vector2.one, null, smoothness: 0.4f);
        Material ceilingMat = LoadOrCreateMat(folder, "Hollow_Ceiling", new Color(0.02f, 0.02f, 0.025f), null, null, Vector2.one, null);
        Material woodMat = LoadOrCreateMat(folder, "Hollow_Wood", new Color(0.06f, 0.05f, 0.05f), null, null, Vector2.one, null);
        Color glassColor = new Color(1.0f, 0.90f, 0.75f);
        Material glassMat = LoadOrCreateMat(folder, "Hollow_Glass", glassColor, null, null, Vector2.one, glassColor * 2.5f);
        Material mirrorMat = LoadOrCreateMat(folder, "Hollow_Mirror", new Color(0.05f, 0.05f, 0.06f), null, null, Vector2.one, null, smoothness: 0.9f);
        if (mirrorMat.HasProperty("_Metallic")) mirrorMat.SetFloat("_Metallic", 0.8f);

        // Wall: body only, no dressing - the runes carry it.
        GameObject wallRoot = new GameObject("Hollow_Wall");
        Box(wallRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(5f, 4f, 0.5f), wallMat, keepCollider: true);
        WallPiece wall = FinishWall(wallRoot, folder);

        GameObject pillarRoot = new GameObject("Hollow_Pillar");
        Box(pillarRoot.transform, "Body", new Vector3(0f, 2f, 0f), new Vector3(0.6f, 4f, 0.6f), wallMat, keepCollider: true);
        GameObject pillar = SaveAsPrefab(pillarRoot, folder, "Hollow_Pillar");

        GameObject floorRoot = new GameObject("Hollow_FloorTile");
        Box(floorRoot.transform, "Tile", new Vector3(0f, -0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), floorMat, keepCollider: true);
        GameObject floorTile = SaveAsPrefab(floorRoot, folder, "Hollow_FloorTile");

        GameObject ceilingRoot = new GameObject("Hollow_CeilingTile");
        Box(ceilingRoot.transform, "Tile", new Vector3(0f, 0.1f, 0f), new Vector3(TileSize, 0.2f, TileSize), ceilingMat, keepCollider: true);
        GameObject ceilingTile = SaveAsPrefab(ceilingRoot, folder, "Hollow_CeilingTile");

        // Lamp: bare bulb hanging off an arm. Root stays at the wall mount; the 0.5 m offset to the bulb
        // is within ExposureAt's distance tolerance (decision/plan note under Theme 5's lamp row).
        GameObject lampRoot = new GameObject("Hollow_Lamp");
        CylinderBetween(lampRoot.transform, "Arm", new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0.5f), 0.02f, woodMat);
        CylinderBetween(lampRoot.transform, "Wire", new Vector3(0f, 0f, 0.5f), new Vector3(0f, -0.3f, 0.5f), 0.008f, woodMat);
        GameObject glassGo = Sphere(lampRoot.transform, "Bulb", new Vector3(0f, -0.3f, 0.5f), Vector3.one * 0.14f, glassMat);
        GameObject hollowLightHolder = new GameObject("Light");
        hollowLightHolder.transform.SetParent(lampRoot.transform, false);
        hollowLightHolder.transform.localPosition = new Vector3(0f, -0.3f, 0.5f);
        Light hollowLight = hollowLightHolder.AddComponent<Light>();
        hollowLight.type = LightType.Point;
        hollowLight.shadows = LightShadows.None;
        WallLamp lamp = FinishLamp(lampRoot, folder, hollowLight, glassGo.GetComponent<Renderer>());

        // Locker: black wardrobe. Left panel is the hinged, slatted door (narrower than the standard
        // width); right is a fixed mirror strip on the body itself, not on the swinging door.
        Locker locker = BuildLockerShell(folder, "Hollow_Locker", woodMat,
            doorPivot =>
            {
                BuildDoorSlats(doorPivot, 0.74f, 2.06f, 0.04f, woodMat);
            },
            root =>
            {
                Box(root.transform, "MirrorPanel", new Vector3(0.38f, 1.05f, 0.62f), new Vector3(0.3f, 2.0f, 0.03f), mirrorMat);
            });

        List<PropPiece> props = new List<PropPiece>();

        // ---- F70: additional props (plan section 3.3, Hollow table) ----

        Color lanternGlow = new Color(0.9f, 0.7f, 0.35f);
        props.AddRange(BuildCommonFloorProps());
        props.AddRange(BuildAlchemistProps(ThemeSets[4])); // F79
        props.AddRange(BuildPackProps(PackHollow)); // F85

        // Hollow doubles Handprint/ClawMarks weight (decision, plan section 3.2).
        CommonDecalMaterials hollowCommonDecals = GetCommonDecalMaterials();
        Material hollowGrimeMat = LoadOrCreateDecalMat(folder, "Hollow_Grime", EnsureDecalTexture("Decal_Grime"), new Color(0.03f, 0.03f, 0.035f), 0.05f);
        Material hollowDripMat = LoadOrCreateDecalMat(folder, "Hollow_Drip", EnsureDecalTexture("Decal_Drip"), new Color(0.05f, 0.02f, 0.02f), 0.1f);
        Material hollowFloorGrimeMat = LoadOrCreateDecalMat(folder, "Hollow_FloorGrime", EnsureDecalTexture("Decal_Grime"), new Color(0.02f, 0.02f, 0.025f), 0.05f);
        props.AddRange(BuildThemeDecals(folder, "Hollow", hollowCommonDecals, hollowGrimeMat, hollowDripMat, hollowFloorGrimeMat, handClawWeightScale: 2f));

        // ---- F70: set pieces (plan section 3.4) ----

        SetPiece BuildHollowCircle()
        {
            GameObject root = new GameObject("Hollow_Circle");
            DressCircle(root); // pack candles and a baby doll (F89)

            for (int hI = 0; hI < 3; hI++)
            {
                float hx = -0.6f + hI * 0.6f;
                CreateDecalQuad(root.transform, $"Handprint{hI}", hollowCommonDecals.Handprint, false, new Vector3(hx, 1.4f, WallDecalInset), Quaternion.Euler(0f, 0f, hI * 15f), 0.28f);
            }

            GameObject lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0.9f);
            Light circleLight = lightGo.AddComponent<Light>();
            circleLight.type = LightType.Point;
            circleLight.range = 3f;
            circleLight.intensity = 0.6f;
            circleLight.color = lanternGlow;
            circleLight.shadows = LightShadows.None;
            ConfigureFlicker(root, FlickerLight.FlickerMode.Candle, circleLight, null);

            return FinishSetPiece(root, folder, width: 1.6f, depth: 1.7f, weight: 1f);
        }

        List<SetPiece> setPieces = new List<SetPiece>
        {
            BuildHollowCircle(),
            BuildFallenRunnerSetPiece(hollowCommonDecals)
        };
        setPieces.AddRange(BuildPackSetPieces(PackHollow)); // F85

        // Hollow dust is slower (ash) - lower startSpeed than the shared default.
        DustMotes dust = BuildDustPrefab(folder, "Hollow_Dust", new Color(0.55f, 0.55f, 0.55f), 40f, 0.015f, 0.03f, speedMin: 0.01f, speedMax: 0.03f);

        return new ThemeAssets
        {
            DisplayName = "The Hollow",
            Ambient = new Color(0.035f, 0.03f, 0.045f),
            FogColor = new Color(0.01f, 0.01f, 0.015f),
            FogDensityScale = 1.0f,
            LampColor = glassColor,
            FaultyLampChance = 0.60f,
            WallPropChance = 0.3f,
            CeilingPropChance = 0.2f,
            WallMaterial = wallMat,
            Wall = wall,
            Pillar = pillar,
            FloorTile = floorTile,
            CeilingTile = ceilingTile,
            Lamp = lamp,
            Locker = locker,
            Props = props.ToArray(),
            FloorPropChance = 0.35f,
            WallDecalChance = 0.5f,
            FloorDecalChance = 0.35f,
            MaxDecalsPerCell = 2,
            SetPieces = setPieces.ToArray(),
            SetPieceCount = 3,
            Dust = dust
        };
    }
}
