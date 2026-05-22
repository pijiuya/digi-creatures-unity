using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DigiCreatures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DigiCreaturesEditor
{
    [InitializeOnLoad]
    public static class CreatureSemanticLongTestRunner
    {
        private const string ActiveKey = "DigiCreatures.LongTest.Active";
        private const string RunningKey = "DigiCreatures.LongTest.Running";
        private const string StartTicksKey = "DigiCreatures.LongTest.StartTicks";
        private const string DurationKey = "DigiCreatures.LongTest.DurationSeconds";
        private const string VisualModeKey = "DigiCreatures.LongTest.VisualMode";
        private const string OutputRootKey = "DigiCreatures.LongTest.OutputRoot";
        private const string OriginalCreatureDataPathKey = "DigiCreatures.LongTest.OriginalCreatureDataPath";
        private const string OriginalBackendKey = "DigiCreatures.LongTest.OriginalBackend";
        private const string NextFrameSecondsKey = "DigiCreatures.LongTest.NextFrameSeconds";
        private const string FrameIndexKey = "DigiCreatures.LongTest.FrameIndex";
        private const string TestMemoryFile = "test-memory.jsonl";
        private const string ReportFile = "llm-long-test-report.md";
        private const string VisualReportFile = "visual-long-test-report.md";
        private const int DefaultDurationSeconds = 300;
        private const int VisualDurationSeconds = 1200;
        private const int VisualFrameIntervalSeconds = 60;

        static CreatureSemanticLongTestRunner()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("数字生物/高级设置/场景语义/运行长时间 LLM 测试")]
        public static void RunLongTest()
        {
            StartTest(DefaultDurationSeconds);
        }

        [MenuItem("数字生物/高级设置/场景语义/运行 90 秒快速 LLM 测试")]
        public static void RunQuickTest()
        {
            StartTest(90);
        }

        [MenuItem("数字生物/高级设置/场景语义/运行 20 分钟本地 LLM 视觉测试")]
        public static void RunLocalVisualTest()
        {
            StartVisualTest(VisualDurationSeconds);
        }

        private static void StartTest(int durationSeconds)
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("长时间 LLM 测试", "请先退出播放模式，再启动长时间测试。", "确定");
                return;
            }

            string semanticReport = CreatureSemanticSceneUtility.ScanAndGenerateTargets(false);
            CreatureBrain[] brains = UnityEngine.Object.FindObjectsByType<CreatureBrain>(FindObjectsInactive.Include);
            if (brains.Length == 0)
            {
                EditorUtility.DisplayDialog("长时间 LLM 测试", "场景中没有 CreatureBrain。请先运行“场景验证与修复”。", "确定");
                return;
            }

            string root = ResolveProjectPath(string.IsNullOrWhiteSpace(brains[0].CreatureDataPath)
                ? Path.Combine("Assets", "DigiCreaturesData", SafeFolderName(brains[0].name))
                : brains[0].CreatureDataPath);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, TestMemoryFile), "");

            foreach (CreatureBrain brain in brains)
            {
                Undo.RecordObject(brain, "准备长时间 LLM 测试");
                brain.UseTestMemory = true;
                brain.TestMemoryFileName = TestMemoryFile;
                brain.RecordLlmMetrics = true;
                brain.AgentMode = CreatureAgentMode.Move;
                EditorUtility.SetDirty(brain);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(RunningKey, false);
            SessionState.SetString(StartTicksKey, DateTime.UtcNow.Ticks.ToString());
            SessionState.SetInt(DurationKey, Mathf.Max(30, durationSeconds));

            Debug.Log($"长时间 LLM 测试即将开始，时长 {durationSeconds} 秒。\n{semanticReport}");
            EditorApplication.isPlaying = true;
        }

        private static void StartVisualTest(int durationSeconds)
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("本地 LLM 视觉长测", "请先退出播放模式，再启动视觉长测。", "确定");
                return;
            }

            string semanticReport = CreatureSemanticSceneUtility.ScanAndGenerateTargets(false);
            CreatureBrain[] brains = UnityEngine.Object.FindObjectsByType<CreatureBrain>(FindObjectsInactive.Include);
            if (brains.Length == 0)
            {
                EditorUtility.DisplayDialog("本地 LLM 视觉长测", "场景中没有 CreatureBrain。请先运行“确保数字生物在场景中”。", "确定");
                return;
            }

            CreatureLlmSettings settings = CreatureAgentConsoleWindow.LoadOrCreateSettings();
            SessionState.SetString(OriginalBackendKey, settings.backend);
            settings.backend = "ollama";
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            string originalDataPath = brains[0].CreatureDataPath;
            SessionState.SetString(OriginalCreatureDataPathKey, originalDataPath);
            string outputRoot = Path.GetFullPath(Path.Combine("Library", "DigiCreaturesTestRuns", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            string creatureDataRoot = Path.Combine(outputRoot, "CreatureData");
            Directory.CreateDirectory(creatureDataRoot);
            string sourceRoot = string.IsNullOrWhiteSpace(originalDataPath)
                ? Path.Combine("Assets", "DigiCreaturesData", SafeFolderName(brains[0].name))
                : originalDataPath;
            CopyIfExists(Path.Combine(ResolveProjectPath(sourceRoot), "soul.md"), Path.Combine(creatureDataRoot, "soul.md"));
            CopyIfExists(Path.Combine(ResolveProjectPath(sourceRoot), "summary.md"), Path.Combine(creatureDataRoot, "summary.md"));
            File.WriteAllText(Path.Combine(creatureDataRoot, TestMemoryFile), "");
            File.WriteAllText(Path.Combine(outputRoot, "semantic-scan-report.txt"), semanticReport);

            foreach (CreatureBrain brain in brains)
            {
                Undo.RecordObject(brain, "准备本地 LLM 视觉长测");
                brain.CreatureDataPath = creatureDataRoot;
                brain.LlmSettings = settings;
                brain.UseTestMemory = true;
                brain.TestMemoryFileName = TestMemoryFile;
                brain.RecordLlmMetrics = true;
                brain.AgentMode = CreatureAgentMode.Move;
                EditorUtility.SetDirty(brain);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(VisualModeKey, true);
            SessionState.SetString(OutputRootKey, outputRoot);
            SessionState.SetString(StartTicksKey, DateTime.UtcNow.Ticks.ToString());
            SessionState.SetInt(DurationKey, Mathf.Max(60, durationSeconds));
            SessionState.SetFloat(NextFrameSecondsKey, 1f);
            SessionState.SetInt(FrameIndexKey, 0);

            Debug.Log($"本地 LLM 视觉长测即将开始，时长 {durationSeconds} 秒，输出目录：{outputRoot}\n{semanticReport}");
            EditorApplication.isPlaying = true;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false) || !Application.isPlaying)
            {
                return;
            }

            int duration = SessionState.GetInt(DurationKey, DefaultDurationSeconds);
            DateTime start = ReadStartTime();
            if (SessionState.GetBool(VisualModeKey, false))
            {
                CaptureFrameIfDue(start);
            }

            if ((DateTime.UtcNow - start).TotalSeconds >= duration)
            {
                Debug.Log("长时间 LLM 测试到达设定时长，正在退出播放模式并生成报告。");
                EditorApplication.isPlaying = false;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(ActiveKey, false))
            {
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.SetBool(RunningKey, true);
            }
            else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(RunningKey, false))
            {
                FinishTest();
            }
        }

        private static void FinishTest()
        {
            bool visualMode = SessionState.GetBool(VisualModeKey, false);
            SessionState.SetBool(ActiveKey, false);
            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(VisualModeKey, false);

            CreatureBrain[] brains = UnityEngine.Object.FindObjectsByType<CreatureBrain>(FindObjectsInactive.Include);
            string outputRoot = SessionState.GetString(OutputRootKey, string.Empty);
            string root = visualMode && !string.IsNullOrWhiteSpace(outputRoot)
                ? Path.Combine(outputRoot, "CreatureData")
                : brains.Length > 0
                    ? ResolveProjectPath(brains[0].CreatureDataPath)
                    : ResolveProjectPath(Path.Combine("Assets", "DigiCreaturesData", "Creature01"));

            foreach (CreatureBrain brain in brains)
            {
                Undo.RecordObject(brain, "恢复正式记忆设置");
                if (visualMode)
                {
                    brain.CreatureDataPath = SessionState.GetString(OriginalCreatureDataPathKey, string.Empty);
                    brain.LlmSettings = CreatureAgentConsoleWindow.LoadOrCreateSettings();
                }

                brain.UseTestMemory = false;
                brain.RecordLlmMetrics = false;
                EditorUtility.SetDirty(brain);
            }

            if (visualMode)
            {
                CreatureLlmSettings settings = CreatureAgentConsoleWindow.LoadOrCreateSettings();
                settings.backend = SessionState.GetString(OriginalBackendKey, settings.backend);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            string report = visualMode
                ? BuildVisualReport(root, outputRoot, ReadStartTime(), DateTime.UtcNow)
                : BuildReport(root, ReadStartTime(), DateTime.UtcNow);
            string reportPath = Path.Combine(visualMode ? outputRoot : root, visualMode ? VisualReportFile : ReportFile);
            File.WriteAllText(reportPath, report);
            if (!visualMode)
            {
                AssetDatabase.ImportAsset(ToAssetPath(reportPath));
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log("长时间 LLM 测试报告：\n" + report);
            EditorUtility.DisplayDialog("长时间 LLM 测试完成", report, "确定");
        }

        private static string BuildReport(string root, DateTime start, DateTime end)
        {
            string memoryPath = Path.Combine(root, TestMemoryFile);
            string[] lines = File.Exists(memoryPath)
                ? File.ReadAllLines(memoryPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()
                : Array.Empty<string>();

            int decisions = Count(lines, "\"type\":\"decision\"");
            int arrivals = Count(lines, "\"type\":\"arrived\"");
            int dialogues = Count(lines, "\"type\":\"dialogue\"");
            int moveFailures = Count(lines, "\"type\":\"move_failed\"");
            int fallbacks = Count(lines, "\"type\":\"decision_fallback\"") + Count(lines, "\"type\":\"interaction_failed\"") + moveFailures;
            int metrics = Count(lines, "\"type\":\"llm_metrics\"");
            int llmFailures = lines.Count(line => line.Contains("\"type\":\"llm_metrics\"", StringComparison.Ordinal) &&
                                                 line.Contains("success=False", StringComparison.OrdinalIgnoreCase));

            List<long> latencies = ExtractLatencies(lines);
            List<string> targets = ExtractTargets(lines);
            string averageLatency = latencies.Count == 0 ? "无" : $"{latencies.Average():0} ms";
            string maxLatency = latencies.Count == 0 ? "无" : $"{latencies.Max()} ms";
            string distinctTargets = targets.Count == 0 ? "无" : string.Join(", ", targets.Distinct().Take(12));

            return "# DigiCreatures 长时间 LLM 测试报告\n\n" +
                   $"- 开始时间 UTC：{start:o}\n" +
                   $"- 结束时间 UTC：{end:o}\n" +
                   $"- 测试时长：{(end - start).TotalSeconds:0} 秒\n" +
                   $"- 记忆文件：{TestMemoryFile}\n" +
                   $"- LLM 指标条目：{metrics}\n" +
                   $"- 平均延迟：{averageLatency}\n" +
                   $"- 最高延迟：{maxLatency}\n" +
                   $"- 成功移动到达：{arrivals}\n" +
                   $"- 移动失败/卡住保护：{moveFailures}\n" +
                   $"- 移动决策：{decisions}\n" +
                   $"- 对话次数：{dialogues}\n" +
                   $"- fallback/互动失败：{fallbacks}\n" +
                   $"- LLM JSON 或请求失败：{llmFailures}\n" +
                   $"- 目标多样性：{targets.Distinct().Count()} 个\n" +
                   $"- 最近目标：{distinctTargets}\n";
        }

        private static string BuildVisualReport(string root, string outputRoot, DateTime start, DateTime end)
        {
            string memoryPath = Path.Combine(root, TestMemoryFile);
            string[] lines = File.Exists(memoryPath)
                ? File.ReadAllLines(memoryPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()
                : Array.Empty<string>();

            int decisions = Count(lines, "\"type\":\"decision\"");
            int arrivals = Count(lines, "\"type\":\"arrived\"");
            int dialogues = Count(lines, "\"type\":\"dialogue\"");
            int localActivities = Count(lines, "\"type\":\"local_activity\"");
            int interactions = Count(lines, "\"type\":\"interaction\"");
            int moveFailures = Count(lines, "\"type\":\"move_failed\"");
            int fallbacks = Count(lines, "\"type\":\"decision_fallback\"") + Count(lines, "\"type\":\"interaction_failed\"") + moveFailures;
            int metrics = Count(lines, "\"type\":\"llm_metrics\"");
            int llmFailures = lines.Count(line => line.Contains("\"type\":\"llm_metrics\"", StringComparison.Ordinal) &&
                                                 line.Contains("success=False", StringComparison.OrdinalIgnoreCase));

            List<long> latencies = ExtractLatencies(lines);
            List<string> targets = ExtractTargets(lines);
            List<string> dialoguesSeen = ExtractTokenAfter(lines, "Dialogue: ");
            string[] frames = Directory.Exists(outputRoot)
                ? Directory.GetFiles(outputRoot, "frame_*.png").OrderBy(path => path).ToArray()
                : Array.Empty<string>();

            int jumpActions = Count(lines, "jump_animation");
            int moveActions = Count(lines, "move_object");
            int spawnActions = Count(lines, "spawn_prefab");
            string averageLatency = latencies.Count == 0 ? "无" : $"{latencies.Average():0} ms";
            string maxLatency = latencies.Count == 0 ? "无" : $"{latencies.Max()} ms";
            string distinctTargets = targets.Count == 0 ? "无" : string.Join(", ", targets.Distinct().Take(12));
            string languageVerdict = dialoguesSeen.Distinct().Count() >= 3 ? "较好：有多句不同表达。" : "偏少：建议强化 prompt，要求每次移动也说一句短话。";
            string actionVerdict = interactions > 0 || localActivities > 0 ? "有动作表现。" : "偏少：建议在语义物体面板给更多对象添加 jump_animation/move_object/spawn_prefab。";
            string movementVerdict = arrivals > 0 && targets.Distinct().Count() > 1 ? "移动和目标多样性可用。" : "移动或目标多样性不足，建议检查 NavMesh/语义点。";
            string recentDialogues = dialoguesSeen.Count == 0 ? "无" : string.Join(" / ", dialoguesSeen.TakeLast(Mathf.Min(5, dialoguesSeen.Count)));

            return "# DigiCreatures 本地 LLM 视觉长测报告\n\n" +
                   $"- 输出目录：{outputRoot}\n" +
                   $"- 开始时间 UTC：{start:o}\n" +
                   $"- 结束时间 UTC：{end:o}\n" +
                   $"- 测试时长：{(end - start).TotalSeconds:0} 秒\n" +
                   $"- 截图数量：{frames.Length}\n" +
                   $"- 记忆文件：{memoryPath}\n" +
                   $"- LLM 指标条目：{metrics}\n" +
                   $"- 平均延迟：{averageLatency}\n" +
                   $"- 最高延迟：{maxLatency}\n" +
                   $"- 移动决策：{decisions}\n" +
                   $"- 成功移动到达：{arrivals}\n" +
                   $"- 移动失败/卡住保护：{moveFailures}\n" +
                   $"- 对话次数：{dialogues}\n" +
                   $"- 头顶气泡文本次数：{dialoguesSeen.Count}\n" +
                   $"- 最近气泡文本：{recentDialogues}\n" +
                   $"- 原地动作次数：{localActivities}\n" +
                   $"- 互动触发次数：{interactions}\n" +
                   $"- jump_animation：{jumpActions}\n" +
                   $"- move_object：{moveActions}\n" +
                   $"- spawn_prefab：{spawnActions}\n" +
                   $"- fallback/互动失败：{fallbacks}\n" +
                   $"- LLM JSON 或请求失败：{llmFailures}\n" +
                   $"- 目标多样性：{targets.Distinct().Count()} 个\n" +
                   $"- 最近目标：{distinctTargets}\n\n" +
                   "## 视觉/智能体感审查\n\n" +
                   $"- 语言表现：{languageVerdict}\n" +
                   $"- 动作表现：{actionVerdict}\n" +
                   $"- 移动表现：{movementVerdict}\n\n" +
                   "## 智能动作建议接口\n\n" +
                   "- 本轮测试不在 Play Mode 中实时写 C#。\n" +
                   "- 可将本报告交给本地或线上 LLM/Unity AI Assistant，让它基于现有 `CreatureInteractionAction`、`CreatureIdleMotion` 和语义物体动作生成候选动作脚本或 AnimationClip 配置建议。\n";
        }

        private static int Count(IEnumerable<string> lines, string token)
        {
            return lines.Count(line => line.Contains(token, StringComparison.Ordinal));
        }

        private static List<long> ExtractLatencies(IEnumerable<string> lines)
        {
            List<long> values = new List<long>();
            foreach (string line in lines)
            {
                int index = line.IndexOf("latencyMs=", StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                int start = index + "latencyMs=".Length;
                int end = start;
                while (end < line.Length && char.IsDigit(line[end]))
                {
                    end++;
                }

                if (long.TryParse(line.Substring(start, end - start), out long value))
                {
                    values.Add(value);
                }
            }

            return values;
        }

        private static List<string> ExtractTargets(IEnumerable<string> lines)
        {
            List<string> targets = new List<string>();
            foreach (string line in lines)
            {
                int index = line.IndexOf("Target: ", StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                int start = index + "Target: ".Length;
                int end = line.IndexOf('.', start);
                if (end < 0)
                {
                    end = line.Length;
                }

                string target = line.Substring(start, end - start).Trim();
                if (!string.IsNullOrWhiteSpace(target))
                {
                    targets.Add(target);
                }
            }

            return targets;
        }

        private static List<string> ExtractTokenAfter(IEnumerable<string> lines, string token)
        {
            List<string> values = new List<string>();
            foreach (string line in lines)
            {
                int index = line.IndexOf(token, StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                string value = line.Substring(index + token.Length).Trim();
                int end = value.IndexOf('.', StringComparison.Ordinal);
                if (end >= 0)
                {
                    value = value.Substring(0, end).Trim();
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }

            return values;
        }

        private static void CaptureFrameIfDue(DateTime start)
        {
            string outputRoot = SessionState.GetString(OutputRootKey, string.Empty);
            if (string.IsNullOrWhiteSpace(outputRoot))
            {
                return;
            }

            double elapsed = (DateTime.UtcNow - start).TotalSeconds;
            float next = SessionState.GetFloat(NextFrameSecondsKey, 1f);
            if (elapsed < next)
            {
                return;
            }

            Directory.CreateDirectory(outputRoot);
            int index = SessionState.GetInt(FrameIndexKey, 0);
            string path = Path.Combine(outputRoot, $"frame_{index:000}.png");
            ScreenCapture.CaptureScreenshot(path);
            SessionState.SetInt(FrameIndexKey, index + 1);
            SessionState.SetFloat(NextFrameSecondsKey, next + VisualFrameIntervalSeconds);
        }

        private static void CopyIfExists(string source, string destination)
        {
            if (!File.Exists(source))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, true);
        }

        private static DateTime ReadStartTime()
        {
            string ticksText = SessionState.GetString(StartTicksKey, DateTime.UtcNow.Ticks.ToString());
            return long.TryParse(ticksText, out long ticks) ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.UtcNow;
        }

        private static string ResolveProjectPath(string assetRelativePath)
        {
            if (Path.IsPathRooted(assetRelativePath))
            {
                return assetRelativePath;
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetRelativePath));
        }

        private static string SafeFolderName(string value)
        {
            string source = string.IsNullOrWhiteSpace(value) ? "Creature01" : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                source = source.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(source) ? "Creature01" : source;
        }

        private static string ToAssetPath(string absolutePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return absolutePath.StartsWith(projectRoot, StringComparison.Ordinal)
                ? absolutePath.Substring(projectRoot.Length + 1).Replace("\\", "/")
                : absolutePath;
        }
    }
}
