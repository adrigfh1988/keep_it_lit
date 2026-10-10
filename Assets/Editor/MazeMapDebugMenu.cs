using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// LIGHTS OUT &gt; Debug &gt; Print Maze Map: draws the running maze as ASCII (MazeGenerator.DebugMap - walls, doors, stars, keys,
/// buttons, start, player, hunter), logs it, copies it to the clipboard and writes Logs/MazeMap.txt, since the Console's font is
/// not monospace. Play mode only: the maze is built at runtime. Touches no content.
/// </summary>
public static class MazeMapDebugMenu
{
    private const string MenuPath = "LIGHTS OUT/Debug/Print Maze Map";
    private const string FilePath = "Logs/MazeMap.txt";

    [MenuItem(MenuPath, priority = 201)]
    private static void PrintMazeMap()
    {
        MazeGenerator maze = Object.FindAnyObjectByType<MazeGenerator>();
        if (maze == null)
        {
            Debug.LogWarning("Print Maze Map: there is no MazeGenerator in the open scene.");
            return;
        }

        string map = maze.DebugMap();
        GUIUtility.systemCopyBuffer = map;
        File.WriteAllText(FilePath, map);
        Debug.Log($"Maze map copied to the clipboard and written to {FilePath} (open it in a monospace editor).\n{map}", maze);
    }

    [MenuItem(MenuPath, true)]
    private static bool PrintMazeMapValidate() => EditorApplication.isPlaying;
}
