using DigiCreatures;
using UnityEditor;
using UnityEngine;

namespace DigiCreaturesEditor
{
    [CustomEditor(typeof(CreatureLocationMarker))]
    public sealed class CreatureLocationMarkerEditor : Editor
    {
        private bool showBackend;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox("移动点主要是导航后端给 LLM 看的可达锚点。普通调试时只需要改显示名称、说明和优先级；ID、语义绑定、Nav 区域放在高级里。", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"), new GUIContent("显示名称", "给开发者和模型看的移动点名称。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("description"), new GUIContent("说明", "说明这个点位靠近什么物体、适合观察还是停留。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("priority"), new GUIContent("优先级", "兜底选择固定点时使用。对象接近和散步会优先动态采样 NavMesh。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("navigationKind"), new GUIContent("导航类型", "这个点代表的语义类型，例如平台、可攀爬入口或观察点。"));

            showBackend = EditorGUILayout.Foldout(showBackend, new GUIContent("高级/后端", "这些字段用于 LLM 协议和自动场景扫描，一般不用手写。"), true);
            if (showBackend)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("id"), new GUIContent("移动点 ID", "LLM 返回 destinationId/approachPointId 时使用的稳定标识。"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("tags"), new GUIContent("提示标签", "会进入 prompt，帮助模型理解点位用途。"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("semanticTargetId"), new GUIContent("绑定语义目标", "该点靠近的 CreatureSemanticTarget ID。"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("navAreaName"), new GUIContent("Nav 区域名", "扫描时采样到的 Unity NavMesh area 名称。"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("isSemanticGenerated"), new GUIContent("语义扫描生成", "开启表示这个点由场景语义扫描自动创建或维护。"));
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
