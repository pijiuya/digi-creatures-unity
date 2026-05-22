using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace DigiCreaturesEditor
{
    public sealed class NavMeshProxyBuilderWindow : EditorWindow
    {
        private const string ProxyRootName = "DigiCreatures_NavProxyRoot";
        private const string ProxyMeshObjectName = "Generated Walkable Proxy";

        private float maxSlopeDegrees = 45f;
        private float minTriangleArea = 0.0005f;
        private float verticalOffset = 0.015f;
        private bool includeInactive;
        private bool configureAndBake = true;

        [MenuItem("数字生物/高级设置/导航/打开 NavMesh 代理生成器")]
        public static void Open()
        {
            GetWindow<NavMeshProxyBuilderWindow>("NavMesh 代理");
        }

        [MenuItem("数字生物/高级设置/导航/从选中对象生成代理并烘焙")]
        public static void BuildProxyFromSelectionAndBake()
        {
            BuildProxyFromSelection(45f, 0.0005f, 0.015f, false, true);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "从选中视觉模型中朝上的三角面生成一份单独的导航代理网格。原始场景模型不会被删除或移动。",
                MessageType.Info);

            maxSlopeDegrees = EditorGUILayout.Slider(new GUIContent("最大可行走坡度", "小于这个坡度的朝上三角面会被纳入导航代理。"), maxSlopeDegrees, 0f, 89f);
            minTriangleArea = EditorGUILayout.FloatField(new GUIContent("最小三角面面积", "过滤过小的碎面，避免生成过于零散的导航代理。"), minTriangleArea);
            verticalOffset = EditorGUILayout.FloatField(new GUIContent("垂直抬升", "把生成的代理网格稍微抬高，避免和原模型完全重叠导致显示或烘焙问题。"), verticalOffset);
            includeInactive = EditorGUILayout.Toggle(new GUIContent("包含未激活子物体", "勾选后，未激活的子 MeshFilter 也会参与代理生成。"), includeInactive);
            configureAndBake = EditorGUILayout.Toggle(new GUIContent("配置并烘焙 Surface", "生成代理后自动配置当前 NavMeshSurface 并开始烘焙。"), configureAndBake);

            using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
            {
                if (GUILayout.Button(new GUIContent("从选中对象生成代理", "选择场景模型根节点或若干可行走模型，然后生成导航代理网格。")))
                {
                    BuildProxyFromSelection(maxSlopeDegrees, minTriangleArea, verticalOffset, includeInactive, configureAndBake);
                }
            }

            if (GUILayout.Button(new GUIContent("配置选中/首个 NavMesh Surface", "把场景中的 NavMeshSurface 设置为使用生成的代理网格进行烘焙。")))
            {
                NavMeshSurface surface = GetTargetSurface();
                if (surface == null)
                {
                    EditorUtility.DisplayDialog("NavMesh 代理", "当前打开的场景中没有找到 NavMesh Surface。", "确定");
                    return;
                }

                ConfigureSurfaceForProxy(surface, true);
            }
        }

        private static void BuildProxyFromSelection(float maxSlopeDegrees, float minTriangleArea, float verticalOffset, bool includeInactive, bool configureAndBake)
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("NavMesh 代理", "请先选择场景视觉模型根节点，或选择需要贡献可行走表面的对象。", "确定");
                return;
            }

            Mesh proxyMesh = BuildWalkableMesh(selectedObjects, maxSlopeDegrees, minTriangleArea, verticalOffset, includeInactive);
            if (proxyMesh.vertexCount == 0)
            {
                EditorUtility.DisplayDialog("NavMesh 代理", "在当前选择中没有找到朝上的可行走三角面。", "确定");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();

            GameObject proxyRoot = FindOrCreateProxyRoot();
            GameObject proxyObject = FindOrCreateChild(proxyRoot.transform, ProxyMeshObjectName);
            Mesh savedMesh = SaveOrUpdateProxyMesh(proxyMesh);

            MeshFilter meshFilter = proxyObject.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = Undo.AddComponent<MeshFilter>(proxyObject);
            }

            meshFilter.sharedMesh = savedMesh;

            MeshCollider meshCollider = proxyObject.GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                meshCollider = Undo.AddComponent<MeshCollider>(proxyObject);
            }

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = savedMesh;

            NavMeshModifier modifier = proxyRoot.GetComponent<NavMeshModifier>();
            if (modifier == null)
            {
                modifier = Undo.AddComponent<NavMeshModifier>(proxyRoot);
            }

            modifier.ignoreFromBuild = false;
            modifier.applyToChildren = true;
            modifier.overrideArea = true;
            modifier.area = NavMesh.GetAreaFromName("Walkable");

            EditorUtility.SetDirty(proxyRoot);
            EditorUtility.SetDirty(proxyObject);
            EditorSceneManager.MarkSceneDirty(proxyRoot.scene);

            if (configureAndBake)
            {
                NavMeshSurface surface = GetTargetSurface();
                if (surface != null)
                {
                    ConfigureSurfaceForProxy(surface, true);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            Selection.activeGameObject = proxyRoot;
            SceneView.RepaintAll();
            Debug.Log($"已从选中对象生成 NavMesh 代理，共 {savedMesh.vertexCount} 个顶点。原始场景模型没有被修改。", proxyRoot);
        }

        private static Mesh BuildWalkableMesh(GameObject[] roots, float maxSlopeDegrees, float minTriangleArea, float verticalOffset, bool includeInactive)
        {
            float minNormalY = Mathf.Cos(maxSlopeDegrees * Mathf.Deg2Rad);
            float minDoubleArea = minTriangleArea * 2f;
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var visited = new HashSet<MeshFilter>();

            foreach (GameObject root in roots)
            {
                foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(includeInactive))
                {
                    if (meshFilter == null || !visited.Add(meshFilter))
                    {
                        continue;
                    }

                    if (meshFilter.transform.root.name == ProxyRootName)
                    {
                        continue;
                    }

                    Mesh mesh = meshFilter.sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    Matrix4x4 localToWorld = meshFilter.transform.localToWorldMatrix;
                    Vector3[] sourceVertices = mesh.vertices;

                    for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                    {
                        int[] triangles = mesh.GetTriangles(subMesh);
                        for (int i = 0; i < triangles.Length; i += 3)
                        {
                            Vector3 a = localToWorld.MultiplyPoint3x4(sourceVertices[triangles[i]]);
                            Vector3 b = localToWorld.MultiplyPoint3x4(sourceVertices[triangles[i + 1]]);
                            Vector3 c = localToWorld.MultiplyPoint3x4(sourceVertices[triangles[i + 2]]);
                            Vector3 cross = Vector3.Cross(b - a, c - a);

                            if (cross.sqrMagnitude <= Mathf.Epsilon || cross.magnitude < minDoubleArea)
                            {
                                continue;
                            }

                            Vector3 normal = cross.normalized;
                            if (normal.y < minNormalY)
                            {
                                continue;
                            }

                            int baseIndex = vertices.Count;
                            Vector3 lift = Vector3.up * verticalOffset;
                            vertices.Add(a + lift);
                            vertices.Add(b + lift);
                            vertices.Add(c + lift);
                            indices.Add(baseIndex);
                            indices.Add(baseIndex + 1);
                            indices.Add(baseIndex + 2);
                        }
                    }
                }
            }

            var proxyMesh = new Mesh
            {
                name = "GeneratedDigiCreaturesWalkableProxy",
                indexFormat = vertices.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };

            proxyMesh.SetVertices(vertices);
            proxyMesh.SetTriangles(indices, 0);
            proxyMesh.RecalculateBounds();
            proxyMesh.RecalculateNormals();
            return proxyMesh;
        }

        private static GameObject FindOrCreateProxyRoot()
        {
            GameObject existing = GameObject.Find(ProxyRootName);
            if (existing != null)
            {
                return existing;
            }

            var root = new GameObject(ProxyRootName);
            Undo.RegisterCreatedObjectUndo(root, "创建 NavMesh 代理根节点");
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            return root;
        }

        private static GameObject FindOrCreateChild(Transform root, string childName)
        {
            Transform existing = root.Find(childName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var child = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(child, "创建 NavMesh 代理子节点");
            child.transform.SetParent(root, false);
            return child;
        }

        private static Mesh SaveOrUpdateProxyMesh(Mesh generatedMesh)
        {
            string scenePath = EditorSceneManager.GetActiveScene().path;
            string directory = string.IsNullOrEmpty(scenePath)
                ? "Assets/GeneratedNavigation"
                : Path.Combine(Path.GetDirectoryName(scenePath), Path.GetFileNameWithoutExtension(scenePath), "Navigation");

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string assetPath = Path.Combine(directory, "GeneratedDigiCreaturesWalkableProxy.asset").Replace("\\", "/");
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generatedMesh, assetPath);
                AssetDatabase.SaveAssets();
                return generatedMesh;
            }

            existing.Clear();
            existing.indexFormat = generatedMesh.indexFormat;
            existing.SetVertices(new List<Vector3>(generatedMesh.vertices));
            existing.SetTriangles(generatedMesh.triangles, 0);
            existing.RecalculateBounds();
            existing.RecalculateNormals();
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            Object.DestroyImmediate(generatedMesh);
            return existing;
        }

        private static NavMeshSurface GetTargetSurface()
        {
            GameObject active = Selection.activeGameObject;
            if (active != null)
            {
                NavMeshSurface selectedSurface = active.GetComponent<NavMeshSurface>();
                if (selectedSurface != null)
                {
                    return selectedSurface;
                }
            }

            return Object.FindAnyObjectByType<NavMeshSurface>();
        }

        private static void ConfigureSurfaceForProxy(NavMeshSurface surface, bool bake)
        {
            Undo.RecordObject(surface, "配置 NavMesh Surface 使用代理");
            surface.collectObjects = CollectObjects.MarkedWithModifier;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.minRegionArea = Mathf.Max(surface.minRegionArea, 2f);
            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);

            if (bake)
            {
                Unity.AI.Navigation.Editor.NavMeshAssetManager.instance.StartBakingSurfaces(new Object[] { surface });
            }
        }
    }
}
