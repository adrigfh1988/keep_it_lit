using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// S2: makes <c>Assets/SourceFiles/Audio/GameMixer.mixer</c> (Master > Music, Effects; MasterVolume / MusicVolume / EffectsVolume exposed) and
/// assigns it to MazeGenerator.gameMixer. Unity has no public API for creating a mixer, so this drives the editor-internal
/// UnityEditor.Audio.AudioMixerController through reflection (CreateMixerControllerAtPath, CreateNewGroup, AddChildToParent, AddExposedParameter).
/// Idempotent: only what is missing is added. Registered in ProjectSetup as "Audio mixer (settings volumes)"; no menu item of its own.
/// If a future Unity version moves those internals, the fallback is by hand: Create > Audio Mixer, add child groups Music and Effects under
/// Master, right-click each group's Volume > Expose, and rename the three parameters to the names in GameSettings.
/// </summary>
public static class GameMixerBuilder
{
    public const string AssetPath = "Assets/SourceFiles/Audio/GameMixer.mixer";

    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Null when everything is in place, otherwise what is missing.</summary>
    public static string Problem()
    {
        AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetPath);
        if (mixer == null) return "GameMixer.mixer does not exist";
        if (mixer.FindMatchingGroups(GameSettings.MusicGroupName).Length == 0) return "the mixer has no Music group";
        if (mixer.FindMatchingGroups(GameSettings.EffectsGroupName).Length == 0) return "the mixer has no Effects group";
        HashSet<string> names = ExposedNames(mixer);
        foreach (string p in new[] { GameSettings.MasterParam, GameSettings.MusicParam, GameSettings.EffectsParam })
        {
            if (!names.Contains(p)) return "the mixer does not expose " + p;
        }

        MazeGenerator maze = UnityEngine.Object.FindAnyObjectByType<MazeGenerator>(FindObjectsInactive.Include);
        if (maze != null)
        {
            SerializedProperty p = new SerializedObject(maze).FindProperty("gameMixer");
            if (p == null || p.objectReferenceValue != mixer) return "MazeGenerator.gameMixer is not assigned";
        }
        return null;
    }

    public static void BuildSilently()
    {
        AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetPath);
        if (mixer == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Audio")) AssetDatabase.CreateFolder("Assets/SourceFiles", "Audio");
            Type controllerType = Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor.CoreModule") ?? typeof(Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerController");
            if (controllerType == null) throw new InvalidOperationException("GameMixerBuilder: UnityEditor.Audio.AudioMixerController not found - create the mixer by hand (see the class comment).");
            controllerType.GetMethod("CreateMixerControllerAtPath", All).Invoke(null, new object[] { AssetPath });
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetPath);
        }
        if (mixer == null) throw new InvalidOperationException("GameMixerBuilder: the mixer asset could not be created.");

        Type type = mixer.GetType(); // AudioMixerController
        object master = type.GetProperty("masterGroup", All).GetValue(mixer);

        object music = FindGroup(mixer, GameSettings.MusicGroupName) ?? NewGroup(mixer, master, GameSettings.MusicGroupName);
        object effects = FindGroup(mixer, GameSettings.EffectsGroupName) ?? NewGroup(mixer, master, GameSettings.EffectsGroupName);

        HashSet<string> exposed = ExposedNames(mixer);
        Expose(mixer, master, GameSettings.MasterParam, exposed);
        Expose(mixer, music, GameSettings.MusicParam, exposed);
        Expose(mixer, effects, GameSettings.EffectsParam, exposed);

        EditorUtility.SetDirty(mixer);
        AssetDatabase.SaveAssets();

        MazeGenerator maze = UnityEngine.Object.FindAnyObjectByType<MazeGenerator>(FindObjectsInactive.Include);
        if (maze != null)
        {
            SerializedObject so = new SerializedObject(maze);
            SerializedProperty p = so.FindProperty("gameMixer");
            if (p != null && p.objectReferenceValue != mixer)
            {
                p.objectReferenceValue = mixer;
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(maze.gameObject.scene); // never auto-save: the user saves with Ctrl+S
            }
        }
    }

    // ---------------------------------------------------------------- reflection helpers

    private static object FindGroup(AudioMixer mixer, string name)
    {
        AudioMixerGroup[] found = mixer.FindMatchingGroups(name);
        return found.Length > 0 ? found[0] : null; // the editor-side group controller is the same object
    }

    private static object NewGroup(AudioMixer mixer, object parent, string name)
    {
        Type type = mixer.GetType();
        object group = type.GetMethod("CreateNewGroup", All).Invoke(mixer, new object[] { name, false });
        type.GetMethod("AddChildToParent", All).Invoke(mixer, new[] { group, parent });
        return group;
    }

    private static void Expose(AudioMixer mixer, object group, string parameterName, HashSet<string> already)
    {
        if (already.Contains(parameterName)) return;
        Type type = mixer.GetType();
        object guid = group.GetType().GetMethod("GetGUIDForVolume", All).Invoke(group, null);

        Type pathType = typeof(Editor).Assembly.GetType("UnityEditor.Audio.AudioGroupParameterPath");
        object path = Activator.CreateInstance(pathType, All, null, new[] { group, guid }, null);
        type.GetMethod("AddExposedParameter", All).Invoke(mixer, new[] { path });

        // The new entry has a generated name ("MyExposedParam"); find it by the volume GUID and rename it.
        PropertyInfo prop = type.GetProperty("exposedParameters", All);
        Array arr = (Array)prop.GetValue(mixer);
        for (int i = 0; i < arr.Length; i++)
        {
            object entry = arr.GetValue(i);
            FieldInfo guidField = entry.GetType().GetField("guid", All);
            if (!guidField.GetValue(entry).Equals(guid)) continue;
            entry.GetType().GetField("name", All).SetValue(entry, parameterName);
            arr.SetValue(entry, i);
        }
        prop.SetValue(mixer, arr);
    }

    private static HashSet<string> ExposedNames(AudioMixer mixer)
    {
        var names = new HashSet<string>();
        SerializedProperty list = new SerializedObject(mixer).FindProperty("m_ExposedParameters");
        if (list == null) return names;
        for (int i = 0; i < list.arraySize; i++)
        {
            SerializedProperty n = list.GetArrayElementAtIndex(i).FindPropertyRelative("name");
            if (n != null) names.Add(n.stringValue);
        }
        return names;
    }
}
