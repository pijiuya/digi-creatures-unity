using DigiCreatures;
using UnityEditor;
using UnityEngine;

namespace DigiCreaturesEditor
{
    [CustomEditor(typeof(CreatureMotor))]
    public sealed class CreatureMotorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("agent"), new GUIContent("NavMesh 代理", "优先使用 NavMeshAgent 进行移动；如果不可用，会退回到直接移动。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("animator"), new GUIContent("动画控制器", "可选。存在 Animator 时会同步速度参数。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("visualRoot"), new GUIContent("视觉根节点", "只校正模型朝向，不影响 NavMeshAgent 的真实移动方向。通常选择 Animator 所在的模型子物体。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("visualYawOffsetDegrees"), new GUIContent("视觉朝向校正", "当模型看起来横着走或倒着走时调整。Demo 机器人子物体天然有 90 度偏转，所以这里默认使用 -90。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("walkSpeed"), new GUIContent("行走速度", "LLM 选择 walk 时使用的移动速度。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("runSpeed"), new GUIContent("奔跑速度", "LLM 选择 run 时使用的移动速度。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("arriveDistance"), new GUIContent("到达距离", "距离目标点小于这个值时视为到达。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("directMoveTurnSpeed"), new GUIContent("直接移动转向速度", "NavMesh 不可用时，直接移动模式的转向速度。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxMoveSeconds"), new GUIContent("单次移动最短超时", "实际超时会结合目标距离和速度动态放宽，防止远处目标误判为卡住。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("stuckTimeoutSeconds"), new GUIContent("卡住提前放弃", "NavMeshAgent 长时间没有位置进展时提前结束本次移动，让大脑更快重新决策。"));

            CreatureMotor motor = (CreatureMotor)target;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle(new GUIContent("正在移动", "播放模式中显示当前移动状态。"), motor.IsMoving);
                EditorGUILayout.Toggle(new GUIContent("上次移动成功", "播放模式中显示最近一次移动是否真实到达目标点。"), motor.LastMoveSucceeded);
                EditorGUILayout.TextField(new GUIContent("上次移动问题", "播放模式中显示最近一次移动失败或超时原因。"), motor.LastMoveError);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
