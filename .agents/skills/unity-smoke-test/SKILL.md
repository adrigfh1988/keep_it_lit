---
name: unity-smoke-test
description: >-
  How to smoke-test a KEEP IT LIT run and verify room lighting/surfaces in Play Mode
  using the Unity CLI (`unity command eval`). Use this skill when testing gameplay,
  sampling Play-mode state, or rendering camera views to verify meshes and lighting.
---

# Smoke-Testing & Play-Mode Lighting Verification via Unity CLI

Always drive the open Unity Editor with the **Unity CLI** (`unity`), never the deprecated `unity-mcp` MCP server.

## 1. Connecting and Starting Play Mode

1. Check connection and state:
   ```powershell
   unity status
   unity pipeline list
   ```
2. To start directly in the Asylum Hub or Maze without navigating menus:
   - Before entering Play mode, set `MazeGenerator`'s private serialized `debugStartInHub`, `debugStartInMaze`, `debugDescent`, or `debugFloor` fields via `UnityEditor.SerializedObject` in a `unity command eval --code '...'` call.
   - Restore those fields after testing (leave `superEasyMode` as found — that is the user's setting).
3. Enter/Exit Play Mode:
   ```powershell
   unity command editor_play
   unity command editor_stop
   ```

## 2. Advancing Frames in Play Mode

- **Critical gotcha:** The Unity Editor does not advance Play-mode frames while in the background.
- Bring the Unity Editor window to the foreground (e.g., via PowerShell `(New-Object -ComObject WScript.Shell).AppActivate(<Unity PID>)` or `unity command set_autotick --enable true`) and verify `UnityEngine.Time.time` advances between samples.

## 3. Evaluating C# & Rendering Views

- `unity command eval --code '<statement block>'` executes a statement block (no `using` directives; fully qualify every type such as `UnityEngine.Object`, `UnityEditor.AssetDatabase`; note `GetInstanceID` is obsolete).
- For longer scripts, use `unity command eval_file --file <path>`.
- For long-running jobs (mass reimports, bakes, builds), pass `--detach` and follow with `unity job status <id>` or `unity job wait <id>`, or raise `--timeout`. If the Editor's main thread is busy for >5s, the CLI call may report a timeout while the work continues — check `Logs/Editor.log` before retrying.
- Never edit C# scripts while Play mode is active (triggers domain reload).
- Avoid floats in generated filenames (some locales format floats with commas).

## 4. Verifying Room Lighting & Meshes in Play Mode (Not Edit Mode)

- **Why:** Edit-mode camera renders of authored rooms (`IntakeRoom`, `ShopRoom`, `AsylumHub`) are lit by the Editor's skybox ambient, which hides surfaces with wrong normals or inverted triangle winding (Unity front faces wind clockwise when viewed from outside). In Play mode, ambient is `~0.012` and only point lights + flashlight illuminate the room, so inverted surfaces render pitch black.
- **Procedure:** After any builder change that creates meshes or swaps materials:
  1. Enter Play mode and start a run.
  2. Create a temporary `UnityEngine.Camera` with `CopyFrom(UnityEngine.Camera.main)` and `UnityEngine.Rendering.Universal.UniversalAdditionalCameraData.renderPostProcessing = true`.
  3. Render into a `RenderTexture`, encode to PNG, and write to disk with `System.IO.File.WriteAllBytes` (use `UnityEngine.ScreenCapture` if UI overlays are needed).
