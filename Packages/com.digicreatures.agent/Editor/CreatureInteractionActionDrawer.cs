using DigiCreatures;
using UnityEditor;
using UnityEngine;

namespace DigiCreaturesEditor
{
    [CustomPropertyDrawer(typeof(CreatureInteractionAction))]
    public sealed class CreatureInteractionActionDrawer : PropertyDrawer
    {
        private static readonly GUIContent[] ActionTypeLabels =
        {
            new GUIContent("销毁自身"),
            new GUIContent("生成 Prefab"),
            new GUIContent("移动物体"),
            new GUIContent("自定义事件"),
            new GUIContent("播放动画")
        };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            SerializedProperty actionId = property.FindPropertyRelative("actionId");
            SerializedProperty actionType = property.FindPropertyRelative("actionType");
            SerializedProperty enabled = property.FindPropertyRelative("enabled");

            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded,
                new GUIContent(string.IsNullOrWhiteSpace(actionId.stringValue) ? "互动动作" : $"互动动作：{actionId.stringValue}",
                    "展开后配置这个动作的 ID、类型和执行参数。"), true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.PropertyField(line, actionId, new GUIContent("动作 ID", "给 LLM 选择动作时使用的稳定标识，例如 inspect、break、spawn_key。"));

                line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                enabled.boolValue = EditorGUI.Toggle(line, new GUIContent("启用", "关闭后，这个动作不会出现在提示词中，也不会被执行。"), enabled.boolValue);

                line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                actionType.enumValueIndex = EditorGUI.Popup(line, new GUIContent("动作类型", "决定智能体选择该动作时实际发生什么。"), actionType.enumValueIndex, ActionTypeLabels);

                DrawTypeFields(ref line, property, (CreatureInteractionActionType)actionType.enumValueIndex);
                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            float height = 4 * EditorGUIUtility.singleLineHeight + 3 * EditorGUIUtility.standardVerticalSpacing;
            CreatureInteractionActionType type = (CreatureInteractionActionType)property.FindPropertyRelative("actionType").enumValueIndex;
            if (type == CreatureInteractionActionType.SpawnPrefab)
            {
                height += 2 * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
            }
            else if (type == CreatureInteractionActionType.MoveObject)
            {
                height += 2 * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
            }
            else if (type == CreatureInteractionActionType.PlayAnimation)
            {
                height += 3 * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
            }
            else if (type == CreatureInteractionActionType.CustomEvent)
            {
                height += EditorGUIUtility.standardVerticalSpacing +
                          EditorGUI.GetPropertyHeight(property.FindPropertyRelative("customEvent"), true);
            }

            return height;
        }

        private static void DrawTypeFields(ref Rect line, SerializedProperty property, CreatureInteractionActionType type)
        {
            switch (type)
            {
                case CreatureInteractionActionType.SpawnPrefab:
                    line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                    EditorGUI.PropertyField(line, property.FindPropertyRelative("prefabToSpawn"), new GUIContent("生成 Prefab", "执行动作时生成的 Prefab。"));
                    line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                    EditorGUI.PropertyField(line, property.FindPropertyRelative("spawnPoint"), new GUIContent("生成位置", "为空时使用当前可互动对象的位置和旋转。"));
                    break;
                case CreatureInteractionActionType.MoveObject:
                    line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                    EditorGUI.PropertyField(line, property.FindPropertyRelative("moveTarget"), new GUIContent("移动目标", "如果填写，会把当前物体移动到这个 Transform。"));
                    line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                    EditorGUI.PropertyField(line, property.FindPropertyRelative("moveOffset"), new GUIContent("移动偏移", "未填写移动目标时，按这个向量移动当前物体。"));
                    break;
                case CreatureInteractionActionType.PlayAnimation:
                    line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                    EditorGUI.PropertyField(line, property.FindPropertyRelative("animationClip"), new GUIContent("动画片段", "可选。为空时执行默认跳一下动画。"));
                    line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                    EditorGUI.PropertyField(line, property.FindPropertyRelative("jumpHeight"), new GUIContent("默认跳跃高度", "没有动画片段时使用。"));
                    line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                    EditorGUI.PropertyField(line, property.FindPropertyRelative("jumpDuration"), new GUIContent("默认跳跃时间", "没有动画片段时使用。"));
                    break;
                case CreatureInteractionActionType.CustomEvent:
                    line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                    SerializedProperty customEvent = property.FindPropertyRelative("customEvent");
                    line.height = EditorGUI.GetPropertyHeight(customEvent, true);
                    EditorGUI.PropertyField(line, customEvent, new GUIContent("自定义事件", "执行动作时触发的 UnityEvent。"), true);
                    break;
            }
        }
    }
}
