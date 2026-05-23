# DigiCreatures Agent for Unity

`DigiCreatures Agent` 是一个 Unity 数字生物智能体插件。它把 LLM、灵魂设定、文件记忆、NavMesh、语义物体、语义区域、字幕和摄像机逻辑组合成一个可复用系统，让虚拟生物可以在场景中自主选择目标、移动、停留、表达想法并触发简单互动。

当前版本：`0.1.5`。已在 Unity `6.4` 项目中验证，目标兼容 Unity `6.x`，并针对 Unity `6.0` Windows `.unitypackage` 导入做了兼容处理。

## 安装方式

### UPM Git Package

在 Unity Package Manager 中选择 `Add package from git URL...`，输入：

```text
https://github.com/pijiuya/digi-creatures-unity.git
```

旧的子目录 URL 也继续支持：

```text
https://github.com/pijiuya/digi-creatures-unity.git?path=/Packages/com.digicreatures.agent
```

如果仓库是私有仓库，需要先让客户机器具备 GitHub 访问权限。

### UnityPackage

在开发项目中点击：

```text
DigiCreatures > Export UnityPackage
```

或：

```text
数字生物 > 高级设置 > 导出 UnityPackage
```

导出文件默认位于 `Builds/DigiCreaturesAgent-<版本号>.unitypackage`，例如 `Builds/DigiCreaturesAgent-0.1.5.unitypackage`。导入客户项目后菜单会出现在 `数字生物` 下。

`0.1.3` 起，`.unitypackage` 会导入到 `Assets/DigiCreaturesAgent`，并包含独立依赖安装器；导入后会自动尝试安装 AI Navigation、glTFast、Input System、URP 和 UGUI。也可以手动点击：

```text
数字生物 > 高级设置 > 安装依赖
```

## 依赖

必需：

- `com.unity.ai.navigation`
- `com.unity.cloud.gltfast`
- `com.unity.inputsystem`
- `com.unity.render-pipelines.universal`
- `com.unity.ugui`
- TextMeshPro，通常随 UGUI 一起可用

推荐：

- Ollama：用于本地 OpenAI-compatible Chat Completions

Unity AI Assistant 不是硬依赖；可以作为后续动作生成建议工具使用。

## 快速开始

1. 安装插件。
2. 在 Package Manager 的 Samples 中导入 `DigiPlace Demo`。
3. 打开 demo 场景 `DigiPlace`。
4. 打开 `数字生物 > 模型管理`。
5. 启动 Ollama，下载或选择 `qwen2.5:3b`。
6. 点击 `测试模型响应`，确认能返回智能体 JSON。
7. 点击 `应用到当前场景智能体`。
8. 进入 Play Mode。

Demo 应该出现：数字生物在 NavMesh 上移动，底部字幕显示“说出口的话”和“内心想法”，监控窗口显示原始决策、目标、区域、互动和错误。

## 本地模型

本地后端默认使用 Ollama 的 OpenAI-compatible endpoint：

```text
http://localhost:11434/v1/chat/completions
```

推荐模型：

- `qwen2.5:3b`：中文较稳，适合离线 demo
- `llama3.2`
- `gemma3:4b`

`数字生物 > 模型管理` 中的下载按钮会显示进度条。下载完成后会进入“下载完成，刷新中...”状态，直到 `ollama list` 能确认该模型，随后按钮显示 `已下载` 并自动选中。

Windows 客户机需要保证 `ollama` 在 `PATH` 中。macOS/Linux 会优先尝试常见安装路径，例如 Homebrew 的 `/opt/homebrew/bin/ollama`、`/usr/local/bin/ollama` 和 Ollama.app 内置路径；找不到时再回退到 PATH。插件不会依赖 macOS 的 `/bin/zsh` 执行 `ollama list/pull/serve`。只有用户自定义启动命令才会通过 shell 执行。

## 极简本机烟测

如果想验证“空场景 + 本地模型 + NavMesh + 语义互动”是否可用，可以运行：

```text
数字生物 > 高级设置 > 测试 > 运行 60 秒极简本机测试
```

工具会创建独立场景 `Assets/DigiCreaturesSmokeTest/DigiCreatures_MinimalSmoke.unity`，包含一个平面 NavMesh、一个胶囊 agent、三个语义物体、一个语义区域和底部字幕。测试结束后报告写入：

```text
Library/DigiCreaturesTestRuns/minimal-smoke-*.md
```

报告会统计 agent 是否在 NavMesh 上、移动距离、LLM 成功/失败次数、决策次数和互动次数。这个工具不会修改 DigiPlace demo 场景。

## 线上模型

线上后端使用 OpenAI-compatible Chat Completions。API 地址可以填：

```text
https://api.openai.com/v1
```

或：

```text
https://api.openai.com/v1/chat/completions
```

插件会在请求时规范化到 chat completions URL。

API Key 不写入项目资产。优先级：

1. 环境变量，例如 `OPENAI_API_KEY`
2. Unity EditorPrefs 中的临时 Key，仅保存在本机

不要把真实 Key 提交到 Git。曾经序列化进项目资产的测试 Key 应立即轮换。

## 角色灵魂和记忆

每个生物都有一个数据目录，包含：

- `soul.md`：人格、欲望、禁忌、行动风格
- `summary.md`：长期记忆摘要
- `memory.jsonl`：事件日志
- `config.json`：可选模型配置

开发期默认目录是：

```text
Assets/DigiCreaturesData/<creatureId>
```

正式运行默认目录是：

```text
Application.persistentDataPath/DigiCreatures/<creatureId>
```

也可以在 `CreatureBrain` 中显式指定 `CreatureDataPath`。

`soul.md` 支持 front matter：

```markdown
---
displayName: 暗黑大统领
subtitleName: 暗黑大统领
---

# 暗黑大统领

你是……
```

字幕名字和 prompt 名称优先读取 `soul.md` 的 `subtitleName/displayName`，其次读取 `CreatureProfile`，最后使用 GameObject 名。把角色改成“暗黑大统领”后，字幕不会强制显示 demo 里的星海机器人名字。

如果客户只写了几句粗略设定，可以打开：

```text
数字生物 > 灵魂文件生成器
```

这个工具可以把简单设定扩展成完整 `soul.md`。它支持离线模板生成，也可以调用“模型管理”中当前配置的本地或线上 LLM；生成结果会先显示在预览区，确认后再保存，不会自动覆盖已有文件。

## 语义物体

打开：

```text
数字生物 > 语义物体面板
```

面板会扫描当前场景中带 Renderer 的物体。勾选对象后可以一键添加：

- `CreatureSemanticTarget`：让 LLM 知道这是可理解目标
- `CreatureInteractable`：让 LLM 知道这是可互动对象

可配置动作：

- `inspect`：默认互动。没有 UnityEvent 时也会有轻微跳动反馈。
- `move_object`：移动物体。默认使用 `moveOffset`，也可以指定 `moveTarget`。
- `jump_animation`：播放动画。没有 `AnimationClip` 时执行默认“跳一下”。
- `spawn_prefab`：生成 Prefab。没有指定 Prefab 时不会进入 LLM 可选 action 列表。

`CreatureInteractable` Inspector 中提供测试按钮：`测试 inspect`、`测试 move_object`、`测试 jump_animation`、`测试 spawn_prefab`。测试按钮只在 Editor 下显示，Play Mode 中执行。

## 语义区域和动态目标

固定 `CreatureLocationMarker` 只是 fallback。主要移动逻辑是：

1. LLM 选择 `targetId`、`regionId` 或互动对象
2. Runtime 在语义物体附近或语义区域内部随机采样点
3. 用 `NavMesh.SamplePosition` 和 `NavMesh.CalculatePath` 验证可达
4. `NavMeshAgent` 移动到这个动态点

这样 agent 不会一直去几个固定 marker，而是在语义物体和场景区域内自然漫游。

## NavMesh

Demo 场景包含已配置的 `NavMeshSurface`。客户项目中建议：

1. 给可行走几何体添加 Renderer 或 Collider
2. 场景中添加 `NavMeshSurface`
3. 设置 Agent Type、Collect Objects、Use Geometry
4. 点击 Bake
5. 确认 Scene 视图的 Navigation/NavMesh 可见

如果复杂模型无法直接烘焙，可以使用：

```text
数字生物 > 高级设置 > 导航 > 打开 NavMesh 代理生成器
```

它会从选中模型的朝上三角面生成 `DigiCreatures_NavProxyRoot`，不会删除原始模型。

## 摄像机和字幕

字幕使用包内 `NotoSansSC-Regular` 字体，Windows/macOS/Linux 都可显示中文。字幕分两行：

- 第一行：`dialogue`，角色说出口的话
- 第二行：`intent`，内心想法

摄像机逻辑：

- 固定机位优先
- 空格键可以切换第三人称
- 固定机位看不到 agent 时，70% 使用第三人称，30% 使用运动全景镜头

`CreatureCameraRig` 中可以调整第三人称 offset、look height、damping 和手动切换按键。

## GLB/GLTF

安装 `com.unity.cloud.gltfast` 后可以导入 GLB/GLTF。导入流程：

1. 把 GLB 放入项目
2. 拖入场景
3. 用 `语义物体面板` 标注为语义目标
4. 根据需要勾选可互动动作
5. 确认 NavMesh 可达，或添加语义区域/观察点

## 常见问题

中文显示为方块：

- 确认插件包内存在 `Runtime/Resources/DigiCreatures/NotoSansSC-Regular.otf`
- 确认字幕对象使用 TextMeshPro

Ollama 不在 PATH：

- macOS/Linux：终端执行 `ollama list` 应成功
- Windows：在系统环境变量中加入 Ollama 安装目录

NavMesh 不显示：

- Scene 视图开启 Navigation/NavMesh 显示
- 确认场景有 `NavMeshSurface`
- 确认几何体可被 Surface 收集
- 必要时使用 NavMesh 代理生成器

模型返回坏 JSON：

- Parser 会修复常见问题，例如 `explore -> move`、非法 `movement -> walk`、`inspect:CustomEvent -> inspect`
- 仍建议在模型管理里先运行 `测试模型响应`

互动不触发：

- 确认 agent 到达互动半径
- 确认 action 被启用
- `SpawnPrefab` 必须指定 Prefab 才会进入 prompt
- Play Mode 下用 Inspector 测试按钮确认动作本身可用

## Demo 验收清单

- NavMesh 可见且可行走
- 数字生物能移动至少 5 次
- 字幕中文不乱码
- `dialogue` 和 `intent` 不长期重复
- 修改 `soul.md` 后角色风格改变
- 空格可以切换第三人称
- 至少一次语义物体互动写入 memory
- 监控窗口无红错

## 发布安全

发布前必须检查：

- 不包含真实 API Key
- 不包含 `memory.jsonl`、测试截图、长测报告
- 不包含本机绝对路径
- 字体 license 随包提供

本包随附 Noto Sans SC 字体，遵循 SIL Open Font License。
