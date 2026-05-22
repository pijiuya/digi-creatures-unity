using DigiCreatures;
using UnityEditor;
using UnityEngine;

namespace DigiCreaturesEditor
{
    [CustomEditor(typeof(CreatureBrain))]
    public sealed class CreatureBrainEditor : Editor
    {
        private bool showAdvanced;
        private bool showRuntime = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox("这里保留开发时常用的智能体控制项；数据目录、测试记忆和模型资产引用等后端字段已收进“高级/后端”。", MessageType.Info);
            EditorGUILayout.LabelField(new GUIContent("开发者常用", "这些是调试智能体行为时最常改的字段。"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("profile"), new GUIContent("生物档案", "可选。用于配置生物 ID、显示名、默认 soul/summary 和模型配置。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("startOnAwake"), new GUIContent("启动时自动思考", "进入播放模式后自动开始 LLM 决策循环。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("agentMode"), new GUIContent("运行模式", "移动模式会让 LLM 在对象接近、互动、散步、休息之间决策；对话模式会优先说话并写入记忆。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("interactionScanRadius"), new GUIContent("互动感知半径", "半径内的 CreatureInteractable 会进入 prompt，模型才会知道它们可互动。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("interactionExecutionGrace"), new GUIContent("互动执行余量", "到达动态采样点后允许多远仍执行互动，避免 NavMesh 接近点离物体半径边缘太远。"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(new GUIContent("实时反应", "这些字段决定智能体响应速度和是否依赖固定移动点。"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("decisionIntervalSeconds"), new GUIContent("决策间隔", "一次行动结束后多久请求下一次 LLM。调小会更活跃，但请求更多。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maximumDwellSeconds"), new GUIContent("最长停留", "限制模型返回的 dwellSeconds，避免它在一个点位站太久。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("wanderRadius"), new GUIContent("散步半径", "activity=wander 或兜底散步时，会在当前位置附近随机采样 NavMesh。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("preferObjectsOverFixedMarkers"), new GUIContent("优先接近物体", "开启后模型选择 targetId 或 interactionId 时，运行时会动态采样接近点，而不是固定去少数 Marker。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("localReactionsWhileThinking"), new GUIContent("等待模型时也有反应", "LLM 响应较慢时，智能体会看向附近目标，并把等待状态写入“决策监控”。Game 窗口底部字幕只显示真实对话和内心想法。"));

            showAdvanced = EditorGUILayout.Foldout(showAdvanced, new GUIContent("高级/后端", "这些字段主要给系统读取。普通调试时通常不用改。"), true);
            if (showAdvanced)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("creatureDataPath"), new GUIContent("数据目录", "保存 soul.md、memory.jsonl、summary.md 的智能体数据目录。为空时按生物档案自动使用 Assets/DigiCreaturesData 或 persistentDataPath。"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("llmSettings"), new GUIContent("模型配置", "可选。为空时优先使用生物档案里的模型配置，再使用运行时默认配置。"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("useTestMemory"), new GUIContent("使用测试记忆", "长时间测试时写入 test-memory.jsonl，避免污染正式 memory.jsonl。"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("testMemoryFileName"), new GUIContent("测试记忆文件", "使用测试记忆时写入的数据文件名。"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("recordLlmMetrics"), new GUIContent("记录模型指标", "写入每次 LLM 请求的延迟、成功状态和错误摘要，主要用于长测报告。"));
                }
            }

            EditorGUILayout.Space(8f);
            showRuntime = EditorGUILayout.Foldout(showRuntime, new GUIContent("运行时调试", "这些字段由播放模式中的 LLM 决策更新，只用于观察。"), true);
            if (showRuntime)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawReadonly("当前意图", serializedObject.FindProperty("currentIntent"));
                    DrawReadonly("最近对话", serializedObject.FindProperty("lastDialogue"));
                    DrawReadonly("上次活动", serializedObject.FindProperty("lastActivity"));
                    DrawReadonly("上次状态", serializedObject.FindProperty("lastStatus"));
                    DrawReadonly("上次目标点", serializedObject.FindProperty("lastDestinationId"));
                    DrawReadonly("上次语义目标", serializedObject.FindProperty("lastTargetId"));
                    DrawReadonly("上次语义区域", serializedObject.FindProperty("lastRegionId"));
                    DrawReadonly("上次兴趣理由", serializedObject.FindProperty("lastTargetInterest"));
                    DrawReadonly("上次模型延迟 ms", serializedObject.FindProperty("lastLlmLatencyMs"));
                    DrawReadonly("上次决策错误", serializedObject.FindProperty("lastDecisionError"));
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("绑定项目模型配置", "绑定或创建 Assets/Creatures/CreatureLlmSettings.asset。")))
                {
                    CreatureBrain brain = (CreatureBrain)target;
                    Undo.RecordObject(brain, "绑定项目模型配置");
                    brain.LlmSettings = CreatureAgentConsoleWindow.LoadOrCreateSettings();
                    EditorUtility.SetDirty(brain);
                }

                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    if (GUILayout.Button(new GUIContent("场景验证与修复", "自动补齐智能体、移动点、调试器和模型配置。")))
                    {
                        CreatureSceneRepairUtility.ValidateAndRepairCurrentScene();
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawReadonly(string label, SerializedProperty property)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
            }
        }
    }
}
