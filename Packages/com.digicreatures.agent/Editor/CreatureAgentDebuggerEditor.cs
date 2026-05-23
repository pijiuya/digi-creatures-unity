using System.IO;
using DigiCreatures;
using UnityEditor;
using UnityEngine;

namespace DigiCreaturesEditor
{
    [CustomEditor(typeof(CreatureAgentDebugger))]
    public sealed class CreatureAgentDebuggerEditor : Editor
    {
        private bool showSoul = true;
        private bool showActions = true;
        private TextAsset soulAsset;

        [MenuItem("数字生物/高级设置/调试/创建智能体调试器")]
        public static void CreateAgentDebugger()
        {
            GameObject debuggerObject = new GameObject("智能体调试器");
            Undo.RegisterCreatedObjectUndo(debuggerObject, "创建智能体调试器");
            CreatureAgentDebugger debugger = debuggerObject.AddComponent<CreatureAgentDebugger>();
            debugger.targetAgent = CreatureObjectFinder.FindAnyObjectByType<CreatureBrain>(false);
            Selection.activeObject = debuggerObject;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetAgent"), new GUIContent("目标智能体", "拖入带有 CreatureBrain 的 GameObject。这个调试器会读取并控制它的模式、灵魂文本和动作状态。"));
            serializedObject.ApplyModifiedProperties();

            CreatureAgentDebugger debugger = (CreatureAgentDebugger)target;
            CreatureBrain agent = debugger.targetAgent;

            if (agent == null)
            {
                EditorGUILayout.HelpBox("请拖入一个带 CreatureBrain 的目标智能体，用来查看灵魂文本和动作状态。", MessageType.Info);
                return;
            }

            DrawMode(agent);
            DrawSoul(agent);
            DrawActions(agent);
        }

        private void DrawMode(CreatureBrain agent)
        {
            EditorGUILayout.LabelField(new GUIContent("运行模式", "移动模式会让智能体选择地点并移动；对话模式会优先停留、说话并记录记忆。"));
            int selectedMode = GUILayout.Toolbar(agent.AgentMode == CreatureAgentMode.Dialogue ? 1 : 0, new[] { "移动模式", "对话模式" });
            CreatureAgentMode mode = selectedMode == 1 ? CreatureAgentMode.Dialogue : CreatureAgentMode.Move;
            if (mode != agent.AgentMode)
            {
                Undo.RecordObject(agent, "设置智能体运行模式");
                agent.AgentMode = mode;
                EditorUtility.SetDirty(agent);
            }

            EditorGUILayout.LabelField(new GUIContent("最近对话", "运行时由对话模式或 LLM 决策生成的最后一句话。"), new GUIContent(string.IsNullOrWhiteSpace(agent.LastDialogue) ? "（暂无）" : agent.LastDialogue));
        }

        private void DrawSoul(CreatureBrain agent)
        {
            showSoul = EditorGUILayout.Foldout(showSoul, new GUIContent("灵魂文本", "灵魂文本会作为智能体的人格、偏好和行为风格输入给模型。"), true);
            if (!showSoul)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                soulAsset = (TextAsset)EditorGUILayout.ObjectField(new GUIContent("拖入文本", "拖入 .txt 或 .md 文本资源，然后点击“绑定拖入文本”复制到该智能体的 soul.md。"), soulAsset, typeof(TextAsset), false);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(soulAsset == null))
                    {
                        if (GUILayout.Button(new GUIContent("绑定拖入文本", "把上方拖入的文本复制为当前智能体的 soul.md。")))
                        {
                            BindSoul(agent, soulAsset);
                        }
                    }

                    if (GUILayout.Button(new GUIContent("新建", "如果当前智能体还没有 soul.md，则创建一个默认灵魂文本文件。")))
                    {
                        CreateNewSoul(agent);
                    }
                }

                string fullPath = ResolveProjectPath(Path.Combine(agent.CreatureDataPath, "soul.md"));
                if (File.Exists(fullPath))
                {
                    EditorGUILayout.LabelField(new GUIContent("当前文件", "当前智能体实际读取的灵魂文本路径。"), new GUIContent(Path.Combine(agent.CreatureDataPath, "soul.md")));
                    EditorGUILayout.TextArea(File.ReadAllText(fullPath), GUILayout.MinHeight(110f));
                }
                else
                {
                    EditorGUILayout.HelpBox("这个智能体还没有 soul.md。可以点击“新建”，或拖入文本后点击“绑定拖入文本”。", MessageType.Warning);
                }
            }
        }

        private void DrawActions(CreatureBrain agent)
        {
            showActions = EditorGUILayout.Foldout(showActions, new GUIContent("动作与互动", "查看目标智能体的移动组件，以及场景里所有可被智能体互动的物体。"), true);
            if (!showActions)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                CreatureMotor motor = agent.GetComponent<CreatureMotor>();
                EditorGUILayout.LabelField(new GUIContent("移动能力", "CreatureMotor 负责 NavMesh 或直接移动。"), new GUIContent(motor == null ? "未找到 CreatureMotor" : "已挂载 CreatureMotor"));

                CreatureInteractable[] interactables = CreatureObjectFinder.FindObjectsByType<CreatureInteractable>(false);
                EditorGUILayout.LabelField(new GUIContent("场景可互动对象", "挂载 CreatureInteractable 的对象数量。LLM 会在附近对象中选择 interactionId 和 actionId。"), new GUIContent(interactables.Length.ToString()));
                foreach (CreatureInteractable interactable in interactables)
                {
                    if (interactable == null)
                    {
                        continue;
                    }

                    EditorGUILayout.LabelField(interactable.interactionId, $"{interactable.displayName}（{interactable.actions.Count} 个动作）");
                }

                if (GUILayout.Button(new GUIContent("选中目标智能体", "在 Hierarchy 中选中当前调试器绑定的智能体。")))
                {
                    Selection.activeObject = agent.gameObject;
                }
            }
        }

        private static void BindSoul(CreatureBrain agent, TextAsset source)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            string destinationAssetPath = Path.Combine(agent.CreatureDataPath, "soul.md").Replace("\\", "/");
            Directory.CreateDirectory(Path.GetDirectoryName(destinationAssetPath));
            File.Copy(sourcePath, destinationAssetPath, true);
            AssetDatabase.ImportAsset(destinationAssetPath);
            AssetDatabase.Refresh();
        }

        private static void CreateNewSoul(CreatureBrain agent)
        {
            string destinationAssetPath = Path.Combine(agent.CreatureDataPath, "soul.md").Replace("\\", "/");
            Directory.CreateDirectory(Path.GetDirectoryName(destinationAssetPath));
            if (!File.Exists(destinationAssetPath))
            {
                File.WriteAllText(destinationAssetPath, "# 数字灵魂\n\n一个好奇的数字生物。\n");
            }

            AssetDatabase.ImportAsset(destinationAssetPath);
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(destinationAssetPath);
        }

        private static string ResolveProjectPath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        }
    }
}
