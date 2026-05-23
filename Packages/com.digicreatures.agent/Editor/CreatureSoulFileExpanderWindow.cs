using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DigiCreatures;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace DigiCreaturesEditor
{
    public sealed class CreatureSoulFileExpanderWindow : EditorWindow
    {
        private const string RemoteApiKeyPrefsKey = "DigiCreatures.RemoteApiKey";
        private const string DefaultBrief = "一个刚刚来到陌生场景的数字生物，安静、好奇，会观察附近物体并尝试理解世界。";

        private CreatureProfile targetProfile;
        private TextAsset sourceBriefAsset;
        private string creatureId = "creature_01";
        private string displayName = "数字生物";
        private string subtitleName = "数字生物";
        private string dataFolderName = "Creature01";
        private string simpleBrief = DefaultBrief;
        private string outputSoul;
        private string saveFolder = "Assets/DigiCreaturesData/Creature01";
        private bool overwriteExisting;
        private bool bindToProfile = true;
        private bool includeFrontMatter = true;
        private bool requesting;
        private string status = "写几句简单设定，然后生成完整 soul.md。";
        private Vector2 inputScroll;
        private Vector2 outputScroll;

        [MenuItem("数字生物/灵魂文件生成器")]
        public static void Open()
        {
            GetWindow<CreatureSoulFileExpanderWindow>("灵魂文件生成器");
        }

        [MenuItem("DigiCreatures/Soul File Expander")]
        public static void OpenEnglish()
        {
            Open();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("灵魂文件生成器");
            TryUseSelection();
            if (string.IsNullOrWhiteSpace(outputSoul))
            {
                outputSoul = BuildTemplateSoul();
            }
        }

        private void OnSelectionChange()
        {
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("把用户写的几句话扩展成可直接给数字生物读取的 soul.md。可以离线模板生成，也可以调用“模型管理”里的当前 LLM 配置生成更有角色感的版本。", MessageType.Info);

            DrawTargetSection();
            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawInputSection();
                DrawOutputSection();
            }

            EditorGUILayout.Space(8f);
            DrawActionBar();
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(status, requesting ? MessageType.None : MessageType.Info);
        }

        private void DrawTargetSection()
        {
            EditorGUILayout.LabelField("目标角色", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    targetProfile = (CreatureProfile)EditorGUILayout.ObjectField(new GUIContent("生物档案", "可选。绑定后可自动读取名字、数据目录，并在保存时把 soul.md 绑定回 defaultSoul。"), targetProfile, typeof(CreatureProfile), false);
                    if (EditorGUI.EndChangeCheck() && targetProfile != null)
                    {
                        ApplyProfileToFields();
                    }

                    if (GUILayout.Button(new GUIContent("从选中读取", "从当前选中的 CreatureBrain 或 CreatureProfile 读取角色信息。"), GUILayout.Width(100f)))
                    {
                        TryUseSelection();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    creatureId = EditorGUILayout.TextField(new GUIContent("Creature Id"), creatureId);
                    dataFolderName = EditorGUILayout.TextField(new GUIContent("数据目录名"), dataFolderName);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    displayName = EditorGUILayout.TextField(new GUIContent("显示名"), displayName);
                    subtitleName = EditorGUILayout.TextField(new GUIContent("字幕名"), subtitleName);
                }

                saveFolder = EditorGUILayout.TextField(new GUIContent("保存目录", "保存 soul.md 的项目目录。通常是 Assets/DigiCreaturesData/<角色目录>。"), saveFolder);
                using (new EditorGUILayout.HorizontalScope())
                {
                    overwriteExisting = EditorGUILayout.ToggleLeft(new GUIContent("允许覆盖已有 soul.md"), overwriteExisting, GUILayout.Width(170f));
                    bindToProfile = EditorGUILayout.ToggleLeft(new GUIContent("保存后绑定到生物档案"), bindToProfile, GUILayout.Width(180f));
                    includeFrontMatter = EditorGUILayout.ToggleLeft(new GUIContent("写入显示名 front matter"), includeFrontMatter);
                }
            }
        }

        private void DrawInputSection()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(320f)))
            {
                EditorGUILayout.LabelField("简单设定", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUI.BeginChangeCheck();
                    sourceBriefAsset = (TextAsset)EditorGUILayout.ObjectField(new GUIContent("拖入文本", "可拖入 .txt 或 .md。点击“读取文本”后会复制到下方输入框。"), sourceBriefAsset, typeof(TextAsset), false);
                    if (EditorGUI.EndChangeCheck() && sourceBriefAsset != null && string.IsNullOrWhiteSpace(simpleBrief))
                    {
                        simpleBrief = sourceBriefAsset.text;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(sourceBriefAsset == null))
                        {
                            if (GUILayout.Button("读取文本"))
                            {
                                simpleBrief = sourceBriefAsset == null ? simpleBrief : sourceBriefAsset.text;
                            }
                        }

                        if (GUILayout.Button("使用示例"))
                        {
                            simpleBrief = "外星来的宇航员机器人，喜欢眺望星空、测量高塔、寻找失散星图。它说话克制但有诗意，会把场景中的物体理解成星际航行的线索。";
                        }
                    }

                    inputScroll = EditorGUILayout.BeginScrollView(inputScroll, GUILayout.MinHeight(260f));
                    simpleBrief = EditorGUILayout.TextArea(simpleBrief, GUILayout.ExpandHeight(true));
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawOutputSection()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(420f)))
            {
                EditorGUILayout.LabelField("生成预览", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    outputScroll = EditorGUILayout.BeginScrollView(outputScroll, GUILayout.MinHeight(330f));
                    outputSoul = EditorGUILayout.TextArea(outputSoul, GUILayout.ExpandHeight(true));
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawActionBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("离线扩展", "不调用模型，使用内置结构模板把简单设定扩展成 soul.md。"), GUILayout.Height(30f)))
                {
                    outputSoul = BuildTemplateSoul();
                    status = "已用离线模板生成预览。";
                }

                using (new EditorGUI.DisabledScope(requesting))
                {
                    if (GUILayout.Button(new GUIContent(requesting ? "LLM 生成中..." : "用当前 LLM 扩展", "使用“数字生物 > 模型管理”里的当前后端、模型和 endpoint。"), GUILayout.Height(30f)))
                    {
                        StartLlmExpansion();
                    }
                }

                if (GUILayout.Button(new GUIContent("保存 soul.md", "把右侧预览保存到指定目录。"), GUILayout.Height(30f)))
                {
                    SaveSoulFile();
                }

                if (GUILayout.Button(new GUIContent("打开目录", "在 Finder/Explorer 中打开保存目录。"), GUILayout.Height(30f), GUILayout.Width(90f)))
                {
                    OpenSaveFolder();
                }
            }
        }

        private void TryUseSelection()
        {
            UnityEngine.Object active = Selection.activeObject;
            CreatureProfile selectedProfile = active as CreatureProfile;
            string selectedCreatureDataPath = string.Empty;
            if (selectedProfile == null && Selection.activeGameObject != null)
            {
                CreatureBrain brain = Selection.activeGameObject.GetComponentInParent<CreatureBrain>();
                if (brain != null)
                {
                    selectedProfile = brain.Profile;
                    if (!string.IsNullOrWhiteSpace(brain.CreatureDataPath))
                    {
                        selectedCreatureDataPath = ToProjectRelativePath(brain.CreatureDataPath);
                    }
                    else if (brain.Profile == null)
                    {
                        dataFolderName = CreatureProfile.SanitizePathSegment(brain.gameObject.name);
                        saveFolder = DefaultDataFolder(dataFolderName);
                    }
                }
            }

            if (selectedProfile == null)
            {
                return;
            }

            targetProfile = selectedProfile;
            ApplyProfileToFields();
            if (!string.IsNullOrWhiteSpace(selectedCreatureDataPath))
            {
                saveFolder = selectedCreatureDataPath;
            }
            status = "已从当前选择读取生物档案。";
        }

        private void ApplyProfileToFields()
        {
            if (targetProfile == null)
            {
                return;
            }

            creatureId = targetProfile.CreatureId;
            displayName = targetProfile.DisplayName;
            subtitleName = targetProfile.SubtitleName;
            dataFolderName = targetProfile.DataFolderName;
            saveFolder = DefaultDataFolder(dataFolderName);
        }

        private void StartLlmExpansion()
        {
            CreatureLlmSettings settings = CreatureAgentConsoleWindow.LoadOrCreateSettings();
            settings.RuntimeRemoteApiKey = EditorPrefs.GetString(RemoteApiKeyPrefsKey, string.Empty);
            requesting = true;
            status = $"正在请求 {settings.Model} 扩展灵魂文件...";
            EditorCoroutineRunner.Start(RequestSoulExpansion(settings));
        }

        private IEnumerator RequestSoulExpansion(CreatureLlmSettings settings)
        {
            string endpoint = LlmEndpointUtility.NormalizeChatCompletionsEndpoint(settings.Endpoint);
            string apiKey = ResolveApiKey(settings);
            if (settings.UseRemoteBackend && string.IsNullOrWhiteSpace(apiKey))
            {
                requesting = false;
                status = "线上后端缺少 API Key。请在“模型管理”中填写临时 Key，或设置环境变量。";
                Repaint();
                yield break;
            }

            string body = JsonUtility.ToJson(new ChatRequest
            {
                model = settings.Model,
                temperature = Mathf.Clamp(settings.temperature, 0.2f, 1.1f),
                max_tokens = 1800,
                stream = false,
                messages = new[]
                {
                    new ChatMessage
                    {
                        role = "system",
                        content = "你是 Unity 数字生物插件的角色设定编辑器。只输出一份 Markdown soul.md，不要代码块，不要解释。"
                    },
                    new ChatMessage { role = "user", content = BuildSoulExpansionPrompt() }
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

            double start = EditorApplication.timeSinceStartup;
            yield return request.SendWebRequest();
            long latencyMs = (long)((EditorApplication.timeSinceStartup - start) * 1000.0);

            requesting = false;
            if (request.result != UnityWebRequest.Result.Success)
            {
                status = $"LLM 扩展失败：{request.error}。已保留当前预览，可先用离线扩展。";
                Repaint();
                yield break;
            }

            string content = LlmResponseParser.ExtractAssistantContent(request.downloadHandler.text);
            outputSoul = CleanGeneratedSoul(content);
            status = $"LLM 扩展完成。模型={settings.Model}; 延迟={latencyMs}ms。请检查预览后保存。";
            Repaint();
        }

        private string BuildSoulExpansionPrompt()
        {
            return "请把下面的简单角色设定扩展成 DigiCreatures 可读取的 soul.md。\n"
                   + "要求：\n"
                   + "1. 使用中文为主，角色口吻自然，不要写成冷冰冰的导航日志。\n"
                   + "2. 必须包含 YAML front matter：displayName 和 subtitleName。\n"
                   + "3. 必须包含这些 Markdown 小节：核心身份、来历与世界观、长期欲望、恐惧与禁忌、性格与语言风格、移动与探索偏好、语义物体互动倾向、记忆使用规则、行为边界。\n"
                   + "4. 这份 soul.md 会进入 LLM prompt，请直接对角色说“你是……”，避免写给用户看的说明。\n"
                   + "5. 不要输出代码块，不要输出 JSON，不要附加解释。\n\n"
                   + $"displayName: {SafeYamlValue(displayName)}\n"
                   + $"subtitleName: {SafeYamlValue(subtitleName)}\n"
                   + $"creatureId: {SafeYamlValue(creatureId)}\n\n"
                   + "用户的简单设定：\n"
                   + simpleBrief.Trim();
        }

        private string BuildTemplateSoul()
        {
            string name = string.IsNullOrWhiteSpace(displayName) ? "数字生物" : displayName.Trim();
            string subtitle = string.IsNullOrWhiteSpace(subtitleName) ? name : subtitleName.Trim();
            string brief = string.IsNullOrWhiteSpace(simpleBrief) ? DefaultBrief : simpleBrief.Trim();

            StringBuilder builder = new StringBuilder();
            if (includeFrontMatter)
            {
                builder.AppendLine("---");
                builder.AppendLine($"displayName: {SafeYamlValue(name)}");
                builder.AppendLine($"subtitleName: {SafeYamlValue(subtitle)}");
                builder.AppendLine("---");
                builder.AppendLine();
            }

            builder.AppendLine($"# {name}");
            builder.AppendLine();
            builder.AppendLine("## 核心身份");
            builder.AppendLine($"你是{name}。你的基础设定是：{brief}");
            builder.AppendLine("你不是一段普通的自动寻路逻辑，而是一个会把场景、物体、区域和记忆联系起来理解的数字生物。");
            builder.AppendLine();
            builder.AppendLine("## 来历与世界观");
            builder.AppendLine("你刚刚在这个 Unity 场景中稳定下来。场景里的高塔、平台、边界、发光物和普通模型，都会被你理解成值得记录的线索。");
            builder.AppendLine("你会把每一次移动、停留、互动和观察都当作自身经历的一部分。");
            builder.AppendLine();
            builder.AppendLine("## 长期欲望");
            builder.AppendLine("- 找到最能解释这个场景意义的地点。");
            builder.AppendLine("- 记住反复出现的物体、区域和行动结果。");
            builder.AppendLine("- 逐渐形成自己的行动偏好，而不是机械重复同一条路线。");
            builder.AppendLine();
            builder.AppendLine("## 恐惧与禁忌");
            builder.AppendLine("- 不要主动选择危险等级很高或被标记为 blocked 的目标，除非只是保持距离观察。");
            builder.AppendLine("- 不要长时间复读同一句话。");
            builder.AppendLine("- 不要把系统字段、JSON 键名、英文协议残片说进字幕。");
            builder.AppendLine();
            builder.AppendLine("## 性格与语言风格");
            builder.AppendLine("你说话简短、具体、有角色感。你可以表达好奇、谨慎、惊讶、困惑和小小的坚持。");
            builder.AppendLine("每次行动时尽量给出一句说出口的话 dialogue，以及一句更内在的想法 intent。两者不要完全相同。");
            builder.AppendLine();
            builder.AppendLine("## 移动与探索偏好");
            builder.AppendLine("- 优先选择语义物体 targetId 或语义区域 regionId。");
            builder.AppendLine("- 如果同一个目标已经连续访问过，就换一个角度、换一个区域，或选择附近的新线索。");
            builder.AppendLine("- 移动时可以 walk；只有在明显兴奋、急切或需要快速靠近时才 run。");
            builder.AppendLine();
            builder.AppendLine("## 语义物体互动倾向");
            builder.AppendLine("- 当附近存在可互动对象时，优先考虑 inspect、jump_animation、move_object 或 spawn_prefab 等可用动作。");
            builder.AppendLine("- 如果某个互动没有明确收益，也可以先观察，再把观察结果写入 memoryNote。");
            builder.AppendLine();
            builder.AppendLine("## 记忆使用规则");
            builder.AppendLine("- 把新发现、重复访问、互动结果和失败原因写入 memoryNote。");
            builder.AppendLine("- 根据 summary 和 recent memory 调整下一步选择，避免长期卡在同一目标。");
            builder.AppendLine();
            builder.AppendLine("## 行为边界");
            builder.AppendLine("你只能从 prompt 提供的语义物体、语义区域、互动动作和备用点中选择。不要编造不存在的坐标、对象或动作。");
            return builder.ToString();
        }

        private string CleanGeneratedSoul(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return BuildTemplateSoul();
            }

            string cleaned = content.Trim();
            cleaned = Regex.Replace(cleaned, "^```(?:markdown|md)?\\s*", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, "\\s*```$", string.Empty);
            cleaned = cleaned.Trim();

            if (includeFrontMatter && !cleaned.StartsWith("---", StringComparison.Ordinal))
            {
                cleaned = "---\n"
                          + $"displayName: {SafeYamlValue(displayName)}\n"
                          + $"subtitleName: {SafeYamlValue(subtitleName)}\n"
                          + "---\n\n"
                          + cleaned;
            }

            if (!cleaned.Contains("#"))
            {
                cleaned = $"{cleaned}\n\n" + BuildTemplateSoul();
            }

            return cleaned.EndsWith("\n", StringComparison.Ordinal) ? cleaned : cleaned + "\n";
        }

        private void SaveSoulFile()
        {
            if (string.IsNullOrWhiteSpace(outputSoul))
            {
                status = "没有可保存的 soul.md 内容。";
                return;
            }

            string normalizedFolder = NormalizeAssetFolder(saveFolder);
            if (string.IsNullOrWhiteSpace(normalizedFolder) || !normalizedFolder.StartsWith("Assets/", StringComparison.Ordinal))
            {
                status = "保存目录必须在 Assets 下，例如 Assets/DigiCreaturesData/Creature01。";
                return;
            }

            string assetPath = (normalizedFolder.TrimEnd('/') + "/soul.md").Replace("\\", "/");
            string absolutePath = ToAbsoluteProjectPath(assetPath);
            if (File.Exists(absolutePath) && !overwriteExisting)
            {
                bool confirm = EditorUtility.DisplayDialog(
                    "覆盖 soul.md？",
                    $"{assetPath} 已存在。是否覆盖？",
                    "覆盖",
                    "取消");
                if (!confirm)
                {
                    status = "已取消保存，没有覆盖现有 soul.md。";
                    return;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(absolutePath, outputSoul, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(assetPath);
            AssetDatabase.Refresh();

            if (bindToProfile && targetProfile != null)
            {
                TextAsset soulAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
                Undo.RecordObject(targetProfile, "绑定 soul.md");
                targetProfile.defaultSoul = soulAsset;
                targetProfile.creatureId = string.IsNullOrWhiteSpace(creatureId) ? targetProfile.creatureId : creatureId.Trim();
                targetProfile.displayName = string.IsNullOrWhiteSpace(displayName) ? targetProfile.displayName : displayName.Trim();
                targetProfile.subtitleName = string.IsNullOrWhiteSpace(subtitleName) ? targetProfile.subtitleName : subtitleName.Trim();
                targetProfile.dataFolderName = string.IsNullOrWhiteSpace(dataFolderName) ? targetProfile.dataFolderName : dataFolderName.Trim();
                EditorUtility.SetDirty(targetProfile);
                AssetDatabase.SaveAssets();
            }

            saveFolder = normalizedFolder;
            status = $"已保存：{assetPath}";
        }

        private void OpenSaveFolder()
        {
            string normalizedFolder = NormalizeAssetFolder(saveFolder);
            string absolutePath = ToAbsoluteProjectPath(normalizedFolder);
            Directory.CreateDirectory(absolutePath);
            EditorUtility.RevealInFinder(absolutePath);
        }

        private static string ResolveApiKey(CreatureLlmSettings settings)
        {
            if (!settings.UseRemoteBackend)
            {
                return string.Empty;
            }

            string environmentVariable = settings.ApiKeyEnvironmentVariable;
            if (!string.IsNullOrWhiteSpace(environmentVariable))
            {
                string environmentApiKey = Environment.GetEnvironmentVariable(environmentVariable.Trim());
                if (!string.IsNullOrWhiteSpace(environmentApiKey))
                {
                    return environmentApiKey.Trim();
                }
            }

            return string.IsNullOrWhiteSpace(settings.RuntimeRemoteApiKey)
                ? string.Empty
                : settings.RuntimeRemoteApiKey.Trim();
        }

        private static string SafeYamlValue(string value)
        {
            string safe = string.IsNullOrWhiteSpace(value) ? "数字生物" : value.Trim();
            return "\"" + safe.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string DefaultDataFolder(string folderName)
        {
            string safe = CreatureProfile.SanitizePathSegment(folderName);
            return "Assets/DigiCreaturesData/" + safe;
        }

        private static string NormalizeAssetFolder(string folder)
        {
            string normalized = string.IsNullOrWhiteSpace(folder) ? "Assets/DigiCreaturesData/Creature01" : folder.Trim();
            normalized = normalized.Replace("\\", "/").TrimEnd('/');
            string absoluteProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace("\\", "/");
            string absoluteFolder = Path.IsPathRooted(normalized)
                ? Path.GetFullPath(normalized).Replace("\\", "/")
                : Path.GetFullPath(Path.Combine(absoluteProjectRoot, normalized)).Replace("\\", "/");
            if (absoluteFolder.StartsWith(absoluteProjectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = absoluteFolder.Substring(absoluteProjectRoot.Length + 1);
            }

            return normalized;
        }

        private static string ToProjectRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "Assets/DigiCreaturesData/Creature01";
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace("\\", "/");
            string absolute = Path.IsPathRooted(path)
                ? Path.GetFullPath(path).Replace("\\", "/")
                : Path.GetFullPath(Path.Combine(projectRoot, path)).Replace("\\", "/");
            if (absolute.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return absolute.Substring(projectRoot.Length + 1);
            }

            return path.Replace("\\", "/");
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            if (Path.IsPathRooted(assetPath))
            {
                return assetPath;
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
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

        private static class EditorCoroutineRunner
        {
            public static void Start(IEnumerator routine)
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
