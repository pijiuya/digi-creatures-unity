using DigiCreatures;
using UnityEditor;
using UnityEngine;

namespace DigiCreaturesEditor
{
    [CustomEditor(typeof(CreatureInteractable))]
    public sealed class CreatureInteractableEditor : Editor
    {
        private bool showBackend;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox("可互动对象面向开发者保留名称、描述、半径和动作；互动 ID 和标签属于 LLM 协议字段，收在高级里。", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"), new GUIContent("显示名称", "给编辑器和提示词使用的人类可读名称。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("description"), new GUIContent("描述", "告诉智能体这个物体是什么、有什么特征、为什么值得互动。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("interactionRadius"), new GUIContent("互动半径", "智能体必须进入这个距离以内，互动动作才会真正执行。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("actions"), new GUIContent("可执行动作", "每个动作都有 actionId。LLM 会在附近对象里选择 interactionId 和 actionId。"), true);

            showBackend = EditorGUILayout.Foldout(showBackend, new GUIContent("高级/后端", "这些字段会进入 LLM prompt 或作为模型返回 JSON 的稳定标识。"), true);
            if (showBackend)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("interactionId"), new GUIContent("互动 ID", "给 LLM 使用的稳定标识。建议使用英文、小写、无空格，例如 door_01。"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("tags"), new GUIContent("标签", "用逗号分隔的语义标签，例如 fragile,magic,door。LLM 会参考它判断是否互动。"));
                }
            }

            serializedObject.ApplyModifiedProperties();
            DrawTestButtons((CreatureInteractable)target);
        }

        private static void DrawTestButtons(CreatureInteractable interactable)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(new GUIContent("动作测试", "这些按钮只在编辑器里出现，用来快速确认互动动作确实有可见反馈。"), EditorStyles.boldLabel);
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("进入 Play Mode 后可以直接测试动作。非播放模式不会执行，避免误改场景物体。", MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying || interactable == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawActionButton(interactable, "测试 inspect", "inspect");
                    DrawActionButton(interactable, "测试 move_object", "move_object");
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawActionButton(interactable, "测试 jump_animation", "jump_animation");
                    DrawActionButton(interactable, "测试 spawn_prefab", "spawn_prefab");
                }
            }
        }

        private static void DrawActionButton(CreatureInteractable interactable, string label, string actionId)
        {
            if (!GUILayout.Button(label))
            {
                return;
            }

            if (interactable.TryExecute(actionId, null, out string result))
            {
                Debug.Log($"DigiCreatures interaction test: {result}", interactable);
            }
            else
            {
                Debug.LogWarning($"DigiCreatures interaction test failed: {result}", interactable);
            }
        }
    }
}
