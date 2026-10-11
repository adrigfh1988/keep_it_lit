using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// KEEP IT LIT &gt; Debug &gt; Print Maze Map: draws the running maze as ASCII (MazeGenerator.DebugMap - walls, doors, stars, keys,
/// buttons, start, player, hunter), logs it, copies it to the clipboard and writes Logs/MazeMap.txt, since the Console's font is
/// not monospace. Play mode only: the maze is built at runtime. Touches no content.
/// </summary>
public static class MazeMapDebugMenu
{
    private const string MenuPath = "KEEP IT LIT/Debug/Print Maze Map";
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

    /// <summary>KEEP IT LIT &gt; Debug &gt; Go To Loop Entrance: Play only, moves the player to the floor's loop corridor (MazeGenerator.DebugGoToLoopEntrance).</summary>
    [MenuItem("KEEP IT LIT/Debug/Go To Loop Entrance", priority = 202)]
    private static void GoToLoopEntrance()
    {
        MazeGenerator maze = Object.FindAnyObjectByType<MazeGenerator>();
        Debug.Log(maze != null ? "Go To Loop Entrance: " + maze.DebugGoToLoopEntrance() : "Go To Loop Entrance: there is no MazeGenerator in the open scene.", maze);
    }

    [MenuItem("KEEP IT LIT/Debug/Go To Loop Entrance", true)]
    private static bool GoToLoopEntranceValidate() => EditorApplication.isPlaying;

    /// <summary>KEEP IT LIT &gt; Debug &gt; Go To Exit Gate: Play only, moves the player to a gate that is not yet open (MazeGenerator.DebugGoToExitGate).</summary>
    [MenuItem("KEEP IT LIT/Debug/Go To Exit Gate", priority = 203)]
    private static void GoToExitGate()
    {
        MazeGenerator maze = Object.FindAnyObjectByType<MazeGenerator>();
        Debug.Log(maze != null ? "Go To Exit Gate: " + maze.DebugGoToExitGate() : "Go To Exit Gate: there is no MazeGenerator in the open scene.", maze);
    }

    [MenuItem("KEEP IT LIT/Debug/Go To Exit Gate", true)]
    private static bool GoToExitGateValidate() => EditorApplication.isPlaying;
}
