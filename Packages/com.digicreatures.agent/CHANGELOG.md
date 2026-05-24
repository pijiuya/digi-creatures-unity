# Changelog

## 0.1.8

- Added multi-agent model animation tooling for DigiRoom so each CreatureBrain can bind a distinct FBX/GLB model root, Animator, Avatar, and generated locomotion controller without overwriting other agents.
- Updated scene repair behavior to preserve multiple autonomous CreatureBrain instances by default, with a separate menu action for single-agent startup mode.
- Made Animator parameter writes tolerant of model-specific controllers and ensured imported single-clip locomotion animations loop without being frozen by a zero MotionSpeed parameter.
- Added a DigiRoom multi-agent unitypackage export that includes the agent package plus the scene, model assets, and generated controller closure.
- Removed an unused hard reference to Cinemachine from the DigiPlace demo `RespawnPlayer` sample script so imported `.unitypackage` demos compile in projects that do not have Cinemachine installed.
- Renamed the sample third-person camera target field away from Cinemachine terminology while preserving existing serialized demo references.
- Made `.unitypackage` dependency installation safer by compiling the lightweight installer before the main runtime/editor assemblies when required packages are missing, and removed URP from the auto-install list.
- Added a dedicated demo assembly definition so sample scripts wait for their UGUI/TextMeshPro/Input System dependencies instead of failing before the auto-installer can run.
- Removed stale Unity performance-test resource files from the demo package because they referenced old project-only dependencies and were not needed at runtime.

## 0.1.7

- Lowered the package baseline to Unity `2022.3` while keeping Unity 6 compatibility.
- Switched object lookup compatibility code to legacy `FindObject(s)OfType` APIs so the package does not depend on Unity 6 object-search overloads.
- Updated package dependencies to Unity 2022.3-compatible AI Navigation `1.1.5` and Input System `1.7.0`.

## 0.1.6

- Fixed Unity 6.0 compile errors caused by newer `FindObjectsInactive` object-search overloads that are available in Unity 6.4 but not accepted by Unity 6.0.
- Added a compatibility object finder used by runtime and editor tools.
- Declared required Unity built-in modules such as Animation, AI, Physics, JSON serialization, ScreenCapture, UI, and UnityWebRequest so minimal Unity 6 projects can compile the package.
- Added `数字生命` menu aliases for the main daily entry points while keeping the existing `数字生物` menu.

## 0.1.5

- Rebuilt the `.unitypackage` format for Windows Unity 6.0 compatibility by avoiding PAX tar headers and exporting sample asset paths with ASCII-only names.
- Relaxed the `.unitypackage` dependency installer so Unity chooses package versions compatible with the current editor instead of forcing Unity 6.4-era URP dependencies.
- Lowered UPM dependency baselines to Unity 6.0-compatible AI Navigation and Input System versions, and removed URP as a hard package dependency.

## 0.1.4

- Fixed an editor compile error in `DigiCreaturesPackageExporter` caused by `CompressionLevel` resolving ambiguously between `System.IO.Compression` and `UnityEngine` after package import.

## 0.1.3

- Added a root `package.json` so the bare Git URL can be installed directly from Unity Package Manager.
- Added a dependency installer assembly that can automatically add AI Navigation, glTFast, Input System, URP, and UGUI when importing the `.unitypackage` into a fresh project.
- Rebuilt the `.unitypackage` exporter as a stable manual packer. It now preserves original asset GUIDs and writes to `Assets/DigiCreaturesAgent`, avoiding missing prefab/model/script references caused by Unity temporary-copy GUID regeneration.

## 0.1.2

- Added `数字生物 > 灵魂文件生成器`, an editor tool that expands a short character brief into a full `soul.md` using either an offline template or the currently configured LLM backend.
- Improved Linux custom command portability by using `/bin/sh` instead of assuming `/bin/zsh`.
- Fixed `.unitypackage` export GUID remapping so imported demo scenes keep prefab, model, GLB, and MonoScript references intact on fresh Windows/macOS/Linux projects.

## 0.1.1

- Added a minimal smoke test scene generator and 60-second local Play Mode smoke test under `数字生物 > 高级设置 > 测试`.
- Improved Ollama executable discovery on macOS/Linux so Unity-launched editors can find Homebrew or Ollama.app installs even when PATH is incomplete.
- Improved fallback movement so unavailable or slow local models still alternate between semantic target interaction and region roaming.
- Added smoke test reporting for LLM successes/failures and interaction counts.
- Exported `.unitypackage` filenames now follow the package version.

## 0.1.0

- Initial DigiCreatures Agent package.
- Added LLM-backed creature brain, memory files, semantic targets and regions, NavMesh motor, subtitles, model management, camera helpers, and DigiPlace demo tooling.
