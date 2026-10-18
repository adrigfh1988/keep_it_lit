using System.Collections;
using UnityEngine;

/// <summary>
/// Animates the skeletal bones of a rigged bathtub-with-bodies set piece (Bathtub_Bodies_HP) or hanging body
/// (Deadbody_Hanging_HP) when the player approaches or shines their flashlight on it, plus subtle eerie breathing/twitching.
/// Attached automatically by FloorThemeBuilder.Pack.cs onto Pack_BloodyBath and Pack_HangingBody variants.
/// </summary>
public class HorrorPropAnimator : MonoBehaviour
{
    public enum PropKind { HangingBody, BathtubBodies }

    [SerializeField] private PropKind kind;
    [SerializeField] private Transform torsoBone;
    [SerializeField] private Transform headBone;
    [SerializeField] private Transform secondaryBone;
    [SerializeField] private Transform armBone;

    [Header("Scare & Twitch Settings")]
    [SerializeField] private float scareTriggerRadius = 2.6f;
    [SerializeField] private float idleTwitchIntervalMin = 7f;
    [SerializeField] private float idleTwitchIntervalMax = 16f;

    private Transform _player;
    private Flashlight _torch;
    private Quaternion _torsoRest;
    private Quaternion _headRest;
    private Quaternion _secondaryRest;
    private Quaternion _armRest;
    private bool _scared;
    private bool _animating;
    private float _nextTwitchTime;
    private float _breathSeed;

    public void BindBones(PropKind propKind, Transform torso, Transform head, Transform secondary, Transform arm)
    {
        kind = propKind;
        torsoBone = torso;
        headBone = head;
        secondaryBone = secondary;
        armBone = arm;
    }

    private void Awake()
    {
        CacheBonesIfNeeded();
        if (torsoBone != null) _torsoRest = torsoBone.localRotation;
        if (headBone != null) _headRest = headBone.localRotation;
        if (secondaryBone != null) _secondaryRest = secondaryBone.localRotation;
        if (armBone != null) _armRest = armBone.localRotation;
        _breathSeed = Random.Range(0f, 100f);
        _nextTwitchTime = Time.time + Random.Range(idleTwitchIntervalMin, idleTwitchIntervalMax);
    }

    private void CacheBonesIfNeeded()
    {
        if (torsoBone != null || headBone != null) return;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            switch (t.name)
            {
                case "Torso":
                    kind = PropKind.HangingBody;
                    torsoBone = t;
                    break;
                case "Head":
                    headBone = t;
                    break;
                case "Legs":
                    secondaryBone = t;
                    break;
                case "SeatedBody_Torso":
                    kind = PropKind.BathtubBodies;
                    torsoBone = t;
                    break;
                case "SeatedBody_Head":
                    headBone = t;
                    break;
                case "SlumpedBody_Head":
                    secondaryBone = t;
                    break;
                case "SlumpedBody_Arm":
                    armBone = t;
                    break;
            }
        }
    }

    private void Update()
    {
        if (!GameFlow.IsRunActive || GameOutcome.IsOver) return;
        if (torsoBone == null && headBone == null) return;

        if (_player == null)
        {
            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null)
            {
                _player = pObj.transform;
                _torch = pObj.GetComponentInChildren<Flashlight>(true);
            }
            if (_player == null) return;
        }

        if (_animating) return;

        Vector3 toPlayer = _player.position - transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;

        // Sudden close-range scare lurch / convulsion once per run when the player approaches!
        if (!_scared && dist <= scareTriggerRadius)
        {
            _scared = true;
            StartCoroutine(ScareLurchRoutine());
            return;
        }

        // Occasional jerky twitch
        if (Time.time >= _nextTwitchTime && dist <= 12f)
        {
            _nextTwitchTime = Time.time + Random.Range(idleTwitchIntervalMin, idleTwitchIntervalMax);
            StartCoroutine(TwitchRoutine());
            return;
        }

        // Subtle uneasy breathing / swaying micro-motion on the bones
        float t = Time.time * 1.6f + _breathSeed;
        float s1 = Mathf.Sin(t) * 1.8f;
        float s2 = Mathf.Cos(t * 0.75f) * 2.2f;
        if (torsoBone != null) torsoBone.localRotation = _torsoRest * Quaternion.Euler(s1, 0f, s1 * 0.5f);
        if (headBone != null) headBone.localRotation = _headRest * Quaternion.Euler(s2, s1 * 0.8f, 0f);
        if (armBone != null) armBone.localRotation = _armRest * Quaternion.Euler(s1 * 1.2f, 0f, 0f);
    }

    private IEnumerator ScareLurchRoutine()
    {
        _animating = true;
        Vector3 soundPos = transform.position + Vector3.up * 0.9f;
        if (GameAudio.Library != null)
        {
            AudioSlot slot = kind == PropKind.BathtubBodies ? GameAudio.Library.farDrag : GameAudio.Library.floorCreak;
            GameAudio.PlayAt(slot, soundPos, 0.75f, Random.Range(0.88f, 1.08f), 10f);
        }

        // Violent convulse / head snap
        int spasms = kind == PropKind.BathtubBodies ? 4 : 3;
        for (int i = 0; i < spasms; i++)
        {
            Quaternion tTarget = _torsoRest * Quaternion.Euler(
                Random.Range(-18f, 22f),
                Random.Range(-16f, 16f),
                Random.Range(-20f, 20f));
            Quaternion hTarget = _headRest * Quaternion.Euler(
                Random.Range(-28f, 28f),
                Random.Range(-32f, 32f),
                Random.Range(-25f, 25f));
            Quaternion sTarget = _secondaryRest * Quaternion.Euler(
                Random.Range(-22f, 22f),
                Random.Range(-15f, 15f),
                Random.Range(-18f, 18f));
            Quaternion aTarget = _armRest * Quaternion.Euler(
                Random.Range(-30f, 12f),
                Random.Range(-14f, 14f),
                Random.Range(-22f, 22f));

            float dur = Random.Range(0.07f, 0.13f);
            Quaternion t0 = torsoBone != null ? torsoBone.localRotation : Quaternion.identity;
            Quaternion h0 = headBone != null ? headBone.localRotation : Quaternion.identity;
            Quaternion s0 = secondaryBone != null ? secondaryBone.localRotation : Quaternion.identity;
            Quaternion a0 = armBone != null ? armBone.localRotation : Quaternion.identity;

            for (float el = 0f; el < dur; el += Time.deltaTime)
            {
                float k = el / dur;
                if (torsoBone != null) torsoBone.localRotation = Quaternion.Slerp(t0, tTarget, k);
                if (headBone != null) headBone.localRotation = Quaternion.Slerp(h0, hTarget, k);
                if (secondaryBone != null) secondaryBone.localRotation = Quaternion.Slerp(s0, sTarget, k);
                if (armBone != null) armBone.localRotation = Quaternion.Slerp(a0, aTarget, k);
                yield return null;
            }
        }

        // Hold eerie crooked head tilt briefly, then settle back
        yield return new WaitForSeconds(0.35f);

        Quaternion tf = torsoBone != null ? torsoBone.localRotation : Quaternion.identity;
        Quaternion hf = headBone != null ? headBone.localRotation : Quaternion.identity;
        Quaternion sf = secondaryBone != null ? secondaryBone.localRotation : Quaternion.identity;
        Quaternion af = armBone != null ? armBone.localRotation : Quaternion.identity;
        for (float el = 0f; el < 0.45f; el += Time.deltaTime)
        {
            float k = el / 0.45f;
            if (torsoBone != null) torsoBone.localRotation = Quaternion.Slerp(tf, _torsoRest, k);
            if (headBone != null) headBone.localRotation = Quaternion.Slerp(hf, _headRest, k);
            if (secondaryBone != null) secondaryBone.localRotation = Quaternion.Slerp(sf, _secondaryRest, k);
            if (armBone != null) armBone.localRotation = Quaternion.Slerp(af, _armRest, k);
            yield return null;
        }

        _animating = false;
    }

    private IEnumerator TwitchRoutine()
    {
        _animating = true;
        Quaternion hTarget = _headRest * Quaternion.Euler(Random.Range(-16f, 16f), Random.Range(-22f, 22f), Random.Range(-15f, 15f));
        Quaternion aTarget = _armRest * Quaternion.Euler(Random.Range(-18f, 8f), 0f, Random.Range(-12f, 12f));
        Quaternion h0 = headBone != null ? headBone.localRotation : Quaternion.identity;
        Quaternion a0 = armBone != null ? armBone.localRotation : Quaternion.identity;

        for (float el = 0f; el < 0.08f; el += Time.deltaTime)
        {
            float k = el / 0.08f;
            if (headBone != null) headBone.localRotation = Quaternion.Slerp(h0, hTarget, k);
            if (armBone != null) armBone.localRotation = Quaternion.Slerp(a0, aTarget, k);
            yield return null;
        }
        for (float el = 0f; el < 0.22f; el += Time.deltaTime)
        {
            float k = el / 0.22f;
            if (headBone != null) headBone.localRotation = Quaternion.Slerp(hTarget, _headRest, k);
            if (armBone != null) armBone.localRotation = Quaternion.Slerp(aTarget, _armRest, k);
            yield return null;
        }
        _animating = false;
    }
}
