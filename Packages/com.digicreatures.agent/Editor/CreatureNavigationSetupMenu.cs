using DigiCreatures;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace DigiCreaturesEditor
{
    public static class CreatureNavigationSetupMenu
    {
        [MenuItem("数字生物/高级设置/导航/在当前场景安装运行时导航")]
        public static void InstallRuntimeNavigation()
        {
            CreatureSceneBootstrapper bootstrapper = CreatureObjectFinder.FindAnyObjectByType<CreatureSceneBootstrapper>(false);
            GameObject host;

            if (bootstrapper == null)
            {
                host = new GameObject("智能体运行时启动器");
                bootstrapper = host.AddComponent<CreatureSceneBootstrapper>();
            }
            else
            {
                host = bootstrapper.gameObject;
            }

            SerializedObject serialized = new SerializedObject(bootstrapper);
            serialized.FindProperty("createDemoWorld").boolValue = false;
            serialized.FindProperty("spawnCreature").boolValue = true;
            serialized.FindProperty("playerRobotPrefabPath").stringValue = "Assets/Prefabs/PlayerRobot.prefab";
            serialized.FindProperty("creatureSpawnPosition").vector3Value = Vector3.zero;
            serialized.FindProperty("navMeshCenter").vector3Value = Vector3.zero;
            serialized.FindProperty("navMeshSize").vector3Value = new Vector3(120f, 40f, 120f);
            serialized.FindProperty("collectGeometry").enumValueIndex = (int)NavMeshCollectGeometry.PhysicsColliders;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EnsureDefaultLocation("origin", "原点", "spawn,neutral", 2, Vector3.zero,
                "场景中的中性锚点。");
            EnsureDefaultLocation("north", "北侧探索点", "explore", 1, new Vector3(0f, 0f, 8f),
                "北侧的探索位置。");
            EnsureDefaultLocation("east", "东侧探索点", "explore", 1, new Vector3(8f, 0f, 0f),
                "东侧的探索位置。");

            Selection.activeObject = host;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("已安装智能体运行时启动器。进入播放模式前，请调整 Nav Mesh 中心/尺寸，并移动 CreatureLocation 标记到合适位置。");
        }

        [MenuItem("数字生物/高级设置/导航/将选中对象烘焙为简单 NavMesh")]
        public static void BakeSelectedObjectAsSimpleNavMesh()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("烘焙简单 NavMesh", "请先选择一个 Plane 或其他可行走网格。", "确定");
                return;
            }

            NavMeshSurface surface = selected.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                Undo.AddComponent<NavMeshSurface>(selected);
                surface = selected.GetComponent<NavMeshSurface>();
            }
            else
            {
                Undo.RecordObject(surface, "配置简单 NavMesh Surface");
            }

            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.layerMask = ~0;
            surface.defaultArea = NavMesh.GetAreaFromName("Walkable");
            surface.minRegionArea = 0f;
            surface.overrideVoxelSize = false;
            surface.overrideTileSize = false;
            surface.buildHeightMesh = false;

            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(selected.scene);
            Unity.AI.Navigation.Editor.NavMeshAssetManager.instance.StartBakingSurfaces(new Object[] { surface });
            SceneView.RepaintAll();

            Debug.Log($"正在直接从选中对象“{selected.name}”烘焙简单 NavMesh。该流程使用当前对象层级和渲染网格。", selected);
        }

        private static void EnsureDefaultLocation(string id, string displayName, string tags, int priority, Vector3 position, string description)
        {
            foreach (CreatureLocationMarker marker in CreatureObjectFinder.FindObjectsByType<CreatureLocationMarker>(false))
            {
                if (marker.id == id)
                {
                    return;
                }
            }

            GameObject markerObject = new GameObject("CreatureLocation_" + id);
            markerObject.transform.position = position;
            CreatureLocationMarker newMarker = markerObject.AddComponent<CreatureLocationMarker>();
            newMarker.id = id;
            newMarker.displayName = displayName;
            newMarker.tags = tags;
            newMarker.priority = priority;
            newMarker.description = description;
        }
    }
}
