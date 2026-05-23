# DigiCreatures 本地 Codex 运行手册

面向客户现场使用。目标是让客户把 Unity 项目交给本地 Codex 后，能稳定完成三件事：

- 唤醒服务：确认 Unity 项目、Ollama/线上 LLM、模型配置和场景智能体都处于可用状态。
- 稳定运行：跑通 DigiPlace Demo 或极简烟测，拿到可读的验证结果。
- 快速修复：遇到模型、语义物体、NavMesh、字幕、互动失败时，让 Codex 按固定顺序排查，不乱改项目。

本文默认插件为 `DigiCreatures Agent 0.1.8`，Unity 目标版本为 `2022.3 LTS` 到 `Unity 6.x`。推荐本地模型是 `Ollama + qwen2.5:3b`。

---

## 1. 给 Codex 的第一条消息

客户打开 Codex 后，先进入 Unity 项目根目录，再把下面这段发给 Codex。

```text
你现在是 DigiCreatures Agent 的本地运维助手。

项目目标：
- 先只读检查当前 Unity 项目，不要直接改源码或场景。
- 确认 DigiCreatures Agent 插件、Unity 菜单、Ollama/模型、场景智能体、NavMesh、语义物体和烟测入口是否正常。
- 优先使用本地 Ollama + qwen2.5:3b；如果客户明确要求，再检查线上 OpenAI-compatible endpoint。
- API Key 只能读取环境变量名 OPENAI_API_KEY 是否存在，不要打印真实 Key。

工作方式：
1. 先运行只读检查：pwd、git status --short、README、Packages/com.digicreatures.agent/package.json、关键 MenuItem。
2. 把检查结果分成：已正常、需要客户操作、建议 Codex 修复。
3. 需要启动服务时，先说明会启动什么服务，再启动 Ollama 或指导客户打开 Unity。
4. 不要执行 git reset、删除文件、清理 Library、覆盖 soul.md、修改场景，除非我明确同意。
5. 每次修复后要给出验证步骤和结果。
```

如果客户希望 Codex 直接开始唤醒服务，可以追加：

```text
请按本地唤醒流程执行：检查项目 -> 唤醒 Ollama -> 确认 qwen2.5:3b -> 指导我在 Unity 运行模型测试和 60 秒极简烟测。
```

---

## 2. Codex 操作边界

客户现场最怕“越修越乱”。请让 Codex 遵守下面的边界。

允许 Codex 直接做：

- 读取 README、package.json、C# 脚本、配置文件。
- 运行 `rg`、`find`、`git status --short`、`ollama list`、`curl localhost` 等检查命令。
- 启动本地 `ollama serve`。
- 生成检查报告、Markdown 说明、排查清单。
- 指导客户点击 Unity 菜单。

需要客户确认后再做：

- 修改 `soul.md`、`summary.md`、`config.json`。
- 修改 Unity 场景、Prefab、材质、NavMesh 资产。
- 运行会写场景的 Unity 菜单，例如“场景验证与修复”“扫描并生成目标”“从选中对象生成代理并烘焙”。
- 导出 UnityPackage。

禁止默认做：

- 打印真实 API Key。
- 删除 `Library/`、`Assets/`、`ProjectSettings/`。
- `git reset --hard`、`git checkout --`、批量清理未跟踪文件。
- 未确认就覆盖客户的灵魂文件或记忆文件。

---

## 3. 一键只读体检

让 Codex 先跑下面这些命令。它们只读取信息，不会修改项目。

```bash
pwd
git status --short

sed -n '1,220p' README.md
sed -n '1,180p' Packages/com.digicreatures.agent/package.json

rg -n "MenuItem\\(|模型管理|灵魂文件生成器|语义物体面板|决策监控|运行 60 秒|场景验证|NavMesh 代理" \
  Packages/com.digicreatures.agent -g '*.cs'

find Assets -maxdepth 4 \( -name 'CreatureLlmSettings.asset' -o -name 'soul.md' -o -name 'summary.md' -o -name 'config.json' -o -name '*.unity' \) -print
```

Codex 应该给客户输出四类结果：

- 插件版本：例如 `0.1.8`。
- 可用菜单：模型管理、灵魂文件生成器、语义物体面板、决策监控、安装依赖、场景验证与修复、60 秒极简本机测试。
- 当前角色数据：`soul.md`、`summary.md`、`memory.jsonl`、`config.json` 的位置。
- 风险提示：未跟踪文件、缺包、缺模型、缺场景、缺 NavMesh。

---

## 4. 唤醒本地模型服务

### 4.1 检查 Ollama

```bash
command -v ollama
ollama list
```

预期能看到 `qwen2.5:3b`。如果没有：

```bash
ollama pull qwen2.5:3b
```

### 4.2 启动 Ollama

先检查是否已经运行：

```bash
pgrep -fl "ollama" || true
```

如果没有运行：

```bash
nohup ollama serve > /tmp/digicreatures-ollama.log 2>&1 &
sleep 2
ollama list
```

### 4.3 测试 OpenAI-compatible 接口

```bash
curl -s http://localhost:11434/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "qwen2.5:3b",
    "messages": [
      {
        "role": "user",
        "content": "只返回一个 JSON：{\"mode\":\"move\",\"movement\":\"walk\",\"intent\":\"测试\"}"
      }
    ],
    "temperature": 0.2
  }' | head -c 800
```

看到 JSON 响应即可。这里不是测试角色智能，只是确认本地服务醒了。

---

## 5. 唤醒 Unity 项目

Codex 不一定能可靠操作 Unity UI。最稳妥是让 Codex先说明路径，客户打开 Unity 后按菜单操作。

### 5.1 打开项目

客户用 Unity Hub 打开项目根目录，或在 macOS 上让 Codex运行类似命令：

```bash
open -na "/Applications/Unity/Hub/Editor/6000.4.7f1/Unity.app" --args -projectPath "$PWD"
```

如果客户 Unity 版本不同，让客户用 Unity Hub 打开，不要硬写路径。

### 5.2 在 Unity 中检查菜单

菜单栏应出现：

```text
数字生物 > 模型管理
数字生物 > 灵魂文件生成器
数字生物 > 语义物体面板
数字生物 > 决策监控
数字生物 > 确保数字生物在场景中
数字生物 > 高级设置 > 安装依赖
数字生物 > 高级设置 > 场景验证与修复
数字生物 > 高级设置 > 测试 > 运行 60 秒极简本机测试
数字生物 > 高级设置 > 导航 > 打开 NavMesh 代理生成器
数字生物 > 高级设置 > 场景语义 > 验证可达性
```

如果菜单不存在，优先检查：

- 包是否导入：`Packages/com.digicreatures.agent/package.json`。
- Console 是否有编译错误。
- 依赖是否安装：AI Navigation、glTFast、Input System、UGUI/TextMeshPro。
- `.unitypackage` 客户项目中是否导入到了 `Assets/DigiCreaturesAgent`。

---

## 6. 在 Unity 中唤醒智能体

### 6.1 模型管理

打开：

```text
数字生物 > 模型管理
```

推荐顺序：

1. 选择“本地 Ollama”。
2. 点击“启动 Ollama”。
3. 点击“刷新本地模型”。
4. 选择或下载 `qwen2.5:3b`。
5. 点击“测试模型响应”。
6. 返回智能体 JSON 后，点击“应用到当前场景智能体”。

线上模型只在客户明确要求时使用。API Key 规则：

- 运行时优先读 `OPENAI_API_KEY`。
- 模型管理窗口的“临时 API Key”只保存在本机 EditorPrefs。
- 不要把真实 Key 写进项目资产或提交到 Git。

### 6.2 确保智能体在场景中

打开：

```text
数字生物 > 确保数字生物在场景中
```

这个操作会补齐场景里的智能体组件，可能修改场景。客户确认后再点。

### 6.3 决策监控

打开：

```text
数字生物 > 决策监控
```

播放时重点看：

- 当前智能体。
- 当前状态。
- 当前动作。
- 最近目标。
- 最近区域。
- 最近兴趣理由。
- 最近错误。

---

## 7. 最稳验证：60 秒极简本机烟测

客户确认可以创建测试场景后，在 Unity 中运行：

```text
数字生物 > 高级设置 > 测试 > 运行 60 秒极简本机测试
```

预期行为：

- 创建独立测试场景。
- 包含平面 NavMesh、一个胶囊智能体、三个语义物体、一个语义区域和字幕。
- 自动进入播放测试。
- 结束后写出报告。

报告位置：

```text
Library/DigiCreaturesTestRuns/minimal-smoke-*.md
```

让 Codex 读取最新报告：

```bash
ls -t Library/DigiCreaturesTestRuns/minimal-smoke-*.md 2>/dev/null | head -n 1
```

然后：

```bash
report=$(ls -t Library/DigiCreaturesTestRuns/minimal-smoke-*.md 2>/dev/null | head -n 1)
test -n "$report" && sed -n '1,220p' "$report"
```

Codex 判断标准：

- `Agent on NavMesh: true`。
- 有移动距离。
- 有 LLM success 或合理 fallback。
- 没有持续 JSON 解析失败。
- 没有持续 NavMesh 路径不完整。

---

## 8. 常见故障与 Codex 处理顺序

### 8.1 模型没醒

现象：

- 模型管理显示无法连接。
- 测试响应超时。
- Console 提示 LLM unavailable。

Codex 排查：

```bash
command -v ollama
pgrep -fl "ollama" || true
ollama list
curl -s http://localhost:11434/v1/models | head -c 500
```

处理：

- 没安装 Ollama：客户安装。
- 没模型：`ollama pull qwen2.5:3b`。
- 服务没启动：`nohup ollama serve > /tmp/digicreatures-ollama.log 2>&1 &`。
- 模型太慢：建议先换 `qwen2.5:1.5b` 做连通性验证，再回到 `qwen2.5:3b`。

### 8.2 返回不是合法 JSON

现象：

- 决策监控出现解析失败。
- memory 里只有 fallback。

Codex 排查：

- 检查 `soul.md` 是否要求“不要输出 JSON 之外的任何文本”。
- 降低模型管理里的 temperature。
- 换更稳定模型。
- 用“测试模型响应”先验证模型协议。

### 8.3 智能体不动

现象：

- 角色在场景中站着。
- 决策有目标，但移动失败。

Codex 排查：

```bash
rg -n "NavMesh 路径不完整|move_failed|Could not reach|路径无效|isOnNavMesh" Assets Library Logs 2>/dev/null
```

Unity 中检查：

- 场景是否有 `NavMeshSurface`。
- Agent 是否在 NavMesh 上。
- 目标附近是否能采样到 NavMesh。
- 语义物体的 `approachRadius` 是否太小。

处理顺序：

1. 打开 `数字生物 > 高级设置 > 场景语义 > 验证可达性`。
2. 如果复杂模型烘焙失败，用 `数字生物 > 高级设置 > 导航 > 打开 NavMesh 代理生成器`。
3. 必要时重新 Bake。

### 8.4 语义物体无效

现象：

- 模型总去固定 marker。
- 不接近客户新放的物体。
- 互动不触发。

Unity 中处理：

```text
数字生物 > 语义物体面板
```

检查：

- 是否勾选并标记为语义物体。
- 显示名称是否清楚。
- 描述是否说明“为什么值得注意”。
- 语义标签是否有用，例如 tower, console, light, fragile。
- `可互动`、`可移动`、`可触发动画`、`可生成 Prefab` 是否按需勾选。
- `interactionRadius` 是否太小。

### 8.5 角色名字或性格不对

Codex 检查：

```bash
find Assets -name soul.md -print
sed -n '1,120p' Assets/Creatures/Digimon01/soul.md 2>/dev/null || true
```

优先级：

1. `soul.md` front matter 里的 `displayName` 和 `subtitleName`。
2. `CreatureProfile`。
3. GameObject 名称。

客户要改角色时，推荐用：

```text
数字生物 > 灵魂文件生成器
```

让 Codex 先生成草稿，不要直接覆盖原文件。

---

## 9. 客户可直接复制的 Codex 任务模板

### 9.1 只读健康检查

```text
请对这个 DigiCreatures Unity 项目做只读健康检查。

要求：
- 不修改任何文件。
- 检查 README、package.json、菜单入口、模型配置、角色数据目录、场景文件、烟测入口。
- 检查本机是否有 ollama 和 qwen2.5:3b。
- 不打印任何真实 API Key。
- 最后给我一个三栏结果：正常、需要我手动处理、建议你修复。
```

### 9.2 唤醒本地模型

```text
请唤醒本地 Ollama 服务并验证 qwen2.5:3b。

要求：
- 先检查 ollama 是否已运行。
- 如果没运行，可以启动 ollama serve。
- 用 /v1/chat/completions 发一个最小测试请求。
- 不修改 Unity 项目。
- 输出清楚：服务是否运行、模型是否存在、接口是否可响应。
```

### 9.3 指导客户跑 Unity 验证

```text
请指导我在 Unity 里跑 DigiCreatures 验证。

要求：
- 告诉我依次打开哪些菜单。
- 先模型管理测试响应，再应用到当前场景智能体。
- 然后打开决策监控，进入 Play Mode。
- 如果我同意，再运行 60 秒极简本机烟测。
- 每一步告诉我应该看到什么，看到异常时下一步查什么。
```

### 9.4 分析烟测报告

```text
请读取最新的 Library/DigiCreaturesTestRuns/minimal-smoke-*.md，并判断 DigiCreatures 是否跑通。

请重点看：
- Agent on NavMesh 是否为 true。
- 移动距离是否大于 0。
- LLM successes、decision count、interaction count。
- 是否有 JSON 解析失败、请求超时、NavMesh 路径不完整。

最后给出：通过/未通过、原因、下一步修复建议。
```

### 9.5 修复 NavMesh 问题

```text
智能体现在不动或移动失败。请按 NavMesh 方向排查。

要求：
- 先只读检查日志和报告。
- 不要删除场景对象。
- 不要清理 Library。
- 给我列出 Unity 中应该点击的菜单和 Inspector 字段。
- 如果需要运行“场景验证与修复”或“NavMesh 代理生成器”，先说明会修改什么，再等我确认。
```

### 9.6 订制角色但不覆盖原文件

```text
请帮我为 DigiCreatures 设计一个新角色 soul.md 草稿。

要求：
- 先读取当前 soul.md 的结构。
- 生成一个新的草稿内容，不要覆盖原文件。
- 必须包含 front matter：displayName 和 subtitleName。
- 必须包含：核心身份、行动偏好、语义物体互动倾向、记忆目标、行为边界。
- 明确要求模型不要编造 targetId、regionId、interactionId、destinationId，不要输出坐标。
```

---

## 10. Codex 最终汇报格式

每次客户让 Codex 排查或唤醒服务，最后都按这个格式汇报。

```text
结论：
- 通过/未通过/部分通过。

我检查了：
- 项目版本：
- Unity 菜单：
- Ollama 服务：
- 模型：
- 场景智能体：
- NavMesh：
- 语义物体：
- 决策监控/烟测报告：

我没有做：
- 没有打印 API Key。
- 没有删除文件。
- 没有重置 Git。
- 没有覆盖 soul.md/场景。

下一步：
1. 客户需要做什么。
2. Codex 可以继续做什么。
3. 哪一步需要客户确认。
```

---

## 11. 速查表

常用 Unity 菜单：

```text
数字生物 > 模型管理
数字生物 > 灵魂文件生成器
数字生物 > 语义物体面板
数字生物 > 决策监控
数字生物 > 确保数字生物在场景中
数字生物 > 高级设置 > 安装依赖
数字生物 > 高级设置 > 场景验证与修复
数字生物 > 高级设置 > 测试 > 运行 60 秒极简本机测试
数字生物 > 高级设置 > 导航 > 打开 NavMesh 代理生成器
数字生物 > 高级设置 > 场景语义 > 验证可达性
```

关键文件：

```text
Packages/com.digicreatures.agent/package.json
Assets/DigiCreatures/Settings/CreatureLlmSettings.asset
Assets/DigiCreaturesData/<角色目录>/soul.md
Assets/DigiCreaturesData/<角色目录>/summary.md
Assets/DigiCreaturesData/<角色目录>/memory.jsonl
Assets/DigiCreaturesData/<角色目录>/config.json
Library/DigiCreaturesTestRuns/minimal-smoke-*.md
```

推荐模型：

```text
本地端点：http://localhost:11434/v1/chat/completions
本地模型：qwen2.5:3b
线上 Key 环境变量：OPENAI_API_KEY
```

一句话原则：

```text
先唤醒模型，再唤醒场景；先验证 NavMesh，再调角色性格；先读报告，再做修复。
```
