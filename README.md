# DigiCreatures Unity / 数字生物智能体 Unity 插件

`DigiCreatures Agent` is a Unity package for building LLM-driven virtual creatures that can read a soul profile, remember events, understand semantic objects, navigate with NavMesh, speak through subtitles, and trigger simple interactions in a 3D scene.

`DigiCreatures Agent` 是一个用于 Unity 的数字生物智能体插件。它把 LLM、灵魂设定、文件记忆、语义物体、NavMesh、自主移动、字幕表达、摄像机和互动动作组合成一套可复用系统，让虚拟生物能在场景中自己选择目标、移动、观察、表达想法并与物体发生互动。

Current version / 当前版本：`0.1.7`

Unity target / Unity 目标版本：Unity `2022.3 LTS` 到 Unity `6.x`。已在 Unity `6.4 (6000.4.7f1)` 上验证，并针对 Unity `6.0` Windows 导入和编译做兼容处理。

Repository / 仓库：

```text
https://github.com/pijiuya/digi-creatures-unity
```

UPM Git URL:

```text
https://github.com/pijiuya/digi-creatures-unity.git
```

Subfolder Git URL, also supported:

```text
https://github.com/pijiuya/digi-creatures-unity.git?path=/Packages/com.digicreatures.agent
```

The root URL is supported from `0.1.3` onward. If a customer already uses the older subfolder URL, it can continue to work.

从 `0.1.3` 开始，裸仓库 URL 可以直接作为 UPM 包安装。旧的子目录 URL 也继续支持。

Release package / UnityPackage 下载：

```text
https://github.com/pijiuya/digi-creatures-unity/releases/tag/v0.1.7
```

## What It Does / 项目能做什么

English:

- Lets a Unity creature periodically ask an OpenAI-compatible LLM what to do next.
- Gives each creature a configurable soul file, memory files, and optional profile asset.
- Lets the LLM choose semantic objects, semantic regions, backup markers, movement style, dialogue, inner thoughts, and interactions.
- Converts object or region choices into fresh reachable NavMesh points at runtime, so the agent does not only walk to fixed markers.
- Shows dialogue and inner thoughts in a bottom subtitle HUD using a packaged Chinese-capable font.
- Supports local Ollama models and remote OpenAI-compatible endpoints through one settings asset.
- Includes model management, Ollama download progress, semantic object panel, decision monitor, camera helper, smoke test scene, and a complete DigiPlace demo.

中文：

- 让 Unity 场景里的生物周期性调用 OpenAI-compatible LLM，决定下一步行为。
- 每个生物拥有可配置的 `soul.md`、`summary.md`、`memory.jsonl` 和可选 `CreatureProfile`。
- LLM 可以选择语义物体、语义区域、备用目标点、移动方式、对白、内心想法和互动动作。
- Runtime 会把语义物体或区域动态采样成 NavMesh 可达点，避免生物只去固定 marker。
- 使用底部字幕 HUD 同时显示“说出口的话”和“内心想法”，并随包提供可显示中文的字体。
- 支持本地 Ollama 和线上 OpenAI-compatible endpoint，通过统一配置切换。
- 包含模型管理、Ollama 下载进度、语义物体面板、决策监控、摄像机辅助、极简烟测场景和完整 DigiPlace demo。

## Package Layout / 包结构

```text
Packages/com.digicreatures.agent/
  package.json
  Runtime/
    CreatureBrain.cs
    CreatureMotor.cs
    CreatureMemoryStore.cs
    CreatureProfile.cs
    CreatureSemanticTarget.cs
    CreatureSemanticRegion.cs
    CreatureInteractable.cs
    CreatureSubtitleHud.cs
    CreatureCameraRig.cs
    LlmClient.cs
  Editor/
    CreatureAgentConsoleWindow.cs
    CreatureSemanticObjectPanel.cs
    CreatureAgentMonitorWindow.cs
    DigiCreaturesSmokeTestSceneUtility.cs
    DigiCreaturesPackageExporter.cs
  Samples~/DigiPlaceDemo/
  Documentation~/README.md
  CHANGELOG.md
  LICENSE.md
```

## Main Systems / 核心系统

### Creature Brain / 生物大脑

`CreatureBrain` is the orchestration loop. It reads creature memory, gathers scene perception, builds an LLM prompt, parses the returned JSON, resolves a movement goal, starts the motor, displays subtitles, writes memory events, and triggers interactions.

`CreatureBrain` 是主循环。它读取记忆、收集场景感知、构造 LLM prompt、解析 JSON 决策、解析移动目标、驱动 `CreatureMotor`、显示字幕、写入记忆，并在到达后触发互动。

Decision schema / 决策协议：

```json
{
  "mode": "move",
  "targetId": "star_obelisk",
  "regionId": "",
  "targetName": "星图方碑",
  "targetInterest": "这块方碑像星图校准器",
  "navigationKind": "Walkable",
  "approachPointId": "",
  "destinationId": "",
  "movement": "walk",
  "dwellSeconds": 3,
  "dialogue": "我绕到这个目标旁边，看看它有没有星图回声。",
  "interactionId": "star_obelisk",
  "actionId": "jump_animation",
  "activity": "interact",
  "intent": "我想确认它是否保存着失落星图的线索。",
  "memoryNote": "接近并检查了星图方碑。"
}
```

### Memory And Soul / 记忆与灵魂

Each creature can use:

- `soul.md`: personality, desires, taboos, behavior style
- `summary.md`: long-term memory summary
- `memory.jsonl`: timestamped event log
- `config.json`: optional model/backend config

每个生物可以使用：

- `soul.md`：人格、欲望、禁忌、行动风格
- `summary.md`：长期记忆摘要
- `memory.jsonl`：事件日志
- `config.json`：可选模型/后端配置

`soul.md` supports front matter. This means customers can rename and restyle the creature without changing code.

`soul.md` 支持 front matter，因此客户可以不改代码就替换角色身份和字幕名。

```markdown
---
displayName: 暗黑大统领
subtitleName: 暗黑大统领
---

# 暗黑大统领

你来自……
```

The editor also includes `数字生物 > 灵魂文件生成器`. Customers can write a short character brief, expand it into a complete `soul.md` with an offline template or the currently configured LLM, preview it, and save it into `Assets/DigiCreaturesData/<creature>`.

编辑器提供 `数字生物 > 灵魂文件生成器`。客户只需要写几句简单角色设定，就可以用离线模板或当前配置的 LLM 扩展成完整 `soul.md`，预览确认后保存到 `Assets/DigiCreaturesData/<角色目录>`。

### Semantic Objects And Regions / 语义物体与语义区域

The LLM never receives arbitrary coordinates. It sees curated semantic IDs:

- `CreatureSemanticTarget`: meaningful object, such as tower, console, orb, gate
- `CreatureSemanticRegion`: meaningful area, such as plaza, observation zone, edge zone
- `CreatureLocationMarker`: backup marker or viewpoint
- `CreatureInteractable`: executable action list

LLM 不直接输出任意坐标，而是选择语义 ID：

- `CreatureSemanticTarget`：有意义的场景物体，例如塔、控制台、球体、门
- `CreatureSemanticRegion`：有意义的区域，例如广场、观测区、边界区
- `CreatureLocationMarker`：备用目标点或观察点
- `CreatureInteractable`：可执行互动动作列表

Runtime then samples a reachable NavMesh point near the object or inside the region. This makes movement less stiff than fixed marker navigation.

Runtime 会在物体附近或区域内部随机采样 NavMesh 可达点，让移动不像固定 marker 那样僵硬。

### Interactions / 互动动作

Supported first-version actions:

- `inspect`: default feedback; if no UnityEvent is assigned, the object does a small jump
- `move_object`: moves the target using `moveOffset` or `moveTarget`
- `jump_animation`: plays an `AnimationClip`, or defaults to a simple jump
- `spawn_prefab`: spawns a configured prefab; hidden from the LLM if no prefab is assigned
- `custom_event`: invokes a UnityEvent and falls back to visible feedback if empty

第一版支持的互动动作：

- `inspect`：默认检查反馈；没有 UnityEvent 时物体会轻微跳动
- `move_object`：用 `moveOffset` 或 `moveTarget` 移动物体
- `jump_animation`：播放 `AnimationClip`，为空时执行默认跳一下
- `spawn_prefab`：生成指定 prefab；未指定 prefab 时不会进入 LLM 可选动作列表
- `custom_event`：触发 UnityEvent；没有绑定时仍会有可见反馈

### Local And Remote LLM / 本地与线上模型

Local default / 本地默认：

```text
Ollama
http://localhost:11434/v1/chat/completions
qwen2.5:3b
```

Remote backend / 线上后端：

```text
OpenAI-compatible Chat Completions
https://api.openai.com/v1/chat/completions
```

API keys are not serialized into project assets. Runtime reads environment variables such as `OPENAI_API_KEY`, and the editor can store a temporary local key in EditorPrefs.

API Key 不会序列化进项目资产。Runtime 优先读取 `OPENAI_API_KEY` 等环境变量；编辑器临时 Key 只保存在本机 EditorPrefs。

### Camera And Subtitles / 摄像机与字幕

- Fixed camera is preferred when it can see the agent.
- Space can toggle third-person view.
- If fixed view cannot see the agent, runtime can switch to third-person or a movement framing camera.
- Subtitles show two channels: spoken `dialogue` and private `intent`.
- A bundled Noto Sans SC font is used for Chinese subtitle readability on macOS, Windows, and Linux.

- 固定机位可见时优先使用固定机位。
- 空格键可以切换第三人称。
- 固定机位看不到 agent 时，可切换第三人称或运动全景镜头。
- 字幕分两行显示：说出口的 `dialogue` 和内心想法 `intent`。
- 随包提供 Noto Sans SC 字体，方便 macOS/Windows/Linux 显示中文。

## Installation / 安装

### Option 1: UPM Git Package

Unity Package Manager:

```text
Add package from git URL...
https://github.com/pijiuya/digi-creatures-unity.git
```

Older subfolder URL / 旧子目录 URL：

```text
https://github.com/pijiuya/digi-creatures-unity.git?path=/Packages/com.digicreatures.agent
```

For private repositories, the customer machine must be authenticated with GitHub before Unity Package Manager can clone it.

如果仓库是私有仓库，客户电脑必须先完成 GitHub/Git 认证，否则 Unity Package Manager 会显示无法安装。

### Option 2: UnityPackage

Download from release:

```text
https://github.com/pijiuya/digi-creatures-unity/releases/tag/v0.1.7
```

Or export from this development project:

```text
DigiCreatures > Export UnityPackage
数字生物 > 高级设置 > 导出 UnityPackage
```

The exporter creates:

```text
Builds/DigiCreaturesAgent-0.1.7.unitypackage
```

The UnityPackage imports to `Assets/DigiCreaturesAgent`. It includes an independent dependency installer that attempts to add AI Navigation, glTFast, Input System, URP, and UGUI automatically.

`.unitypackage` 会导入到 `Assets/DigiCreaturesAgent`，并包含一个独立依赖安装器，会自动尝试安装 AI Navigation、glTFast、Input System、URP 和 UGUI。

## Required Dependencies / 必需依赖

- `com.unity.ai.navigation`
- `com.unity.cloud.gltfast`
- `com.unity.inputsystem`
- `com.unity.render-pipelines.universal`
- `com.unity.ugui`
- TextMeshPro

Recommended / 推荐：

- Ollama for local model testing

Unity AI Assistant is optional. It is not a hard dependency.

Unity AI Assistant 是可选增强，不是硬依赖。

## Quick Start / 快速开始

English:

1. Install the package through UPM or `.unitypackage`.
2. Import the `DigiPlace Demo` sample from Package Manager.
3. Open the DigiPlace scene.
4. Open `数字生物 > 模型管理`.
5. Start Ollama or configure a remote endpoint.
6. Download/select `qwen2.5:3b`.
7. Click `测试模型响应`.
8. Click `应用到当前场景智能体`.
9. Enter Play Mode.

中文：

1. 通过 UPM 或 `.unitypackage` 安装插件。
2. 在 Package Manager 中导入 `DigiPlace Demo` sample。
3. 打开 DigiPlace 场景。
4. 打开 `数字生物 > 模型管理`。
5. 启动 Ollama 或配置线上 endpoint。
6. 下载/选择 `qwen2.5:3b`。
7. 点击 `测试模型响应`。
8. 点击 `应用到当前场景智能体`。
9. 进入 Play Mode。

Expected result / 预期效果：

- The creature moves on NavMesh.
- Bottom subtitles show spoken words and inner thoughts.
- The decision monitor records targets, regions, intent, dialogue, errors, and interactions.
- Semantic objects can visibly react through jump/move/spawn actions.

- 生物能在 NavMesh 上移动。
- 底部字幕显示对白和内心想法。
- 决策监控记录目标、区域、意图、对白、错误和互动。
- 语义物体能通过跳动、移动、生成 prefab 等动作产生可见反馈。

## Minimal Smoke Test / 极简本机烟测

Run:

```text
数字生物 > 高级设置 > 测试 > 运行 60 秒极简本机测试
```

This creates an isolated simple scene:

```text
Assets/DigiCreaturesSmokeTest/DigiCreatures_MinimalSmoke.unity
```

It includes:

- a walkable plane with NavMesh
- one capsule creature
- three semantic interactable objects
- one semantic roaming region
- a fixed camera and subtitle HUD

The report is written to:

```text
Library/DigiCreaturesTestRuns/minimal-smoke-*.md
```

Latest local validation / 最新本机验证：

```text
Unity Play Mode: 60s
Model: qwen2.5:3b
Agent on NavMesh: true
Moved from start: 11.38m
LLM successes: 3
LLM failures: 0
Interactions: 3
Example interaction: 星图方碑 executed jump_animation
```

## Editor Menus / 编辑器菜单

Daily-use menus / 日常菜单：

```text
数字生物 > 模型管理
数字生物 > 灵魂文件生成器
数字生物 > 语义物体面板
数字生物 > 决策监控
数字生物 > 确保数字生物在场景中
```

Advanced menus / 高级菜单：

```text
数字生物 > 高级设置 > 测试
数字生物 > 高级设置 > 导航
数字生物 > 高级设置 > 场景语义
数字生物 > 高级设置 > 导出 UnityPackage
```

## Demo Scene / Demo 场景

The package includes `DigiPlace Demo` as a sample. It demonstrates:

- one LLM-controlled creature
- semantic targets and regions
- NavMesh movement
- subtitles
- fixed/third-person camera behavior
- interactable scene objects
- local/remote model configuration

插件包含 `DigiPlace Demo` sample，用于展示：

- 单个 LLM 数字生物
- 语义目标和语义区域
- NavMesh 移动
- 字幕
- 固定/第三人称摄像机
- 可互动物体
- 本地/线上模型配置

## GLB Workflow / GLB 工作流

With `com.unity.cloud.gltfast` installed:

1. Import a GLB/GLTF model.
2. Drag it into the scene.
3. Open `数字生物 > 语义物体面板`.
4. Mark selected objects as semantic targets.
5. Add interaction options such as move, jump animation, or spawn prefab.
6. Bake or validate NavMesh.

安装 `com.unity.cloud.gltfast` 后：

1. 导入 GLB/GLTF 模型。
2. 拖入场景。
3. 打开 `数字生物 > 语义物体面板`。
4. 把选中的对象标注为语义目标。
5. 添加可移动、跳一下动画、生成 prefab 等互动属性。
6. 烘焙或验证 NavMesh。

## Safety / 安全说明

Do not commit:

- real API keys
- `memory.jsonl` or `test-memory.jsonl`
- long-test reports and screenshots
- local absolute paths
- customer-private assets without permission

不要提交：

- 真实 API Key
- `memory.jsonl` 或 `test-memory.jsonl`
- 长测报告和截图
- 本机绝对路径
- 未授权的客户私有资产

The repository `.gitignore` already excludes Unity cache folders, local smoke test outputs, memory logs, and `.unitypackage` build artifacts.

仓库 `.gitignore` 已排除 Unity 缓存目录、本机烟测输出、记忆日志和 `.unitypackage` 构建产物。

## Release / 发布

Latest release:

```text
v0.1.7
```

UnityPackage:

```text
DigiCreaturesAgent-0.1.7.unitypackage
```

SHA256:

```text
5f303c726077ed304e89e9a316b60d6c274671978577f36ecb311393d8b1f372
```

## Roadmap / 后续计划

- Multi-creature coordination
- Vector memory and retrieval
- Richer animation/action registry
- Better prompt compression for small local models
- In-editor action suggestion workflow with optional Unity AI Assistant
- Customer-ready setup wizard for new scenes

- 多生物协同
- 向量记忆与检索
- 更完整的动画/动作注册表
- 面向小本地模型的 prompt 压缩
- 可选 Unity AI Assistant 动作建议入口
- 面向客户新场景的一键安装向导

## License / 许可证

Plugin code is provided under the package license in `Packages/com.digicreatures.agent/LICENSE.md`.

插件代码许可证见 `Packages/com.digicreatures.agent/LICENSE.md`。

The bundled Noto Sans SC font follows the SIL Open Font License, included in the package.

随包 Noto Sans SC 字体遵循 SIL Open Font License，许可证已包含在包内。
