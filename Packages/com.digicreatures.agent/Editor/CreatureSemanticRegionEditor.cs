using DigiCreatures;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace DigiCreaturesEditor
{
    [CustomEditor(typeof(CreatureSemanticRegion))]
    public sealed class CreatureSemanticRegionEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox("语义区域不是固定目标点。LLM 选择 regionId 后，运行时会在这个区域内随机采样 NavMesh 可达点。", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("regionId"), new GUIContent("区域 ID"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"), new GUIContent("显示名称"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("description"), new GUIContent("描述"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("tags"), new GUIContent("标签"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("priority"), new GUIContent("兴趣权重"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("dangerLevel"), new GUIContent("危险等级"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("shape"), new GUIContent("区域形状"));

            CreatureRegionShape shape = (CreatureRegionShape)serializedObject.FindProperty("shape").enumValueIndex;
            if (shape == CreatureRegionShape.Sphere)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("radius"), new GUIContent("半径"));
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("boxSize"), new GUIContent("盒形尺寸"));
            }

            CreatureSemanticRegion region = (CreatureSemanticRegion)target;
            EditorGUILayout.LabelField("中心 NavMesh", SampleArea(region));
            serializedObject.ApplyModifiedProperties();
        }

        private static string SampleArea(CreatureSemanticRegion region)
        {
            if (region == null ||
                !NavMesh.SamplePosition(region.transform.position, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            {
                return "附近未采样到 NavMesh";
            }

            return $"可采样，距离中心 {Vector3.Distance(region.transform.position, hit.position):0.0}";
        }
    }
}
