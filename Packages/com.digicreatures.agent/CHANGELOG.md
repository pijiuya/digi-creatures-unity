# Changelog

## Unreleased

- Added `数字生物 > 灵魂文件生成器`, an editor tool that expands a short character brief into a full `soul.md` using either an offline template or the currently configured LLM backend.
- Improved Linux custom command portability by using `/bin/sh` instead of assuming `/bin/zsh`.

## 0.1.1

- Added a minimal smoke test scene generator and 60-second local Play Mode smoke test under `数字生物 > 高级设置 > 测试`.
- Improved Ollama executable discovery on macOS/Linux so Unity-launched editors can find Homebrew or Ollama.app installs even when PATH is incomplete.
- Improved fallback movement so unavailable or slow local models still alternate between semantic target interaction and region roaming.
- Added smoke test reporting for LLM successes/failures and interaction counts.
- Exported `.unitypackage` filenames now follow the package version.

## 0.1.0

- Initial DigiCreatures Agent package.
- Added LLM-backed creature brain, memory files, semantic targets and regions, NavMesh motor, subtitles, model management, camera helpers, and DigiPlace demo tooling.
