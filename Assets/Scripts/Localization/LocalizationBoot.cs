using System.Collections;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

/// <summary>
/// S9 (D2): lives alone in Boot.unity, the first scene in the build. Waits for the localization system (startup locale picked by the
/// selector chain, tables preloaded) and then loads the game scene, so nothing ever asks for a string before the tables exist
/// (Addressables cannot block on WebGL). Retry / next floor / back to menu reload GetStarted_Scene directly.
/// </summary>
public class LocalizationBoot : MonoBehaviour
{
    public string nextScene = "GetStarted_Scene";

    private IEnumerator Start()
    {
        yield return LocalizationSettings.InitializationOperation;
        var preload = LocalizationSettings.StringDatabase.PreloadOperation;
        if (!preload.IsDone) yield return preload;
        SceneManager.LoadScene(nextScene);
    }
}
