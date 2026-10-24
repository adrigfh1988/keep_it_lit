---
name: unity-compile-check
description: >-
  How to compile-check KEEP IT LIT while the Unity Editor is open and holds Temp/UnityLockfile.
  Use this skill when verifying C# script compilation via the Unity CLI (`unity recompile`),
  inspecting `Logs/Editor.log`, or driving Unity's bundled Roslyn `csc.dll` directly.
---

# Compile-Checking While the Unity Editor is Open

Batch-mode compilation (`Unity.exe -batchmode -quit ...`) fails whenever the Unity Editor is open and holds `Temp/UnityLockfile`. Use one of the following methods instead:

## Method 1: Unity CLI (Preferred when Editor is running)

1. Check Editor connection:
   ```powershell
   unity status
   ```
2. Trigger recompile via the Unity CLI:
   ```powershell
   unity recompile
   ```
   *(Or `unity command recompile` followed by polling `unity command recompile_status`)*
3. Check `Logs/Editor.log` for `error CS` lines:
   ```powershell
   Select-String -Path "Logs/Editor.log" -Pattern "error CS"
   ```

## Method 2: Direct Roslyn (`csc.dll`) Check (Offline / Locked Editor Fallback)

When you need a direct compiler verdict in seconds without waiting on an Editor domain reload (Editor `6000.6.2f1`):

1. Parse `KeepItLit.Runtime.csproj` / `Assembly-CSharp.csproj` (or `KeepItLit.Editor.csproj` / `Assembly-CSharp-Editor.csproj` for Editor scripts) for `<DefineConstants>`, `<HintPath>` references, and the `<Compile Include>` list.
2. Union the `<Compile Include>` list with `Get-ChildItem -Path "Assets/Scripts" -Recurse -Filter "*.cs"` (and `Get-ChildItem -Path "Assets/Editor" -Recurse -Filter "*.cs"` for Editor scripts) because `.csproj` may be stale for brand-new files.
3. Add every `Library/ScriptAssemblies/*.dll` (except `Assembly-CSharp*.dll`) as `-r:` references — package dependencies (`Unity.InputSystem`, `Unity.AI.Navigation`, `Unity.Cinemachine`) are `<ProjectReference>`s in `.csproj`, so omitting `Library/ScriptAssemblies/*.dll` causes spurious `CS0246`/`CS0234` errors.
4. Write a `.rsp` response file with:
   - `-target:library`
   - `-nostdlib+`
   - `-langversion:9.0`
   - `-nowarn:0649,0618,0414,0169` (`CS0649` on `[SerializeField]` fields is expected noise)
5. Run Unity's bundled Roslyn compiler:
   ```powershell
   & "C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Data\NetCoreRuntime\dotnet.exe" "C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Data\DotNetSdk\sdk\8.0.318\Roslyn\bincore\csc.dll" "@build.rsp"
   ```

### Pitfalls to Avoid
- Drop empty `<HintPath>` entries (`-r:""` triggers `CS2021`).
- Always quote `-out:"path"` when paths contain spaces, and build the `.rsp` using a `List[string]` in PowerShell.
- Do not rely on `python` on this machine (it is the Microsoft Store stub).
