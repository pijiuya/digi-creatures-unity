using DigiCreatures;
using UnityEditor;
using UnityEngine;

namespace DigiCreaturesEditor
{
    [CustomEditor(typeof(CreatureCameraRig))]
    public sealed class CreatureCameraRigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("target"), new GUIContent("参考目标", "仅作为记录，不再驱动摄像机跟随或注视。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetCamera"), new GUIContent("目标摄像机", "该摄像机会保持进入播放时的固定机位。"));

            SerializedProperty mode = serializedObject.FindProperty("cameraMode");
            int selected = GUILayout.Toolbar(mode.enumValueIndex, new[] { "固定机位", "固定机位" });
            mode.enumValueIndex = selected;

            EditorGUILayout.HelpBox("当前版本用于固定机位拍摄：播放时保持摄像机初始位置和旋转，不再跟随或注视智能体。", MessageType.Info);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("运行时运动镜头", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableMovementFraming"), new GUIContent("启用临时运动镜头"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("movementFrameChanceWhenFixedHidden"), new GUIContent("固定不可见时全程镜头概率"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("movementShotMinDistance"), new GUIContent("最短触发距离"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("movementShotMaxDistance"), new GUIContent("最长触发距离"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("movementShotMargin"), new GUIContent("画面余量"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("movementShotHeightPadding"), new GUIContent("高度余量"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fixedReturnPadding"), new GUIContent("回固定机位判定余量"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("useFixedCameraOcclusionCheck"), new GUIContent("固定机位遮挡检测"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fixedCameraOcclusionLayers"), new GUIContent("遮挡检测层"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fixedCameraRequiredVisibleRayRatio"), new GUIContent("最小可见射线比例"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("occlusionRaycastSkin"), new GUIContent("遮挡检测余量"));
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("第三人称临时镜头", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("allowManualThirdPersonToggle"), new GUIContent("允许按键切换"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("manualThirdPersonToggleKey"), new GUIContent("切换按键"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("thirdPersonOffset"), new GUIContent("第三人称偏移"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("thirdPersonYawOffsetDegrees"), new GUIContent("第三人称朝向偏移"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lookAtHeight"), new GUIContent("注视高度"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("positionDamping"), new GUIContent("位置阻尼"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rotationDamping"), new GUIContent("旋转阻尼"));

            serializedObject.ApplyModifiedProperties();
        }
    }
}
