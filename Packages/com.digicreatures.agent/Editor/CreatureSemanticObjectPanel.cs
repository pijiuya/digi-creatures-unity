using System;
using System.Collections.Generic;
using System.Linq;
using DigiCreatures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DigiCreaturesEditor
{
    public sealed class CreatureSemanticObjectPanel : EditorWindow
    {
        private readonly List<Row> rows = new List<Row>();
        private Vector2 scroll;
        private string filter = string.Empty;
        private bool includeInactive;

        [MenuItem("数字生物/语义物体面板")]
        public static void Open()
        {
            CreatureSemanticObjectPanel window = GetWindow<CreatureSemanticObjectPanel>("语义物体");
            window.RefreshRows();
        }

        private void OnEnable()
        {
            RefreshRows();
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawBulkButtons();
            EditorGUILayout.Space(6f);
            DrawRows();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("刷新列表", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    RefreshRows();
                }

                GUILayout.Label("过滤", GUILayout.Width(32f));
                filter = GUILayout.TextField(filter, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120f));
                includeInactive = GUILayout.Toggle(includeInactive, "含隐藏", EditorStyles.toolbarButton, GUILayout.Width(64f));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"候选 {rows.Count}", EditorStyles.miniLabel);
            }
        }

        private void DrawBulkButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全选可见", GUILayout.Width(86f)))
                {
                    SetVisibleSelection(true);
                }

                if (GUILayout.Button("取消可见", GUILayout.Width(86f)))
                {
                    SetVisibleSelection(false);
                }

                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    if (GUILayout.Button("标记选中为语义物体"))
                    {
                        ApplySelected();
                    }

                    if (GUILayout.Button("移除选中语义标记"))
                    {
                        RemoveSelected();
                    }
                }

                if (GUILayout.Button("选择已标记物体"))
                {
                    Selection.objects = rows
                        .Where(row => row.Target != null)
                        .Select(row => row.GameObject)
                        .Cast<UnityEngine.Object>()
                        .ToArray();
                }
            }
        }

        private void DrawRows()
        {
            if (rows.Count == 0)
            {
                EditorGUILayout.HelpBox("没有找到可标注的场景 Renderer。点击“刷新列表”重新扫描。", MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Row row in rows)
            {
                if (!IsVisible(row))
                {
                    continue;
                }

                DrawRow(row);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawRow(Row row)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    row.Selected = EditorGUILayout.Toggle(row.Selected, GUILayout.Width(18f));
                    EditorGUILayout.ObjectField(row.GameObject, typeof(GameObject), true);
                    GUILayout.Label(row.Target == null ? "未标记" : "已标记", GUILayout.Width(54f));
                    if (GUILayout.Button("选中", GUILayout.Width(48f)))
                    {
                        Selection.activeGameObject = row.GameObject;
                    }
                }

                row.DisplayName = EditorGUILayout.TextField("显示名称", row.DisplayName);
                row.Description = EditorGUILayout.TextField("描述", row.Description);
                row.Tags = EditorGUILayout.TextField("语义标签", row.Tags);
                row.NavigationKind = (CreatureNavigationKind)EditorGUILayout.EnumPopup("导航类型", row.NavigationKind);
                row.DangerLevel = EditorGUILayout.IntSlider("危险等级", row.DangerLevel, 0, 5);
                row.InterestWeight = EditorGUILayout.IntField("兴趣权重", Mathf.Max(1, row.InterestWeight));

                using (new EditorGUILayout.HorizontalScope())
                {
                    row.Interactable = EditorGUILayout.ToggleLeft("可互动", row.Interactable, GUILayout.Width(78f));
                    row.Movable = EditorGUILayout.ToggleLeft("可移动", row.Movable, GUILayout.Width(78f));
                    row.PlayAnimation = EditorGUILayout.ToggleLeft("可触发动画", row.PlayAnimation, GUILayout.Width(104f));
                    row.SpawnPrefab = EditorGUILayout.ToggleLeft("可生成 Prefab", row.SpawnPrefab, GUILayout.Width(118f));
                }

                if (row.SpawnPrefab)
                {
                    row.PrefabToSpawn = (GameObject)EditorGUILayout.ObjectField("生成 Prefab", row.PrefabToSpawn, typeof(GameObject), false);
                    row.SpawnPoint = (Transform)EditorGUILayout.ObjectField("生成位置", row.SpawnPoint, typeof(Transform), true);
                    if (row.PrefabToSpawn == null)
                    {
                        EditorGUILayout.HelpBox("未指定 Prefab 时，这个动作不会进入 LLM 可选动作列表。", MessageType.Warning);
                    }
                }

                if (row.Movable)
                {
                    row.MoveTarget = (Transform)EditorGUILayout.ObjectField("移动目标", row.MoveTarget, typeof(Transform), true);
                    row.MoveOffset = EditorGUILayout.Vector3Field("移动偏移", row.MoveOffset);
                }

                if (row.PlayAnimation)
                {
                    row.AnimationClip = (AnimationClip)EditorGUILayout.ObjectField("动画片段", row.AnimationClip, typeof(AnimationClip), false);
                    row.JumpHeight = EditorGUILayout.FloatField("默认跳跃高度", Mathf.Max(0.05f, row.JumpHeight));
                    row.JumpDuration = EditorGUILayout.FloatField("默认跳跃时间", Mathf.Max(0.1f, row.JumpDuration));
                }
            }
        }

        private void RefreshRows()
        {
            rows.Clear();
            foreach (Renderer renderer in FindObjectsByType<Renderer>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude))
            {
                if (renderer == null || ShouldIgnore(renderer.gameObject) || renderer.bounds.size.sqrMagnitude < 0.05f)
                {
                    continue;
                }

                GameObject host = PickHost(renderer.gameObject);
                if (host == null || ShouldIgnore(host) || rows.Any(row => row.GameObject == host))
                {
                    continue;
                }

                rows.Add(new Row(host));
            }

            rows.Sort((left, right) => string.Compare(left.GameObject.name, right.GameObject.name, StringComparison.OrdinalIgnoreCase));
            Repaint();
        }

        private void SetVisibleSelection(bool selected)
        {
            foreach (Row row in rows.Where(IsVisible))
            {
                row.Selected = selected;
            }
        }

        private void ApplySelected()
        {
            int count = 0;
            foreach (Row row in rows.Where(row => row.Selected && row.GameObject != null))
            {
                ApplyRow(row);
                count++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"语义物体面板：已标记/更新 {count} 个语义物体。");
            RefreshRows();
        }

        private void RemoveSelected()
        {
            int count = 0;
            foreach (Row row in rows.Where(row => row.Selected && row.GameObject != null))
            {
                CreatureInteractable interactable = row.GameObject.GetComponent<CreatureInteractable>();
                if (interactable != null)
                {
                    Undo.DestroyObjectImmediate(interactable);
                }

                CreatureSemanticTarget target = row.GameObject.GetComponent<CreatureSemanticTarget>();
                if (target != null)
                {
                    Undo.DestroyObjectImmediate(target);
                }

                count++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"语义物体面板：已移除 {count} 个物体的语义/互动标记。");
            RefreshRows();
        }

        private static void ApplyRow(Row row)
        {
            CreatureSemanticTarget target = row.GameObject.GetComponent<CreatureSemanticTarget>();
            if (target == null)
            {
                target = Undo.AddComponent<CreatureSemanticTarget>(row.GameObject);
            }
            else
            {
                Undo.RecordObject(target, "更新语义物体");
            }

            target.targetId = CreatureSemanticTarget.MakeId(row.GameObject.name);
            target.displayName = EmptyFallback(row.DisplayName, row.GameObject.name);
            target.description = EmptyFallback(row.Description, "场景中一个值得数字生物注意的物体。");
            target.semanticTags = EmptyFallback(row.Tags, "object,inspectable");
            target.navigationKind = row.NavigationKind;
            target.dangerLevel = Mathf.Clamp(row.DangerLevel, 0, 5);
            target.interestWeight = Mathf.Max(1, row.InterestWeight);
            target.isAutoGenerated = false;
            EditorUtility.SetDirty(target);

            if (!row.Interactable && !row.Movable && !row.PlayAnimation && !row.SpawnPrefab)
            {
                return;
            }

            CreatureInteractable interactable = row.GameObject.GetComponent<CreatureInteractable>();
            if (interactable == null)
            {
                interactable = Undo.AddComponent<CreatureInteractable>(row.GameObject);
            }
            else
            {
                Undo.RecordObject(interactable, "更新可互动语义物体");
            }

            interactable.interactionId = target.targetId;
            interactable.displayName = target.displayName;
            interactable.description = target.description;
            interactable.tags = target.semanticTags;
            EnsureAction(interactable, "inspect", CreatureInteractionActionType.CustomEvent, null, row.Interactable);
            EnsureAction(interactable, "move_object", CreatureInteractionActionType.MoveObject, action =>
            {
                action.moveTarget = row.MoveTarget;
                action.moveOffset = row.MoveOffset;
            }, row.Movable);
            EnsureAction(interactable, "jump_animation", CreatureInteractionActionType.PlayAnimation, action =>
            {
                action.animationClip = row.AnimationClip;
                action.jumpHeight = Mathf.Max(0.05f, row.JumpHeight);
                action.jumpDuration = Mathf.Max(0.1f, row.JumpDuration);
            }, row.PlayAnimation);
            EnsureAction(interactable, "spawn_prefab", CreatureInteractionActionType.SpawnPrefab, action =>
            {
                action.prefabToSpawn = row.PrefabToSpawn;
                action.spawnPoint = row.SpawnPoint;
            }, row.SpawnPrefab);
            EditorUtility.SetDirty(interactable);
        }

        private static void EnsureAction(
            CreatureInteractable interactable,
            string actionId,
            CreatureInteractionActionType type,
            Action<CreatureInteractionAction> configure,
            bool enabled)
        {
            if (interactable.actions == null)
            {
                interactable.actions = new List<CreatureInteractionAction>();
            }

            CreatureInteractionAction action = interactable.actions.FirstOrDefault(candidate =>
                candidate != null && string.Equals(candidate.actionId, actionId, StringComparison.OrdinalIgnoreCase));
            if (action == null)
            {
                action = new CreatureInteractionAction { actionId = actionId };
                interactable.actions.Add(action);
            }

            action.actionType = type;
            action.enabled = enabled;
            configure?.Invoke(action);
        }

        private bool IsVisible(Row row)
        {
            return string.IsNullOrWhiteSpace(filter) ||
                   row.GameObject.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   row.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   row.Tags.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static GameObject PickHost(GameObject source)
        {
            Transform parent = source.transform.parent;
            if (parent != null &&
                source.name.IndexOf("mesh", StringComparison.OrdinalIgnoreCase) >= 0 &&
                !ShouldIgnore(parent.gameObject))
            {
                return parent.gameObject;
            }

            return source;
        }

        private static bool ShouldIgnore(GameObject gameObject)
        {
            return gameObject == null ||
                   gameObject.GetComponent<Camera>() != null ||
                   gameObject.GetComponent<Light>() != null ||
                   gameObject.GetComponent<CreatureBrain>() != null ||
                   gameObject.GetComponent<CreatureLocationMarker>() != null ||
                   gameObject.GetComponent<CreatureSemanticRegion>() != null ||
                   gameObject.hideFlags != HideFlags.None ||
                   gameObject.name.StartsWith("智能体对话气泡", StringComparison.Ordinal) ||
                   gameObject.name.StartsWith("CreatureLocation_", StringComparison.Ordinal);
        }

        private static string EmptyFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private sealed class Row
        {
            public Row(GameObject gameObject)
            {
                GameObject = gameObject;
                Target = gameObject.GetComponent<CreatureSemanticTarget>();
                InteractableComponent = gameObject.GetComponent<CreatureInteractable>();
                Selected = Target != null;
                DisplayName = Target == null ? gameObject.name : Target.displayName;
                Description = Target == null ? "场景中一个值得数字生物注意的物体。" : Target.description;
                Tags = Target == null ? "object,inspectable" : Target.semanticTags;
                NavigationKind = Target == null ? CreatureNavigationKind.Walkable : Target.navigationKind;
                DangerLevel = Target == null ? 0 : Target.dangerLevel;
                InterestWeight = Target == null ? 1 : Target.interestWeight;
                Interactable = InteractableComponent != null;
                Movable = HasAction(CreatureInteractionActionType.MoveObject);
                PlayAnimation = HasAction(CreatureInteractionActionType.PlayAnimation);
                SpawnPrefab = HasAction(CreatureInteractionActionType.SpawnPrefab);

                CreatureInteractionAction spawn = FindAction(CreatureInteractionActionType.SpawnPrefab);
                PrefabToSpawn = spawn == null ? null : spawn.prefabToSpawn;
                SpawnPoint = spawn == null ? null : spawn.spawnPoint;

                CreatureInteractionAction move = FindAction(CreatureInteractionActionType.MoveObject);
                MoveTarget = move == null ? null : move.moveTarget;
                MoveOffset = move == null ? Vector3.up : move.moveOffset;

                CreatureInteractionAction animation = FindAction(CreatureInteractionActionType.PlayAnimation);
                AnimationClip = animation == null ? null : animation.animationClip;
                JumpHeight = animation == null ? 0.6f : animation.jumpHeight;
                JumpDuration = animation == null ? 0.55f : animation.jumpDuration;
            }

            public GameObject GameObject;
            public CreatureSemanticTarget Target;
            public CreatureInteractable InteractableComponent;
            public bool Selected;
            public string DisplayName;
            public string Description;
            public string Tags;
            public CreatureNavigationKind NavigationKind;
            public int DangerLevel;
            public int InterestWeight;
            public bool Interactable;
            public bool Movable;
            public bool PlayAnimation;
            public bool SpawnPrefab;
            public GameObject PrefabToSpawn;
            public Transform SpawnPoint;
            public Transform MoveTarget;
            public Vector3 MoveOffset;
            public AnimationClip AnimationClip;
            public float JumpHeight;
            public float JumpDuration;

            private bool HasAction(CreatureInteractionActionType type)
            {
                CreatureInteractionAction action = FindAction(type);
                return action != null && action.enabled;
            }

            private CreatureInteractionAction FindAction(CreatureInteractionActionType type)
            {
                return InteractableComponent == null || InteractableComponent.actions == null
                    ? null
                    : InteractableComponent.actions.FirstOrDefault(action => action != null && action.actionType == type);
            }
        }
    }
}
