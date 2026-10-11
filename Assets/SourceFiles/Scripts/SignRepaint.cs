using TMPro;
using UnityEngine;

/// <summary>
/// F109 (D4 WallPaint): one letter of a painted warning was painted over and redone. A dirty-wall patch covers one character (chosen by a hash of
/// the text, so it follows a language change) and a slightly crooked, fresher copy of that character sits next to it. Pure presentation.
/// </summary>
[DefaultExecutionOrder(150)]
[DisallowMultipleComponent]
public class SignRepaint : MonoBehaviour
{
    [SerializeField] private Transform patch;
    [SerializeField] private TMP_Text redo;

    private TMP_Text _text;
    private int _hash;

    private void Awake() => _text = GetComponent<TMP_Text>();

    private void LateUpdate()
    {
        if (_text == null || patch == null || redo == null) return;
        string s = _text.text ?? "";
        int h = s.GetHashCode() ^ (int)(_text.fontSize * 1000f);
        if (h == _hash) return;
        _hash = h;
        _text.ForceMeshUpdate();
        TMP_TextInfo info = _text.textInfo;
        int n = 0;
        for (int i = 0; i < info.characterCount; i++) if (info.characterInfo[i].isVisible) n++;
        if (n < 3) { patch.gameObject.SetActive(false); redo.gameObject.SetActive(false); return; }
        uint hv = 2166136261u;
        for (int i = 0; i < s.Length; i++) hv = (hv ^ s[i]) * 16777619u;
        int pick = (int)(hv % (uint)n);
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo ci = info.characterInfo[i];
            if (!ci.isVisible) continue;
            if (pick-- != 0) continue;
            Vector3 c = (ci.bottomLeft + ci.topRight) * 0.5f;
            float w = ci.topRight.x - ci.bottomLeft.x, ht = ci.topRight.y - ci.bottomLeft.y;
            patch.gameObject.SetActive(true);
            patch.localPosition = new Vector3(c.x, c.y, patch.localPosition.z);
            patch.localScale = new Vector3(w * 1.5f, ht * 1.35f, 1f);
            redo.gameObject.SetActive(true);
            redo.fontSize = _text.fontSize * 0.92f;
            redo.text = ci.character.ToString();
            redo.transform.localPosition = new Vector3(c.x + w * 0.1f, c.y - ht * 0.08f, redo.transform.localPosition.z);
            redo.transform.localRotation = Quaternion.Euler(0f, 0f, 7f);
            return;
        }
    }
}
