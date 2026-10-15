using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace KeepItLit.Tests
{
    /// <summary>
    /// F118: every star / key / fuse / button / exit gate is reachable (grid + NavMesh) for many seeds x floors 1-5 and DESCENT depths. Slow:
    /// loads the maze 40 + 15 times inside one Play session. Needs a clean open scene (it opens GetStarted_Scene, never discarding edits).
    /// </summary>
    [Category("Maze")]
    public class MazeReachabilityTests
    {
        private const string ScenePath = "Assets/Scenes/GetStarted_Scene.unity";

        [UnityTest]
        public IEnumerator StoryFloors() { yield return RunMode(false); }

        [UnityTest]
        public IEnumerator Descent() { yield return RunMode(true); }

        private static IEnumerator RunMode(bool descent)
        {
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                Assert.Inconclusive("save or discard the open scene first");
                yield break;
            }
            EditorSceneManager.OpenScene(ScenePath);
            MazeGenerator authored = Object.FindAnyObjectByType<MazeGenerator>();
            Assert.IsNotNull(authored, "no MazeGenerator in " + ScenePath);
            string bad = MazeTestDriver.NonDefaultDebugField(authored);
            if (bad != null)
            {
                Assert.Inconclusive("MazeGenerator." + bad + " is not at its default; the test would not measure a normal run");
                yield break;
            }

            string dir = MazeTestDriver.BeginRedirect();
            MazeTestDriver.Result result = new MazeTestDriver.Result();
            try
            {
                yield return new EnterPlayMode();
                yield return MazeTestDriver.Run(descent, result);
                yield return new ExitPlayMode();
            }
            finally
            {
                MazeTestDriver.EndRedirect(dir);
            }

            Debug.Log("Maze reachability " + (descent ? "DESCENT" : "story") + ": " + result.Coverage());
            foreach (string note in result.Squeeze) Debug.Log("squeeze note: " + note);
            if (!descent) Assert.Greater(result.GateFloors, 0, "floor 5 always has gates (ForceGates)");
            Assert.IsEmpty(result.Failures, string.Join("\n", result.Failures));
        }
    }
}
