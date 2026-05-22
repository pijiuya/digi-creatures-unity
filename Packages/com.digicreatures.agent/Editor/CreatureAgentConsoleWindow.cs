using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DigiCreatures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace DigiCreaturesEditor
{
    public sealed class CreatureAgentConsoleWindow : EditorWindow
    {
        private const string SettingsPath = "Assets/DigiCreatures/Settings/CreatureLlmSettings.asset";
        private const string LegacySettingsPath = "Assets/Creatures/CreatureLlmSettings.asset";
        private const string RemoteApiKeyPrefsKey = "DigiCreatures.RemoteApiKey";
        private CreatureLlmSettings settings;
        private string testPrompt = BuildAgentCompatibilityPrompt();
        private string testResult;
        private string testMetrics;
        private bool testing;
        private bool comparing;
        private int comparisonRuns = 5;
        private string comparisonReport;
        private Stopwatch testStopwatch;
        private Process gatewayProcess;
        private Process listModelsProcess;
        private Process pullModelProcess;
        private readonly List<string> localModels = new List<string>();
        private readonly List<string> remoteModels = new List<string>();
        private readonly List<string> listOutput = new List<string>();
        private bool showAdvanced;
        private string modelStatus = "尚未读取本地模型。";
        private string pullingModel;
        private string pendingInstalledModel;
        private bool refreshingAfterPull;
        private float pullProgress = -1f;
        private bool pullProgressKnown;
        private string pullProgressLabel;
        private double pullStartedAt;
        private string remoteApiKeyInput;

        private static readonly Regex PercentPattern = new Regex(@"(?<!\d)(\d{1,3})(?:\.\d+)?\s*%", RegexOptions.Compiled);
        private static readonly Regex AnsiPattern = new Regex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);

        private static readonly RecommendedModel[] RecommendedModels =
        {
            new RecommendedModel("llama3.2", "轻量通用，适合作为默认智能体模型"),
            new RecommendedModel("qwen2.5:3b", "中文能力较好，体积适中"),
            new RecommendedModel("gemma3:4b", "能力更强一些，适合更复杂对话")
        };

        [MenuItem("数字生物/模型管理")]
        public static void Open()
        {
            GetWindow<CreatureAgentConsoleWindow>("模型管理");
        }

        [MenuItem("数字生物/高级设置/智能体控制台")]
        public static void OpenLegacy()
        {
            Open();
        }

        public static CreatureLlmSettings LoadOrCreateSettings()
        {
            CreatureLlmSettings existing = AssetDatabase.LoadAssetAtPath<CreatureLlmSettings>(SettingsPath);
            if (existing != null)
            {
                return existing;
            }

            CreatureLlmSettings legacy = AssetDatabase.LoadAssetAtPath<CreatureLlmSettings>(LegacySettingsPath);

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            CreatureLlmSettings created = CreateInstance<CreatureLlmSettings>();
            if (legacy != null)
            {
                EditorUtility.CopySerialized(legacy, created);
                created.RuntimeRemoteApiKey = string.Empty;
            }

            AssetDatabase.CreateAsset(created, SettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return created;
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("模型管理");
            settings = LoadOrCreateSettings();
            remoteApiKeyInput = EditorPrefs.GetString(RemoteApiKeyPrefsKey, string.Empty);
            if (settings != null)
            {
                settings.RuntimeRemoteApiKey = remoteApiKeyInput;
            }
            if (!IsAgentCompatibilityPrompt(testPrompt))
            {
                testPrompt = BuildAgentCompatibilityPrompt();
            }

            if (settings != null && !settings.UseRemoteBackend && localModels.Count == 0)
            {
                RefreshLocalModels();
            }
        }

        private void OnDisable()
        {
            if (gatewayProcess != null && gatewayProcess.HasExited)
            {
                gatewayProcess.Dispose();
                gatewayProcess = null;
            }

            DisposeFinishedProcess(ref listModelsProcess);
            DisposeFinishedProcess(ref pullModelProcess);
        }

        private void OnGUI()
        {
            if (settings == null)
            {
                settings = LoadOrCreateSettings();
            }

            SerializedObject serialized = new SerializedObject(settings);
            serialized.Update();

            SerializedProperty backend = serialized.FindProperty("backend");
            int backendIndex = string.Equals(backend.stringValue, "remote", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            int selectedBackend = GUILayout.Toolbar(backendIndex, new[] { "本地 Ollama", "线上 LLM" });
            backend.stringValue = selectedBackend == 0 ? "ollama" : "remote";

            if (selectedBackend == 0)
            {
                DrawOllamaSimplePage(serialized);
            }
            else
            {
                DrawRemoteSimplePage(serialized);
            }

            DrawAdvancedSettings(serialized, backend);

            if (serialized.ApplyModifiedProperties())
            {
                settings.RuntimeRemoteApiKey = remoteApiKeyInput;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                SyncCreatureConfigFromSettings();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    if (GUILayout.Button(new GUIContent("应用到当前场景智能体", "把这份模型配置写入当前打开场景里的所有 CreatureBrain。播放模式中会自动禁用，避免修改场景。")))
                    {
                        ApplySettingsToSceneAgents();
                    }
                }

                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    if (GUILayout.Button(new GUIContent("场景验证与修复", "检查当前场景的智能体、移动点、调试器和模型配置。缺少必要组件时会自动补齐。")))
                    {
                        CreatureSceneRepairUtility.ValidateAndRepairCurrentScene();
                    }
                }
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("当前处于播放模式：模型配置可以测试，但场景写入按钮已禁用。", MessageType.Info);
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(new GUIContent("测试响应", "用当前模型发送智能体协议测试，并显示延迟和适配度。"), EditorStyles.boldLabel);
            testPrompt = EditorGUILayout.TextArea(testPrompt, GUILayout.MinHeight(54f));
            using (new EditorGUI.DisabledScope(testing))
            {
                if (GUILayout.Button(new GUIContent(testing ? "测试中..." : "测试模型响应", "发送上方测试文本，并检查它是否能返回本项目需要的智能体 JSON。")))
                {
                    TestResponse();
                }
            }

            if (!string.IsNullOrEmpty(testMetrics))
            {
                EditorGUILayout.HelpBox(testMetrics, MessageType.Info);
            }

            if (!string.IsNullOrEmpty(testResult))
            {
                bool isError = testResult.StartsWith("Error:", StringComparison.Ordinal) ||
                               testResult.StartsWith("错误：", StringComparison.Ordinal);
                EditorGUILayout.HelpBox(testResult, isError ? MessageType.Error : MessageType.Info);
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(new GUIContent("本地 / 线上对比", "连续请求本地和线上模型，比较延迟、解析成功率、字段完整度和可执行性。不会进入播放模式，也不会写入记忆文件。"), EditorStyles.boldLabel);
            comparisonRuns = EditorGUILayout.IntSlider(new GUIContent("每端运行次数", "建议 5-10 次。"), comparisonRuns, 1, 10);
            using (new EditorGUI.DisabledScope(comparing))
            {
                if (GUILayout.Button(new GUIContent(comparing ? "对比中..." : "对比本地和线上 LLM", "使用同一份智能体协议 prompt 分别请求本地 Ollama 和线上 LLM。")))
                {
                    CompareLocalAndRemote();
                }
            }

            if (!string.IsNullOrWhiteSpace(comparisonReport))
            {
                EditorGUILayout.HelpBox(comparisonReport, MessageType.Info);
            }
        }

        private void DrawOllamaSimplePage(SerializedObject serialized)
        {
            EditorGUILayout.LabelField(new GUIContent("本地 Ollama 模型", "这里隐藏了 HTTP 地址等高级信息。一般只需要启动 Ollama、选择或下载模型，然后测试即可。"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("推荐流程：启动 Ollama -> 选择已有模型，或下载推荐模型 -> 测试模型响应 -> 应用到场景智能体。", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(IsGatewayRunning()))
                {
                    if (GUILayout.Button(new GUIContent(IsGatewayRunning() ? "Ollama 运行中" : "启动 Ollama", "执行 ollama serve。启动后会自动读取本机已有模型。")))
                    {
                        StartLocalGateway();
                    }
                }

                if (GUILayout.Button(new GUIContent("刷新本地模型", "执行 ollama list，读取本机已经下载的模型。")))
                {
                    RefreshLocalModels();
                }
            }

            EditorGUILayout.LabelField(new GUIContent("状态", "这里显示启动、刷新和下载模型的进度。"), new GUIContent(modelStatus));
            DrawPullProgress();
            DrawLocalModels(serialized.FindProperty("localModel"));
            DrawRecommendedModels();
        }

        private void DrawLocalModels(SerializedProperty localModel)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(new GUIContent("已安装模型", "来自 ollama list。点击模型名即可设为当前测试和智能体使用的模型。"), EditorStyles.boldLabel);

            if (localModels.Count == 0)
            {
                EditorGUILayout.HelpBox("还没有读取到本地模型。请先启动 Ollama，或点击“刷新本地模型”。", MessageType.Warning);
                return;
            }

            int currentIndex = Mathf.Max(0, localModels.IndexOf(localModel.stringValue));
            int selectedIndex = EditorGUILayout.Popup(new GUIContent("当前模型", "这个模型会用于测试响应，也会被写入模型配置。"), currentIndex, localModels.ToArray());
            localModel.stringValue = localModels[selectedIndex];
        }

        private void DrawRecommendedModels()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(new GUIContent("推荐模型", "如果本地没有对应模型，可以点击下载。下载完成后会自动刷新已安装模型列表。"), EditorStyles.boldLabel);

            foreach (RecommendedModel recommended in RecommendedModels)
            {
                bool installed = IsModelInstalled(recommended.Name);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent(recommended.Name, recommended.Description), GUILayout.Width(140f));
                    EditorGUILayout.LabelField(installed ? "已安装" : recommended.Description);

                    bool isCurrentPull = IsPullingModel() &&
                                         string.Equals(pullingModel, recommended.Name, StringComparison.OrdinalIgnoreCase);
                    if (isCurrentPull)
                    {
                        DrawInlinePullProgress(GUILayout.Width(130f));
                        continue;
                    }

                    if (IsRefreshingDownloadedModel(recommended.Name))
                    {
                        Rect rect = GUILayoutUtility.GetRect(120f, 18f, GUILayout.Width(130f));
                        EditorGUI.ProgressBar(rect, 1f, "下载完成，刷新中");
                        continue;
                    }

                    using (new EditorGUI.DisabledScope(installed || IsPullingModel() || refreshingAfterPull))
                    {
                        if (GUILayout.Button(new GUIContent(installed ? "已下载" : "下载", $"执行 ollama pull {recommended.Name}。"), GUILayout.Width(72f)))
                        {
                            PullModel(recommended.Name);
                        }
                    }
                }
            }
        }

        private void DrawPullProgress()
        {
            if (!IsPullingModel())
            {
                return;
            }

            UpdatePullProgressFromOutput();
            Rect rect = GUILayoutUtility.GetRect(18f, 20f, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(rect, GetDisplayedPullProgress(), BuildPullProgressText());
            EditorGUILayout.Space(3f);
        }

        private void DrawInlinePullProgress(params GUILayoutOption[] options)
        {
            UpdatePullProgressFromOutput();
            Rect rect = GUILayoutUtility.GetRect(72f, 18f, options);
            EditorGUI.ProgressBar(rect, GetDisplayedPullProgress(), pullProgressKnown ? $"{Mathf.RoundToInt(pullProgress * 100f)}%" : "下载中");
        }

        private void DrawRemoteSimplePage(SerializedObject serialized)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(new GUIContent("线上 LLM 配置", "连接任意 OpenAI-compatible chat completions 服务。配置完成后可以直接使用同一个测试按钮检查延迟和适配度。"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("remoteEndpoint"), new GUIContent("API 地址", "远程 OpenAI-compatible chat completions 地址，例如 https://api.openai.com/v1/chat/completions。"));
            SerializedProperty remoteModel = serialized.FindProperty("remoteModel");
            EditorGUILayout.PropertyField(serialized.FindProperty("remoteApiKeyEnvironmentVariable"), new GUIContent("API Key 环境变量", "运行时优先从这个环境变量读取 API Key。默认 OPENAI_API_KEY。"));
            EditorGUI.BeginChangeCheck();
            remoteApiKeyInput = EditorGUILayout.PasswordField(new GUIContent("临时 API Key", "只保存在本机 EditorPrefs，不写入项目文件，也不会进 Git。"), remoteApiKeyInput ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(RemoteApiKeyPrefsKey, remoteApiKeyInput ?? string.Empty);
                if (settings != null)
                {
                    settings.RuntimeRemoteApiKey = remoteApiKeyInput;
                }
            }

            if (GUILayout.Button(new GUIContent("读取线上模型列表", "根据 API 地址和 API Key 请求 /models，读取当前账号实际可用的模型。")))
            {
                EditorCoroutineRunner.Start(FetchRemoteModels(serialized.FindProperty("remoteEndpoint").stringValue, ResolveApiKey(settings)));
            }

            if (remoteModels.Count == 0)
            {
                EditorGUILayout.HelpBox("请先填写 API 地址和 API Key，然后点击“读取线上模型列表”。", MessageType.Info);
                if (!string.IsNullOrWhiteSpace(remoteModel.stringValue))
                {
                    EditorGUILayout.LabelField(new GUIContent("当前已保存模型", "还没有读取模型列表时，保留上一次保存的模型名。"), new GUIContent(remoteModel.stringValue));
                }
                return;
            }

            int currentIndex = Mathf.Max(0, remoteModels.IndexOf(remoteModel.stringValue));
            int selectedIndex = EditorGUILayout.Popup(new GUIContent("模型名称", "从 API 返回的模型列表中选择。"), currentIndex, remoteModels.ToArray());
            remoteModel.stringValue = remoteModels[selectedIndex];
        }

        private void DrawAdvancedSettings(SerializedObject serialized, SerializedProperty backend)
        {
            EditorGUILayout.Space(8f);
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, new GUIContent("高级设置", "平时可以收起。只有在要改端口、启动命令、远程 API 或超时时才需要打开。"), true);
            if (!showAdvanced)
            {
                return;
            }

            if (!string.Equals(backend.stringValue, "remote", StringComparison.OrdinalIgnoreCase))
            {
                EditorGUILayout.PropertyField(serialized.FindProperty("localEndpoint"), new GUIContent("本地端点", "Ollama 的 OpenAI-compatible chat completions 地址，默认是 http://localhost:11434/v1/chat/completions。"));
                EditorGUILayout.PropertyField(serialized.FindProperty("localStartCommand"), new GUIContent("启动命令", "点击启动 Ollama 时执行的 shell 命令。默认 ollama serve。"));
            }

            EditorGUILayout.PropertyField(serialized.FindProperty("temperature"), new GUIContent("温度", "控制模型回复的随机性。数值越高越发散，越低越稳定。"));
            EditorGUILayout.PropertyField(serialized.FindProperty("requestTimeoutSeconds"), new GUIContent("请求超时秒数", "模型请求最多等待多少秒后判定失败。"));
        }

        private bool IsGatewayRunning()
        {
            return gatewayProcess != null && !gatewayProcess.HasExited;
        }

        private void StartLocalGateway()
        {
            string command = settings.localStartCommand;
            if (string.IsNullOrWhiteSpace(command))
            {
                testResult = "错误：本地启动命令为空。";
                return;
            }

            try
            {
                if (string.Equals(command.Trim(), "ollama serve", StringComparison.OrdinalIgnoreCase))
                {
                    StartToolProcess("ollama", "serve", out gatewayProcess);
                }
                else
                {
                    StartProcess(command, out gatewayProcess);
                }

                if (gatewayProcess == null)
                {
                    modelStatus = "启动 Ollama 失败。";
                    return;
                }

                settings.backend = "ollama";
                modelStatus = "已请求启动 Ollama，正在读取本地模型...";
                testResult = "已启动 Ollama。";
                RefreshLocalModels();
            }
            catch (Exception ex)
            {
                testResult = "错误：" + ex.Message;
                modelStatus = "启动 Ollama 失败。";
            }
        }

        private void RefreshLocalModels()
        {
            if (listModelsProcess != null && !listModelsProcess.HasExited)
            {
                modelStatus = "正在读取本地模型...";
                return;
            }

            lock (listOutput)
            {
                listOutput.Clear();
            }
            modelStatus = "正在执行 ollama list...";
            StartToolProcess("ollama", "list", out listModelsProcess);
            if (listModelsProcess == null)
            {
                modelStatus = "无法执行 ollama list。请确认已安装 Ollama，并且命令行可以直接运行 ollama。";
                return;
            }

            EditorApplication.update += PollModelListProcess;
        }

        private void PullModel(string model)
        {
            if (IsPullingModel())
            {
                return;
            }

            lock (listOutput)
            {
                listOutput.Clear();
            }
            pullingModel = model;
            pullProgress = -1f;
            pullProgressKnown = false;
            pullProgressLabel = "准备下载";
            pullStartedAt = EditorApplication.timeSinceStartup;
            modelStatus = $"正在下载 {model}，这可能需要几分钟...";
            StartToolProcess("ollama", "pull " + QuoteArgument(model), out pullModelProcess);
            if (pullModelProcess == null)
            {
                modelStatus = "无法执行 ollama pull。请确认已安装 Ollama。";
                pullingModel = null;
                pullProgress = -1f;
                pullProgressKnown = false;
                pullProgressLabel = string.Empty;
                return;
            }

            EditorApplication.update += PollPullProcess;
        }

        private void TestResponse()
        {
            testPrompt = BuildAgentCompatibilityPrompt();

            testing = true;
            testStopwatch = Stopwatch.StartNew();
            testMetrics = string.Empty;
            testResult = "正在发送请求...";
            Repaint();
            EditorCoroutineRunner.Start(SendTestRequest());
        }

        private void CompareLocalAndRemote()
        {
            comparing = true;
            comparisonReport = "正在对比本地和线上 LLM...";
            Repaint();
            EditorCoroutineRunner.Start(RunComparison());
        }

        private void ApplySettingsToSceneAgents()
        {
            int count = 0;
            foreach (CreatureBrain brain in FindObjectsByType<CreatureBrain>(FindObjectsInactive.Include))
            {
                Undo.RecordObject(brain, "应用智能体模型配置");
                brain.LlmSettings = settings;
                EditorUtility.SetDirty(brain);
                count++;
            }

            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }

            SyncCreatureConfigFromSettings();
            testResult = $"已应用到 {count} 个场景智能体，并同步了生物配置。";
        }

        private void SyncCreatureConfigFromSettings()
        {
            if (settings == null)
            {
                return;
            }

            string configPath = ResolveConfigPathForSync();
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            CreatureConfig config = File.Exists(configPath)
                ? JsonUtility.FromJson<CreatureConfig>(File.ReadAllText(configPath))
                : new CreatureConfig();
            config.backend = settings.UseRemoteBackend ? "online" : "local";
            config.localEndpoint = settings.localEndpoint;
            config.onlineEndpoint = settings.remoteEndpoint;
            config.localModel = settings.localModel;
            config.onlineModel = settings.remoteModel;
            config.onlineApiKeyEnvironmentVariable = settings.remoteApiKeyEnvironmentVariable;
            config.temperature = settings.temperature;
            config.requestTimeoutSeconds = settings.requestTimeoutSeconds;
            File.WriteAllText(configPath, JsonUtility.ToJson(config, true));
            AssetDatabase.ImportAsset(configPath);
        }

        private static string ResolveConfigPathForSync()
        {
            CreatureBrain selectedBrain = Selection.activeGameObject == null
                ? null
                : Selection.activeGameObject.GetComponentInParent<CreatureBrain>();
            CreatureBrain brain = selectedBrain != null
                ? selectedBrain
                : FindAnyObjectByType<CreatureBrain>(FindObjectsInactive.Include);
            if (brain != null && !string.IsNullOrWhiteSpace(brain.CreatureDataPath))
            {
                return Path.Combine(brain.CreatureDataPath, "config.json").Replace("\\", "/");
            }

            string folder = brain != null && brain.Profile != null ? brain.Profile.DataFolderName : "Creature01";
            return Path.Combine("Assets", "DigiCreaturesData", folder, "config.json").Replace("\\", "/");
        }

        private System.Collections.IEnumerator SendTestRequest()
        {
            string body = JsonUtility.ToJson(new ChatRequest
            {
                model = settings.Model,
                temperature = settings.temperature,
                max_tokens = 512,
                stream = false,
                messages = new[]
                {
                    new ChatMessage { role = "system", content = "你正在测试 Unity 数字生物智能体协议。必须只返回一个 JSON 对象，不要解释，不要 Markdown。" },
                    new ChatMessage { role = "user", content = testPrompt }
                }
            });

            string endpoint = LlmEndpointUtility.NormalizeChatCompletionsEndpoint(settings.Endpoint);
            using UnityWebRequest request = new UnityWebRequest(endpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(1, settings.requestTimeoutSeconds);
            request.SetRequestHeader("Content-Type", "application/json");

            string apiKey = ResolveApiKey(settings);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            }

            yield return request.SendWebRequest();

            testing = false;
            testStopwatch.Stop();
            long latencyMs = testStopwatch.ElapsedMilliseconds;
            testResult = request.result == UnityWebRequest.Result.Success
                ? request.downloadHandler.text
                : "错误：" + request.error + "\n" + request.downloadHandler.text;
            testMetrics = request.result == UnityWebRequest.Result.Success
                ? BuildCompatibilityReport(testResult, latencyMs)
                : $"延迟：{latencyMs} ms\n适配度：不可用（请求失败）";
            Repaint();
        }

        private System.Collections.IEnumerator RunComparison()
        {
            ComparisonStats local = new ComparisonStats("本地", settings.localModel);
            ComparisonStats remote = new ComparisonStats("线上", settings.remoteModel);
            string prompt = BuildAgentCompatibilityPrompt();

            for (int i = 0; i < comparisonRuns; i++)
            {
                yield return RequestComparisonSample(
                    local,
                    LlmEndpointUtility.NormalizeChatCompletionsEndpoint(settings.localEndpoint),
                    settings.localModel,
                    string.Empty,
                    prompt);
                comparisonReport = BuildComparisonReport(local, remote, i + 1, comparisonRuns, false);
                Repaint();
            }

            string apiKey = ResolveApiKey(settings);
            for (int i = 0; i < comparisonRuns; i++)
            {
                yield return RequestComparisonSample(
                    remote,
                    LlmEndpointUtility.NormalizeChatCompletionsEndpoint(settings.remoteEndpoint),
                    settings.remoteModel,
                    apiKey,
                    prompt);
                comparisonReport = BuildComparisonReport(local, remote, i + 1, comparisonRuns, true);
                Repaint();
            }

            comparing = false;
            comparisonReport = BuildComparisonReport(local, remote, comparisonRuns, comparisonRuns, true);
            Debug.Log(comparisonReport);
            Repaint();
        }

        private System.Collections.IEnumerator RequestComparisonSample(
            ComparisonStats stats,
            string endpoint,
            string model,
            string apiKey,
            string prompt)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string body = JsonUtility.ToJson(new ChatRequest
            {
                model = model,
                temperature = settings.temperature,
                max_tokens = 512,
                stream = false,
                messages = new[]
                {
                    new ChatMessage { role = "system", content = "Return only one valid JSON object for a Unity creature decision." },
                    new ChatMessage { role = "user", content = prompt }
                }
            });

            using UnityWebRequest request = new UnityWebRequest(endpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(1, settings.requestTimeoutSeconds);
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            }

            yield return request.SendWebRequest();
            stopwatch.Stop();

            if (request.result != UnityWebRequest.Result.Success)
            {
                stats.AddFailure(stopwatch.ElapsedMilliseconds, request.error + " " + request.downloadHandler.text);
                yield break;
            }

            LlmDecisionParseReport report = LlmResponseParser.AnalyzeDecisionResponse(request.downloadHandler.text);
            stats.AddReport(stopwatch.ElapsedMilliseconds, report);
        }

        private static string BuildComparisonReport(ComparisonStats local, ComparisonStats remote, int completedInCurrentPhase, int total, bool remotePhase)
        {
            StringBuilder builder = new StringBuilder();
            string phase = remotePhase ? "线上" : "本地";
            builder.AppendLine($"进度：{phase} {completedInCurrentPhase}/{total}");
            builder.AppendLine(local.Summarize());
            builder.AppendLine(remote.Summarize());
            builder.AppendLine("说明：可解析率看 JSON 协议是否能转成 CreatureDecision；双通道率要求同时有 intent 和 dialogue；可执行率要求移动决策有 targetId/regionId/destinationId/approachPointId 或是可接受的对话/原地活动。");
            return builder.ToString();
        }

        private static string ResolveApiKey(CreatureLlmSettings source)
        {
            if (!source.UseRemoteBackend)
            {
                return string.Empty;
            }

            string environmentVariable = source.ApiKeyEnvironmentVariable;
            if (!string.IsNullOrWhiteSpace(environmentVariable))
            {
                string environmentApiKey = Environment.GetEnvironmentVariable(environmentVariable.Trim());
                if (!string.IsNullOrWhiteSpace(environmentApiKey))
                {
                    return environmentApiKey.Trim();
                }
            }

            string editorPrefsKey = EditorPrefs.GetString(RemoteApiKeyPrefsKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(editorPrefsKey))
            {
                source.RuntimeRemoteApiKey = editorPrefsKey;
                return editorPrefsKey.Trim();
            }

            return string.IsNullOrWhiteSpace(source.RuntimeRemoteApiKey)
                ? string.Empty
                : source.RuntimeRemoteApiKey.Trim();
        }

        private static string Quote(string value)
        {
            return "'" + value.Replace("'", "'\\''") + "'";
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string GetShellFileName()
        {
#if UNITY_EDITOR_WIN
            return "cmd.exe";
#else
            return "/bin/zsh";
#endif
        }

        private static string GetShellArguments(string command)
        {
#if UNITY_EDITOR_WIN
            return "/C " + QuoteArgument(command);
#else
            return "-lc " + Quote(command);
#endif
        }

        private static string BuildAgentCompatibilityPrompt()
        {
            return "你正在扮演 Unity 场景中的数字生物智能体。请只返回一个 JSON 对象，不要解释，不要 Markdown。"
                   + "必须包含这些键，不能省略任何键：mode、targetId、regionId、targetName、targetInterest、navigationKind、approachPointId、destinationId、movement、dwellSeconds、dialogue、interactionId、actionId、activity、intent、memoryNote。"
                   + "严格要求：mode 只能是 move 或 dialogue；movement 只能是 walk 或 run；targetInterest、intent、dialogue、memoryNote 必须是字符串，不能是数字、对象、数组或 null。"
                   + "语义要求：intent 是内心想法/原因；dialogue 是底部字幕里说出口的话，两者必须不同且都非空。"
                   + "角色要求：你是外星来的宇航员机器人，喜欢眺望星空、测量高塔、寻找失散星图。"
                   + "可选目标只有 tower_core，可选区域只有 plaza_observation，可选备用点只有 tower_base。请使用这个完整格式："
                   + "{\"mode\":\"move\",\"targetId\":\"tower_core\",\"targetName\":\"中心塔\","
                   + "\"regionId\":\"plaza_observation\",\"targetInterest\":\"想观察塔的高度和结构\",\"navigationKind\":\"Climbable\","
                   + "\"approachPointId\":\"tower_base\",\"destinationId\":\"tower_base\",\"movement\":\"walk\",\"dwellSeconds\":3,"
                   + "\"dialogue\":\"那座塔像星际天线。\",\"interactionId\":\"\",\"actionId\":\"\",\"activity\":\"approach\","
                   + "\"intent\":\"我想测量中心塔是否能和失散星图对齐\",\"memoryNote\":\"模型完成了一次语义区域导航协议测试\"}";
        }

        private static bool IsAgentCompatibilityPrompt(string prompt)
        {
            return !string.IsNullOrWhiteSpace(prompt) &&
                   prompt.IndexOf("destinationId", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   prompt.IndexOf("memoryNote", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildCompatibilityReport(string responseText, long latencyMs)
        {
            LlmDecisionParseReport report = LlmResponseParser.AnalyzeDecisionResponse(responseText);
            int score = 0;
            List<string> details = new List<string>();

            if (report.FoundJson)
            {
                score += 20;
                details.Add("找到智能体 JSON");
            }
            else
            {
                details.Add("未找到智能体 JSON");
            }

            if (report.Success)
            {
                score += 40;
                details.Add("可解析为智能体决策");
            }
            else
            {
                details.Add("无法解析为智能体决策：" + report.Error);
            }

            int fieldScore = report.TotalFieldCount == 0
                ? 0
                : Mathf.RoundToInt((report.FieldCount / (float)report.TotalFieldCount) * 40f);
            score += fieldScore;
            string presentFields = report.PresentFields == null || report.PresentFields.Length == 0
                ? "无"
                : string.Join(", ", report.PresentFields);
            details.Add($"协议字段命中 {report.FieldCount}/{report.TotalFieldCount}（折算 {fieldScore}/40，已命中：{presentFields}）");

            string grade = score >= 85 ? "优秀" : score >= 65 ? "可用" : score >= 40 ? "勉强" : "不适配";
            string latencyGrade = latencyMs <= 2000 ? "很快" : latencyMs <= 6000 ? "可接受" : "偏慢";
            string preview = string.IsNullOrWhiteSpace(report.AssistantContent) ? "（空）" : report.AssistantContent.Trim();
            if (preview.Length > 180)
            {
                preview = preview.Substring(0, 180) + "...";
            }

            return $"延迟：{latencyMs} ms（{latencyGrade}）\n适配度：{score}/100（{grade}）\n检查：{string.Join("，", details)}\n评估内容：{preview}";
        }

        private static string ExtractAssistantContent(string responseText)
        {
            string content = ExtractJsonStringValue(responseText, "content");
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            try
            {
                ChatResponse response = JsonUtility.FromJson<ChatResponse>(responseText);
                if (response?.choices != null && response.choices.Length > 0 && response.choices[0].message != null)
                {
                    return response.choices[0].message.content;
                }
            }
            catch
            {
                return responseText;
            }

            return responseText;
        }

        private static string ExtractJsonObject(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            string firstObject = string.Empty;
            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] != '{')
                {
                    continue;
                }

                int depth = 0;
                bool inString = false;
                bool escaping = false;
                for (int j = i; j < content.Length; j++)
                {
                    char c = content[j];
                    if (inString)
                    {
                        if (c == '"' && !escaping)
                        {
                            inString = false;
                        }

                        escaping = c == '\\' && !escaping;
                        if (c != '\\')
                        {
                            escaping = false;
                        }

                        continue;
                    }

                    if (c == '"')
                    {
                        inString = true;
                    }
                    else if (c == '{')
                    {
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            string candidate = content.Substring(i, j - i + 1);
                            if (string.IsNullOrEmpty(firstObject))
                            {
                                firstObject = candidate;
                            }

                            if (Contains(candidate, "\"mode\"") ||
                                Contains(candidate, "\"regionId\"") ||
                                Contains(candidate, "\"destinationId\"") ||
                                Contains(candidate, "\"movement\""))
                            {
                                return candidate;
                            }

                            break;
                        }
                    }
                }
            }

            return firstObject;
        }

        private static bool Contains(string value, string token)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static CreatureDecision TryParseDecisionLoosely(string json, out string reason)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                reason = "没有可解析的 JSON";
                return null;
            }

            try
            {
                CreatureDecision decision = JsonUtility.FromJson<CreatureDecision>(json);
                bool hasProtocolField = Contains(json, "\"mode\"") ||
                                        Contains(json, "\"regionId\"") ||
                                        Contains(json, "\"destinationId\"") ||
                                        Contains(json, "\"movement\"") ||
                                        Contains(json, "\"intent\"");
                if (!hasProtocolField)
                {
                    reason = "JSON 里没有智能体协议字段";
                    return null;
                }

                reason = string.Empty;
                return decision;
            }
            catch (Exception ex)
            {
                reason = "JSON 格式错误：" + ex.Message;
                return null;
            }
        }

        private static string ExtractJsonStringValue(string json, string key)
        {
            string token = "\"" + key + "\"";
            int searchFrom = 0;
            while (searchFrom < json.Length)
            {
                int keyIndex = json.IndexOf(token, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (keyIndex < 0)
                {
                    return string.Empty;
                }

                int colonIndex = json.IndexOf(':', keyIndex + token.Length);
                if (colonIndex < 0)
                {
                    return string.Empty;
                }

                int valueIndex = colonIndex + 1;
                while (valueIndex < json.Length && char.IsWhiteSpace(json[valueIndex]))
                {
                    valueIndex++;
                }

                if (valueIndex >= json.Length)
                {
                    return string.Empty;
                }

                if (json[valueIndex] != '"')
                {
                    searchFrom = valueIndex + 1;
                    continue;
                }

                int index = valueIndex + 1;
                bool escaping = false;
                while (index < json.Length)
                {
                    char c = json[index];
                    if (c == '"' && !escaping)
                    {
                        return UnescapeJsonString(json.Substring(valueIndex + 1, index - valueIndex - 1));
                    }

                    escaping = c == '\\' && !escaping;
                    if (c != '\\')
                    {
                        escaping = false;
                    }

                    index++;
                }

                return string.Empty;
            }

            return string.Empty;
        }

        private static string UnescapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t");
        }

        private System.Collections.IEnumerator FetchRemoteModels(string endpoint, string apiKey)
        {
            remoteModels.Clear();
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            {
                testResult = "错误：请先填写 API 地址和 API Key。";
                yield break;
            }

            string modelsUrl = ResolveModelsUrl(endpoint);
            testResult = "正在读取线上模型列表...";
            using UnityWebRequest request = UnityWebRequest.Get(modelsUrl);
            request.timeout = Mathf.Max(1, settings.requestTimeoutSeconds);
            request.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                testResult = "错误：读取模型列表失败：" + request.error + "\n" + request.downloadHandler.text;
                Repaint();
                yield break;
            }

            ParseRemoteModels(request.downloadHandler.text);
            if (remoteModels.Count == 0)
            {
                testResult = "错误：模型列表读取成功，但没有解析到模型名称。原始响应：\n" + request.downloadHandler.text;
                Repaint();
                yield break;
            }

            if (string.IsNullOrWhiteSpace(settings.remoteModel) || !remoteModels.Contains(settings.remoteModel))
            {
                settings.remoteModel = remoteModels[0];
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            testResult = $"已读取 {remoteModels.Count} 个线上模型。";
            Repaint();
        }

        private static string ResolveModelsUrl(string endpoint)
        {
            string trimmed = endpoint.Trim();
            int chatIndex = trimmed.IndexOf("/chat/completions", StringComparison.OrdinalIgnoreCase);
            if (chatIndex >= 0)
            {
                return trimmed.Substring(0, chatIndex) + "/models";
            }

            int responsesIndex = trimmed.IndexOf("/responses", StringComparison.OrdinalIgnoreCase);
            if (responsesIndex >= 0)
            {
                return trimmed.Substring(0, responsesIndex) + "/models";
            }

            return trimmed.TrimEnd('/') + "/models";
        }

        private void ParseRemoteModels(string response)
        {
            remoteModels.Clear();
            int searchFrom = 0;
            while (searchFrom < response.Length)
            {
                string id = ExtractJsonStringValue(response.Substring(searchFrom), "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    break;
                }

                if (!remoteModels.Contains(id))
                {
                    remoteModels.Add(id);
                }

                int nextIndex = response.IndexOf("\"id\"", searchFrom, StringComparison.OrdinalIgnoreCase);
                searchFrom = nextIndex < 0 ? response.Length : nextIndex + 4;
            }

            remoteModels.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private void StartProcess(string command, out Process process)
        {
            try
            {
                process = CreateProcess(GetShellFileName(), GetShellArguments(command));
                StartAndRead(process);
            }
            catch (Exception ex)
            {
                process = null;
                testResult = "错误：" + ex.Message;
            }
        }

        private void StartToolProcess(string fileName, string arguments, out Process process)
        {
            string resolvedFileName = ResolveToolFileName(fileName);
            try
            {
                process = CreateProcess(resolvedFileName, arguments);
                StartAndRead(process);
            }
            catch (Exception ex)
            {
                process = null;
                testResult = "错误：" + DescribeToolStartFailure(fileName, resolvedFileName, ex.Message);
            }
        }

        private static string ResolveToolFileName(string fileName)
        {
            if (string.Equals(fileName, "ollama", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveOllamaExecutablePath();
            }

            return fileName;
        }

        private static string ResolveOllamaExecutablePath()
        {
#if UNITY_EDITOR_OSX
            string[] candidates =
            {
                "/opt/homebrew/bin/ollama",
                "/usr/local/bin/ollama",
                "/Applications/Ollama.app/Contents/Resources/ollama"
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
#elif UNITY_EDITOR_LINUX
            string[] candidates =
            {
                "/usr/local/bin/ollama",
                "/usr/bin/ollama",
                "/snap/bin/ollama"
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
#endif

            return "ollama";
        }

        private static string DescribeToolStartFailure(string toolName, string resolvedFileName, string message)
        {
            if (string.Equals(toolName, "ollama", StringComparison.OrdinalIgnoreCase))
            {
                string pathHint = string.Equals(toolName, resolvedFileName, StringComparison.OrdinalIgnoreCase)
                    ? "未找到可执行文件路径"
                    : $"已尝试 {resolvedFileName}";
                return $"{message}\n{pathHint}。请确认 Ollama 已安装；Windows 需把 ollama 加入 PATH，macOS 会自动尝试 Homebrew 和 Ollama.app 常见路径。";
            }

            return message;
        }

        private static Process CreateProcess(string fileName, string arguments)
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                },
                EnableRaisingEvents = true
            };
        }

        private void StartAndRead(Process process)
        {
            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    AddProcessOutput(args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    AddProcessOutput(args.Data);
                }
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        private void AddProcessOutput(string line)
        {
            lock (listOutput)
            {
                listOutput.Add(line);
            }
        }

        private string[] CopyProcessOutput()
        {
            lock (listOutput)
            {
                return listOutput.ToArray();
            }
        }

        private string JoinProcessOutput()
        {
            return string.Join("\n", CopyProcessOutput());
        }

        private void UpdatePullProgressFromOutput()
        {
            if (string.IsNullOrWhiteSpace(pullingModel))
            {
                return;
            }

            foreach (string rawLine in CopyProcessOutput())
            {
                string line = CleanProcessLine(rawLine);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string lower = line.ToLowerInvariant();
                if (lower.Contains("pulling manifest"))
                {
                    pullProgressLabel = "读取模型清单";
                }
                else if (lower.Contains("verifying"))
                {
                    pullProgressLabel = "校验文件";
                    pullProgress = Mathf.Max(pullProgress, 0.94f);
                    pullProgressKnown = true;
                }
                else if (lower.Contains("writing manifest"))
                {
                    pullProgressLabel = "写入模型信息";
                    pullProgress = Mathf.Max(pullProgress, 0.97f);
                    pullProgressKnown = true;
                }
                else if (lower.Contains("success"))
                {
                    pullProgressLabel = "下载完成";
                    pullProgress = 1f;
                    pullProgressKnown = true;
                }
                else if (lower.Contains("pulling"))
                {
                    pullProgressLabel = "下载模型文件";
                }

                Match match = PercentPattern.Match(line);
                if (match.Success && float.TryParse(match.Groups[1].Value, out float percent))
                {
                    pullProgress = Mathf.Clamp01(percent / 100f);
                    pullProgressKnown = true;
                    pullProgressLabel = "下载模型文件";
                }
            }

            if (IsPullingModel())
            {
                string detail = pullProgressKnown
                    ? $"{Mathf.RoundToInt(pullProgress * 100f)}%"
                    : "等待 Ollama 返回下载进度";
                modelStatus = $"正在下载 {pullingModel}：{pullProgressLabel} {detail}";
            }
        }

        private float GetDisplayedPullProgress()
        {
            if (pullProgressKnown)
            {
                return Mathf.Clamp01(pullProgress);
            }

            float elapsed = Mathf.Max(0f, (float)(EditorApplication.timeSinceStartup - pullStartedAt));
            return Mathf.Lerp(0.08f, 0.92f, Mathf.PingPong(elapsed * 0.35f, 1f));
        }

        private string BuildPullProgressText()
        {
            string label = string.IsNullOrWhiteSpace(pullProgressLabel) ? "下载中" : pullProgressLabel;
            return pullProgressKnown
                ? $"{pullingModel}：{label} {Mathf.RoundToInt(pullProgress * 100f)}%"
                : $"{pullingModel}：{label}";
        }

        private static string CleanProcessLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            return AnsiPattern
                .Replace(line, string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private void PollModelListProcess()
        {
            if (listModelsProcess == null || !listModelsProcess.HasExited)
            {
                Repaint();
                return;
            }

            EditorApplication.update -= PollModelListProcess;
            if (listModelsProcess.ExitCode == 0)
            {
                ParseLocalModels();
            }
            else
            {
                localModels.Clear();
                modelStatus = "读取本地模型失败。请确认 Ollama 已安装，并且命令行可以运行 ollama list。";
                testResult = JoinProcessOutput();
            }

            DisposeFinishedProcess(ref listModelsProcess);
            Repaint();
        }

        private void PollPullProcess()
        {
            UpdatePullProgressFromOutput();
            if (pullModelProcess == null || !pullModelProcess.HasExited)
            {
                Repaint();
                return;
            }

            EditorApplication.update -= PollPullProcess;
            int exitCode = pullModelProcess.ExitCode;
            DisposeFinishedProcess(ref pullModelProcess);

            if (exitCode == 0)
            {
                pullProgress = 1f;
                pullProgressKnown = true;
                pullProgressLabel = "下载完成";
                modelStatus = $"已下载 {pullingModel}，正在刷新模型列表...";
                settings.localModel = pullingModel;
                pendingInstalledModel = pullingModel;
                refreshingAfterPull = true;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                SyncCreatureConfigFromSettings();
                RefreshLocalModels();
            }
            else
            {
                modelStatus = $"下载 {pullingModel} 失败。";
                testResult = JoinProcessOutput();
                pullingModel = null;
                pendingInstalledModel = null;
                refreshingAfterPull = false;
                pullProgress = -1f;
                pullProgressKnown = false;
                pullProgressLabel = string.Empty;
            }

            Repaint();
        }

        private void ParseLocalModels()
        {
            localModels.Clear();
            foreach (string line in CopyProcessOutput())
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("NAME", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && !localModels.Contains(parts[0]))
                {
                    localModels.Add(parts[0]);
                }
            }

            if (localModels.Count == 0)
            {
                modelStatus = "没有发现本地模型。可以从推荐模型中选择一个下载。";
                refreshingAfterPull = false;
                return;
            }

            if (!string.IsNullOrWhiteSpace(pendingInstalledModel))
            {
                if (IsModelInstalled(pendingInstalledModel))
                {
                    settings.localModel = ResolveInstalledModelName(pendingInstalledModel);
                    modelStatus = $"已下载 {settings.localModel}。当前模型：{settings.localModel}";
                    pendingInstalledModel = null;
                    refreshingAfterPull = false;
                    pullingModel = null;
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                    SyncCreatureConfigFromSettings();
                    return;
                }

                modelStatus = $"下载成功但未在 ollama list 中找到 {pendingInstalledModel}，请手动刷新或检查模型名称。";
                pendingInstalledModel = null;
                refreshingAfterPull = false;
                pullingModel = null;
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.localModel) || !IsModelInstalled(settings.localModel))
            {
                settings.localModel = localModels[0];
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                SyncCreatureConfigFromSettings();
            }

            modelStatus = $"已发现 {localModels.Count} 个本地模型。当前模型：{settings.localModel}";
        }

        private bool IsPullingModel()
        {
            return pullModelProcess != null && !pullModelProcess.HasExited;
        }

        private bool IsRefreshingDownloadedModel(string model)
        {
            return refreshingAfterPull &&
                   !string.IsNullOrWhiteSpace(pendingInstalledModel) &&
                   ModelsEquivalent(pendingInstalledModel, model);
        }

        private bool IsModelInstalled(string model)
        {
            foreach (string localModel in localModels)
            {
                if (ModelsEquivalent(localModel, model))
                {
                    return true;
                }
            }

            return false;
        }

        private string ResolveInstalledModelName(string model)
        {
            foreach (string localModel in localModels)
            {
                if (ModelsEquivalent(localModel, model))
                {
                    return localModel;
                }
            }

            return model;
        }

        private static bool ModelsEquivalent(string left, string right)
        {
            string a = NormalizeModelName(left);
            string b = NormalizeModelName(right);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(StripLatest(a), StripLatest(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeModelName(string model)
        {
            return string.IsNullOrWhiteSpace(model) ? string.Empty : model.Trim();
        }

        private static string StripLatest(string model)
        {
            return model.EndsWith(":latest", StringComparison.OrdinalIgnoreCase)
                ? model.Substring(0, model.Length - ":latest".Length)
                : model;
        }

        private static void DisposeFinishedProcess(ref Process process)
        {
            if (process == null || !process.HasExited)
            {
                return;
            }

            process.Dispose();
            process = null;
        }

        private readonly struct RecommendedModel
        {
            public RecommendedModel(string name, string description)
            {
                Name = name;
                Description = description;
            }

            public string Name { get; }
            public string Description { get; }
        }

        private sealed class ComparisonStats
        {
            private readonly string label;
            private readonly string model;
            private int count;
            private int requestFailures;
            private int parseSuccesses;
            private int schemaCompliant;
            private int dualChannel;
            private int executable;
            private long totalLatencyMs;
            private string lastIssue;

            public ComparisonStats(string label, string model)
            {
                this.label = label;
                this.model = string.IsNullOrWhiteSpace(model) ? "-" : model;
            }

            public void AddFailure(long latencyMs, string issue)
            {
                count++;
                requestFailures++;
                totalLatencyMs += latencyMs;
                lastIssue = issue;
            }

            public void AddReport(long latencyMs, LlmDecisionParseReport report)
            {
                count++;
                totalLatencyMs += latencyMs;
                if (report == null || !report.Success || report.Decision == null)
                {
                    lastIssue = report == null ? "没有报告" : report.Error;
                    return;
                }

                CreatureDecision decision = report.Decision;
                parseSuccesses++;
                if (IsRawSchemaCompliant(report.DecisionJson))
                {
                    schemaCompliant++;
                }

                if (!string.IsNullOrWhiteSpace(decision.intent) &&
                    !string.IsNullOrWhiteSpace(decision.dialogue))
                {
                    dualChannel++;
                }

                if (IsExecutable(decision))
                {
                    executable++;
                }
            }

            public string Summarize()
            {
                if (count == 0)
                {
                    return $"{label}（{model}）：尚未运行。";
                }

                long averageLatency = totalLatencyMs / Mathf.Max(1, count);
                string issue = string.IsNullOrWhiteSpace(lastIssue) ? string.Empty : $"；最近问题：{Trim(lastIssue, 90)}";
                return $"{label}（{model}）：样本 {count}，平均 {averageLatency} ms，请求失败 {requestFailures}，可解析 {parseSuccesses}/{count}，原始字段合规 {schemaCompliant}/{count}，双通道 {dualChannel}/{count}，可执行 {executable}/{count}{issue}";
            }

            private static bool IsRawSchemaCompliant(string json)
            {
                string mode = LlmResponseParser.ExtractJsonStringValue(json, "mode");
                string movement = LlmResponseParser.ExtractJsonStringValue(json, "movement");
                bool modeOk = string.Equals(mode, "move", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(mode, "dialogue", StringComparison.OrdinalIgnoreCase);
                bool movementOk = string.Equals(movement, "walk", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(movement, "run", StringComparison.OrdinalIgnoreCase);
                return modeOk &&
                       movementOk &&
                       !string.IsNullOrWhiteSpace(LlmResponseParser.ExtractJsonStringValue(json, "targetInterest")) &&
                       !string.IsNullOrWhiteSpace(LlmResponseParser.ExtractJsonStringValue(json, "intent")) &&
                       !string.IsNullOrWhiteSpace(LlmResponseParser.ExtractJsonStringValue(json, "dialogue")) &&
                       !string.IsNullOrWhiteSpace(LlmResponseParser.ExtractJsonStringValue(json, "memoryNote"));
            }

            private static bool IsExecutable(CreatureDecision decision)
            {
                if (string.Equals(decision.mode, "dialogue", StringComparison.OrdinalIgnoreCase))
                {
                    return !string.IsNullOrWhiteSpace(decision.dialogue);
                }

                if (string.Equals(decision.activity, "rest", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(decision.activity, "roll", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(decision.activity, "idle", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return !string.IsNullOrWhiteSpace(decision.targetId) ||
                       !string.IsNullOrWhiteSpace(decision.regionId) ||
                       !string.IsNullOrWhiteSpace(decision.destinationId) ||
                       !string.IsNullOrWhiteSpace(decision.approachPointId);
            }

            private static string Trim(string value, int max)
            {
                if (string.IsNullOrWhiteSpace(value) || value.Length <= max)
                {
                    return value;
                }

                return value.Substring(0, Mathf.Max(1, max - 1)) + "...";
            }
        }

        [Serializable]
        private class ChatRequest
        {
            public string model;
            public float temperature;
            public int max_tokens;
            public bool stream;
            public ChatMessage[] messages;
        }

        [Serializable]
        private class ChatMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class ChatResponse
        {
            public ChatChoice[] choices;
        }

        [Serializable]
        private class ChatChoice
        {
            public ChatMessage message;
        }

        private static class EditorCoroutineRunner
        {
            public static void Start(System.Collections.IEnumerator routine)
            {
                object current = null;
                void Tick()
                {
                    try
                    {
                        if (current is AsyncOperation operation && !operation.isDone)
                        {
                            return;
                        }

                        if (!routine.MoveNext())
                        {
                            EditorApplication.update -= Tick;
                            return;
                        }

                        current = routine.Current;
                    }
                    catch (Exception ex)
                    {
                        EditorApplication.update -= Tick;
                        Debug.LogException(ex);
                    }
                }

                EditorApplication.update += Tick;
            }
        }
    }
}
