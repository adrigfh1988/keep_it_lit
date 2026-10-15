using TMPro;
using UnityEngine;

/// <summary>
/// F109 (D8): one character of a sign flickers off in irregular bursts, like a failing tube. The character is chosen by a stable hash of the
/// current text (never a space), so it works in every language and the sign keeps the same dead letter between visits. The effect writes
/// the character vertex alpha in the TMP mesh each LateUpdate (after the TMP rebuild), so it survives a text change or a colour write by
/// another system (IntakeAmbience sign flicker).
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public class SignDeadLetter : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float dimAlpha = 0.1f;
    [SerializeField] private Vector2 restSeconds = new Vector2(2.5f, 8f);

    private TMP_Text _text;
    private Renderer _renderer;
    private int _textHash;
    private int _index = -1;
    private float _nextBurst;
    private int _flicksLeft;
    private float _phaseEnd;
    private bool _off;

    private void Awake()
    {
        _text = GetComponent<TMP_Text>();
        _renderer = GetComponent<Renderer>();
    }

    private void OnEnable()
    {
        _nextBurst = Time.time + Random.Range(restSeconds.x, restSeconds.y);
        _flicksLeft = 0;
        _off = false;
        _index = -1;
    }

    private void LateUpdate()
    {
        if (_text == null || (_renderer != null && !_renderer.isVisible)) return;
        string s = _text.text;
        int hash = s != null ? s.GetHashCode() : 0;
        if (hash != _textHash || _index < 0)
        {
            _textHash = hash;
            _text.ForceMeshUpdate();
            _index = PickCharacter();
        }
        if (_index < 0) return;
        if (FlashSafety.On) { Apply(1f); return; } // F118: the letter stays lit

        float t = Time.time;
        if (_flicksLeft > 0)
        {
            if (t >= _phaseEnd)
            {
                if (_off)
                {
                    _off = false;
                    _flicksLeft--;
                    _phaseEnd = t + Random.Range(0.05f, 0.22f);
                    if (_flicksLeft == 0) _nextBurst = t + Random.Range(restSeconds.x, restSeconds.y);
                }
                else { _off = true; _phaseEnd = t + Random.Range(0.04f, 0.2f); }
            }
        }
        else if (t >= _nextBurst)
        {
            _flicksLeft = Random.Range(1, 4);
            _off = true;
            _phaseEnd = t + Random.Range(0.04f, 0.2f);
        }

        Apply(_off ? dimAlpha : 1f);
    }

    private int PickCharacter()
    {
        TMP_TextInfo info = _text.textInfo;
        if (info == null || info.characterCount == 0) return -1;
        int visible = 0;
        for (int i = 0; i < info.characterCount; i++) if (info.characterInfo[i].isVisible) visible++;
        if (visible == 0) return -1;
        uint h = 2166136261u;
        string s = _text.text ?? "";
        for (int i = 0; i < s.Length; i++) h = (h ^ s[i]) * 16777619u;
        int pick = (int)(h % (uint)visible);
        for (int i = 0; i < info.characterCount; i++)
        {
            if (!info.characterInfo[i].isVisible) continue;
            if (pick-- == 0) return i;
        }
        return -1;
    }

    private void Apply(float factor)
    {
        TMP_TextInfo info = _text.textInfo;
        if (info == null || _index >= info.characterCount) return;
        TMP_CharacterInfo ci = info.characterInfo[_index];
        if (!ci.isVisible) return;
        TMP_MeshInfo mesh = info.meshInfo[ci.materialReferenceIndex];
        if (mesh.colors32 == null || ci.vertexIndex + 3 >= mesh.colors32.Length) return;
        byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(_text.color.a * factor) * 255f);
        for (int k = 0; k < 4; k++)
        {
            Color32 c = mesh.colors32[ci.vertexIndex + k];
            c.a = a;
            mesh.colors32[ci.vertexIndex + k] = c;
        }
        _text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }
}
