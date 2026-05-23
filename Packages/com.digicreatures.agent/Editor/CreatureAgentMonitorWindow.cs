using DigiCreatures;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DigiCreaturesEditor
{
    public sealed class CreatureAgentMonitorWindow : EditorWindow
    {
        private Vector2 scroll;

        [MenuItem("数字生物/决策监控")]
        [MenuItem("数字生命/决策监控")]
        public static void Open()
        {
            GetWindow<CreatureAgentMonitorWindow>("数字生命决策监控");
        }

        private void OnEnable()
        {
            CreatureAgentDecisionLog.Changed += Repaint;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            CreatureAgentDecisionLog.Changed -= Repaint;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawCurrentAgent();
            EditorGUILayout.Space(8f);
            DrawDecisionHistory();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("数字生物行为原因", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("清空记录", "只清空窗口中的临时显示，不删除 memory.jsonl。"), EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    CreatureAgentDecisionLog.Clear();
                }
            }
        }

        private void DrawCurrentAgent()
        {
            CreatureBrain brain = CreatureObjectFinder.FindAnyObjectByType<CreatureBrain>(true);
            if (brain == null)
            {
                EditorGUILayout.HelpBox("当前场景没有找到 CreatureBrain。请先运行“数字生物/确保数字生物在场景中”。", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("当前智能体", brain.name);
                EditorGUILayout.LabelField("当前状态", EmptyToDash(brain.LastStatus));
                EditorGUILayout.LabelField("当前动作", EmptyToDash(brain.LastActivity));
                EditorGUILayout.LabelField("最近意图", EmptyToDash(brain.CurrentIntent));
                EditorGUILayout.LabelField("最近目标", EmptyToDash(brain.LastTargetId));
                EditorGUILayout.LabelField("最近区域", EmptyToDash(brain.LastRegionId));
                EditorGUILayout.LabelField("最近兴趣理由", EmptyToDash(brain.LastTargetInterest));
                EditorGUILayout.LabelField("最近延迟", brain.LastLlmLatencyMs <= 0 ? "-" : brain.LastLlmLatencyMs + " ms");
                if (!string.IsNullOrWhiteSpace(brain.LastDecisionError))
                {
                    EditorGUILayout.HelpBox(brain.LastDecisionError, MessageType.Warning);
                }
            }
        }

        private void DrawDecisionHistory()
        {
            IReadOnlyList<CreatureAgentDecisionEntry> entries = CreatureAgentDecisionLog.Snapshot;
            EditorGUILayout.LabelField("决策记录", EditorStyles.boldLabel);
            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("播放模式中每次数字生物收到 LLM 决策、进入对话、进入休息/动作或触发兜底行为时，会在这里记录原因。", MessageType.None);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (CreatureAgentDecisionEntry entry in entries)
            {
                DrawEntry(entry);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawEntry(CreatureAgentDecisionEntry entry)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(entry.time.ToString("HH:mm:ss"), GUILayout.Width(70f));
                    EditorGUILayout.LabelField(EmptyToDash(entry.status), EditorStyles.boldLabel, GUILayout.Width(90f));
                    EditorGUILayout.LabelField(EmptyToDash(entry.activity), GUILayout.Width(80f));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(entry.latencyMs <= 0 ? "-" : entry.latencyMs + " ms", GUILayout.Width(80f));
                }

                EditorGUILayout.LabelField("目标", EmptyToDash(entry.target));
                EditorGUILayout.LabelField("区域", EmptyToDash(entry.region));
                EditorGUILayout.LabelField("移动点", EmptyToDash(entry.destination));
                if (!string.IsNullOrWhiteSpace(entry.targetInterest))
                {
                    EditorGUILayout.LabelField("目标吸引点", entry.targetInterest);
                }

                EditorGUILayout.LabelField("内心想法", EmptyToDash(entry.reason));
                EditorGUILayout.LabelField("说了什么", EmptyToDash(entry.dialogue));

                if (!string.IsNullOrWhiteSpace(entry.error))
                {
                    EditorGUILayout.HelpBox(entry.error, MessageType.Warning);
                }
            }
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            Repaint();
        }

        private static string EmptyToDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
    }
}
