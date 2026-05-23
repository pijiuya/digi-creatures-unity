# Changelog

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
