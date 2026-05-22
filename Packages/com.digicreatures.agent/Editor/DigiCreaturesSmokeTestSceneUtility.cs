using System;
using System.Collections.Generic;
using System.IO;
using DigiCreatures;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DigiCreaturesEditor
{
    [InitializeOnLoad]
    public static class DigiCreaturesSmokeTestSceneUtility
    {
        private const string RootFolder = "Assets/DigiCreaturesSmokeTest";
        private const string DataFolder = RootFolder + "/CreatureData/SmokeRobot";
        private const string ScenePath = RootFolder + "/DigiCreatures_MinimalSmoke.unity";
        private const string SettingsPath = RootFolder + "/SmokeLlmSettings.asset";
        private const string ProfilePath = RootFolder + "/SmokeCreatureProfile.asset";
        private const string SpawnPrefabPath = RootFolder + "/SmokeSpawnedSignal.prefab";
        private const string ReportRoot = "Library/DigiCreaturesTestRuns";
        private const string RunningKey = "DigiCreatures.SmokeTest.Running";
        private const string EndTimeKey = "DigiCreatures.SmokeTest.EndTime";
        private const string StartedAtKey = "DigiCreatures.SmokeTest.StartedAt";
        private const string ReportPathKey = "DigiCreatures.SmokeTest.ReportPath";

        static DigiCreaturesSmokeTestSceneUtility()
        {
            EditorApplication.update -= SmokeTestTick;
            EditorApplication.update += SmokeTestTick;
        }

        [MenuItem("数字生物/高级设置/测试/创建极简自检场景")]
        public static void CreateMinimalSmokeTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("DigiCreatures smoke scene creation was cancelled because the current scene was not saved.");
                return;
            }

            CreateOrUpdateAssets(out CreatureLlmSettings settings, out CreatureProfile profile, out GameObject spawnPrefab);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "DigiCreatures_MinimalSmoke";

            CreateGroundAndNavMesh();
            GameObject creature = CreateCreature(settings, profile);
            CreateSemanticSceneObjects(spawnPrefab);
            CreateCameraAndLights(creature.transform);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = creature;
            Debug.Log($"DigiCreatures minimal smoke test scene created: {ScenePath}");
        }

        [MenuItem("数字生物/高级设置/测试/运行 60 秒极简本机测试")]
        public static void RunMinimalSmokeTest()
        {
            CreateMinimalSmokeTestScene();
            string absoluteReportRoot = Path.GetFullPath(ReportRoot);
            Directory.CreateDirectory(absoluteReportRoot);
            string reportPath = Path.Combine(absoluteReportRoot, $"minimal-smoke-{DateTime.Now:yyyyMMdd-HHmmss}.md");
            double startedAt = EditorApplication.timeSinceStartup;
            SessionState.SetBool(RunningKey, true);
            SessionState.SetFloat(StartedAtKey, (float)startedAt);
            SessionState.SetFloat(EndTimeKey, (float)(startedAt + 60.0));
            SessionState.SetString(ReportPathKey, reportPath);
            EditorApplication.isPlaying = true;
            Debug.Log("DigiCreatures minimal smoke test started for 60 seconds.");
        }

        private static void SmokeTestTick()
        {
            if (!SessionState.GetBool(RunningKey, false))
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                return;
            }

            double endAt = SessionState.GetFloat(EndTimeKey, 0f);
            if (EditorApplication.timeSinceStartup < endAt)
            {
                return;
            }

            string reportPath = SessionState.GetString(ReportPathKey, string.Empty);
            WriteSmokeReport(reportPath);
            SessionState.SetBool(RunningKey, false);
            EditorApplication.isPlaying = false;
        }

        private static void CreateOrUpdateAssets(
            out CreatureLlmSettings settings,
            out CreatureProfile profile,
            out GameObject spawnPrefab)
        {
            EnsureFolder("Assets", "DigiCreaturesSmokeTest");
            EnsureFolder(RootFolder, "CreatureData");
            EnsureFolder(RootFolder + "/CreatureData", "SmokeRobot");

            WriteTextIfChanged(
                DataFolder + "/soul.md",
                "---\n" +
                "displayName: 极简烟测宇航员\n" +
                "subtitleName: 极简烟测宇航员\n" +
                "---\n\n" +
                "# Soul\n\n" +
                "你是一个外星来的宇航员机器人。你喜欢眺望星空、观察场景中的碑体和信标，寻找散落的星图碎片。你的语言温暖、好奇、有一点诗意。\n");
            WriteTextIfChanged(
                DataFolder + "/summary.md",
                "# Memory Summary\n\n这个极简测试场景用于验证导航、字幕、语义目标和互动动作能在空项目里跑通。\n");
            WriteTextIfChanged(
                DataFolder + "/config.json",
                "{\n" +
                "  \"backend\": \"local\",\n" +
                "  \"localEndpoint\": \"http://localhost:11434/v1/chat/completions\",\n" +
                "  \"onlineEndpoint\": \"https://api.openai.com/v1/chat/completions\",\n" +
                "  \"localModel\": \"qwen2.5:3b\",\n" +
                "  \"onlineModel\": \"gpt-4.1-mini\",\n" +
                "  \"onlineApiKeyEnvironmentVariable\": \"OPENAI_API_KEY\",\n" +
                "  \"temperature\": 0.7,\n" +
                "  \"decisionIntervalSeconds\": 1.5,\n" +
                "  \"requestTimeoutSeconds\": 30,\n" +
                "  \"recentMemoryLimit\": 6,\n" +
                "  \"summaryEveryEvents\": 8\n" +
                "}\n");
            WriteTextIfChanged(DataFolder + "/memory.jsonl", string.Empty);
            WriteTextIfChanged(DataFolder + "/test-memory.jsonl", string.Empty);
            WriteTextIfChanged(DataFolder + "/test-summary.md", "# Memory Summary\n\nSmoke test memory starts empty.\n");
            AssetDatabase.ImportAsset(DataFolder + "/soul.md");
            AssetDatabase.ImportAsset(DataFolder + "/summary.md");
            AssetDatabase.ImportAsset(DataFolder + "/config.json");
            AssetDatabase.ImportAsset(DataFolder + "/memory.jsonl");
            AssetDatabase.ImportAsset(DataFolder + "/test-memory.jsonl");
            AssetDatabase.ImportAsset(DataFolder + "/test-summary.md");

            settings = AssetDatabase.LoadAssetAtPath<CreatureLlmSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<CreatureLlmSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            settings.backend = "ollama";
            settings.localEndpoint = "http://localhost:11434/v1/chat/completions";
            settings.localModel = "qwen2.5:3b";
            settings.remoteEndpoint = "https://api.openai.com/v1/chat/completions";
            settings.remoteModel = "gpt-4.1-mini";
            settings.remoteApiKeyEnvironmentVariable = "OPENAI_API_KEY";
            settings.temperature = 0.7f;
            settings.requestTimeoutSeconds = 30;
            EditorUtility.SetDirty(settings);

            profile = AssetDatabase.LoadAssetAtPath<CreatureProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<CreatureProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            profile.creatureId = "smoke_robot";
            profile.displayName = "极简烟测宇航员";
            profile.subtitleName = "极简烟测宇航员";
            profile.dataFolderName = "SmokeRobot";
            profile.llmSettings = settings;
            profile.defaultSoul = AssetDatabase.LoadAssetAtPath<TextAsset>(DataFolder + "/soul.md");
            profile.defaultSummary = AssetDatabase.LoadAssetAtPath<TextAsset>(DataFolder + "/summary.md");
            EditorUtility.SetDirty(profile);

            spawnPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpawnPrefabPath);
            if (spawnPrefab == null)
            {
                GameObject signal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                signal.name = "Smoke Spawned Signal";
                signal.transform.localScale = Vector3.one * 0.35f;
                ApplyMaterial(signal, "SmokeSignalBlue", new Color(0.25f, 0.7f, 1f));
                spawnPrefab = PrefabUtility.SaveAsPrefabAsset(signal, SpawnPrefabPath);
                UnityEngine.Object.DestroyImmediate(signal);
            }

            AssetDatabase.SaveAssets();
        }

        private static void CreateGroundAndNavMesh()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Smoke Walkable Plane";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);
            ApplyMaterial(ground, "SmokeGround", new Color(0.72f, 0.74f, 0.76f));
            GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.NavigationStatic);

            GameObject navRoot = new GameObject("DigiCreatures_Smoke_NavMeshSurface");
            NavMeshSurface surface = navRoot.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.defaultArea = 0;
            surface.BuildNavMesh();
        }

        private static GameObject CreateCreature(CreatureLlmSettings settings, CreatureProfile profile)
        {
            Vector3 start = new Vector3(-4f, 0f, -4f);
            if (NavMesh.SamplePosition(start, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                start = hit.position;
            }

            GameObject root = new GameObject("Smoke Astronaut Robot");
            root.transform.position = start;
            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.8f;
            agent.speed = 1.8f;
            agent.angularSpeed = 480f;
            agent.acceleration = 10f;
            agent.stoppingDistance = 0.35f;

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Smoke Robot Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.up * 0.9f;
            visual.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            ApplyMaterial(visual, "SmokeRobotWhite", new Color(0.92f, 0.95f, 1f));

            CreatureMotor motor = root.AddComponent<CreatureMotor>();
            CreatureIdleMotion idle = root.AddComponent<CreatureIdleMotion>();
            CreatureBrain brain = root.AddComponent<CreatureBrain>();
            brain.Profile = profile;
            brain.LlmSettings = settings;
            brain.CreatureDataPath = DataFolder;
            brain.StartOnAwake = true;
            brain.AgentMode = CreatureAgentMode.Move;
            brain.UseTestMemory = true;
            brain.TestMemoryFileName = "test-memory.jsonl";
            brain.RecordLlmMetrics = true;

            SerializedObject serializedBrain = new SerializedObject(brain);
            SetPrivateFloat(serializedBrain, "decisionIntervalSeconds", 1.5f);
            SetPrivateFloat(serializedBrain, "maximumDwellSeconds", 2.5f);
            SetPrivateFloat(serializedBrain, "minimumSubtitleVisibleSeconds", 6f);
            SetPrivateFloat(serializedBrain, "subtitleExtraVisibleSeconds", 2f);
            SetPrivateFloat(serializedBrain, "interactionScanRadius", 6f);
            SetPrivateBool(serializedBrain, "localReactionsWhileThinking", true);
            serializedBrain.ApplyModifiedPropertiesWithoutUndo();

            _ = motor;
            _ = idle;
            return root;
        }

        private static void CreateSemanticSceneObjects(GameObject spawnPrefab)
        {
            CreateInteractableTarget(
                "Smoke Star Obelisk",
                "star_obelisk",
                "星图方碑",
                "一块像星图校准器的蓝色方碑，适合触发默认跳一下动画。",
                new Vector3(3.6f, 0.55f, 2.1f),
                PrimitiveType.Cube,
                new Color(0.2f, 0.45f, 0.95f),
                new[]
                {
                    new CreatureInteractionAction
                    {
                        actionId = "jump_animation",
                        actionType = CreatureInteractionActionType.PlayAnimation,
                        jumpHeight = 0.8f,
                        jumpDuration = 0.7f
                    }
                });

            CreateInteractableTarget(
                "Smoke Supply Orb",
                "supply_orb",
                "漂浮补给球",
                "一颗可以被轻轻推开的绿色补给球，用来验证 move_object。",
                new Vector3(-2.5f, 0.55f, 3.8f),
                PrimitiveType.Sphere,
                new Color(0.25f, 0.78f, 0.36f),
                new[]
                {
                    new CreatureInteractionAction
                    {
                        actionId = "move_object",
                        actionType = CreatureInteractionActionType.MoveObject,
                        moveOffset = new Vector3(0f, 0.45f, 0.65f)
                    }
                });

            CreateInteractableTarget(
                "Smoke Signal Emitter",
                "signal_emitter",
                "信标发生器",
                "一个橙色信标，可生成小型星光 prefab。",
                new Vector3(4.4f, 0.55f, -3.1f),
                PrimitiveType.Cylinder,
                new Color(1f, 0.58f, 0.18f),
                new[]
                {
                    new CreatureInteractionAction
                    {
                        actionId = "spawn_prefab",
                        actionType = CreatureInteractionActionType.SpawnPrefab,
                        prefabToSpawn = spawnPrefab
                    }
                });

            GameObject regionObject = new GameObject("Smoke Observation Region");
            regionObject.transform.position = Vector3.zero;
            CreatureSemanticRegion region = regionObject.AddComponent<CreatureSemanticRegion>();
            region.regionId = "observation_plaza";
            region.displayName = "开阔观测区";
            region.description = "一个用于自由漫游、转向天空和重新采样动态 NavMesh 目标点的开阔区域。";
            region.tags = "plaza,stars,free_roam";
            region.priority = 3;
            region.shape = CreatureRegionShape.Box;
            region.boxSize = new Vector3(12f, 2f, 12f);

            CreateMarker("center_anchor", "中心备用点", Vector3.zero, "fallback,viewpoint");
            CreateMarker("north_view", "北侧观测点", new Vector3(0f, 0f, 6f), "fallback,viewpoint");
        }

        private static void CreateInteractableTarget(
            string objectName,
            string id,
            string displayName,
            string description,
            Vector3 position,
            PrimitiveType primitiveType,
            Color color,
            IEnumerable<CreatureInteractionAction> actions)
        {
            GameObject targetObject = GameObject.CreatePrimitive(primitiveType);
            targetObject.name = objectName;
            targetObject.transform.position = position;
            targetObject.transform.localScale = primitiveType == PrimitiveType.Cylinder
                ? new Vector3(0.9f, 0.55f, 0.9f)
                : Vector3.one * 1.1f;
            ApplyMaterial(targetObject, objectName.Replace(" ", string.Empty) + "Material", color);

            CreatureSemanticTarget target = targetObject.AddComponent<CreatureSemanticTarget>();
            target.targetId = id;
            target.displayName = displayName;
            target.description = description;
            target.semanticTags = "object,inspectable,smoke_test";
            target.navigationKind = CreatureNavigationKind.Walkable;
            target.interestWeight = 3;
            target.approachRadius = 2.2f;
            target.isAutoGenerated = false;

            CreatureInteractable interactable = targetObject.AddComponent<CreatureInteractable>();
            interactable.interactionId = id;
            interactable.displayName = displayName;
            interactable.description = description;
            interactable.tags = "smoke_test,usable";
            interactable.interactionRadius = 2.6f;
            interactable.actions = new List<CreatureInteractionAction>
            {
                new CreatureInteractionAction
                {
                    actionId = "inspect",
                    actionType = CreatureInteractionActionType.CustomEvent
                }
            };
            interactable.actions.AddRange(actions);
        }

        private static void CreateMarker(string id, string displayName, Vector3 position, string tags)
        {
            GameObject markerObject = new GameObject("Marker_" + id);
            markerObject.transform.position = position;
            CreatureLocationMarker marker = markerObject.AddComponent<CreatureLocationMarker>();
            marker.id = id;
            marker.displayName = displayName;
            marker.description = "极简测试场景中的备用固定点。";
            marker.tags = tags;
            marker.priority = 1;
            marker.navigationKind = CreatureNavigationKind.Walkable;
        }

        private static void CreateCameraAndLights(Transform target)
        {
            GameObject cameraObject = new GameObject("Smoke Fixed Camera");
            cameraObject.transform.position = new Vector3(0f, 7.2f, -9.2f);
            cameraObject.transform.rotation = Quaternion.Euler(52f, 0f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 48f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();
            CreatureCameraRig rig = cameraObject.AddComponent<CreatureCameraRig>();
            rig.Target = target;

            GameObject lightObject = new GameObject("Smoke Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;

            GameObject fillObject = new GameObject("Smoke Soft Fill Light");
            fillObject.transform.position = new Vector3(-4f, 5f, -4f);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.intensity = 1.4f;
            fill.range = 12f;
        }

        private static void WriteSmokeReport(string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            CreatureBrain brain = UnityEngine.Object.FindAnyObjectByType<CreatureBrain>(FindObjectsInactive.Exclude);
            NavMeshAgent agent = brain == null ? null : brain.GetComponent<NavMeshAgent>();
            Vector3 position = brain == null ? Vector3.zero : brain.transform.position;
            float moved = brain == null ? 0f : Vector3.Distance(position, new Vector3(-4f, 0f, -4f));
            string memoryPath = Path.GetFullPath(Path.Combine(DataFolder, "test-memory.jsonl"));
            string[] memories = File.Exists(memoryPath) ? File.ReadAllLines(memoryPath) : Array.Empty<string>();
            int interactionCount = 0;
            int decisionCount = 0;
            int llmSuccessCount = 0;
            int llmFailureCount = 0;
            foreach (string line in memories)
            {
                if (line.Contains("\"type\":\"interaction", StringComparison.Ordinal))
                {
                    interactionCount++;
                }
                if (line.Contains("\"type\":\"decision", StringComparison.Ordinal))
                {
                    decisionCount++;
                }
                if (line.Contains("\"type\":\"llm_metrics\"", StringComparison.Ordinal))
                {
                    if (line.Contains("success=True", StringComparison.OrdinalIgnoreCase))
                    {
                        llmSuccessCount++;
                    }
                    else if (line.Contains("success=False", StringComparison.OrdinalIgnoreCase))
                    {
                        llmFailureCount++;
                    }
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(
                reportPath,
                "# DigiCreatures Minimal Smoke Test\n\n" +
                $"- Scene: {SceneManager.GetActiveScene().path}\n" +
                $"- Duration: {(EditorApplication.timeSinceStartup - SessionState.GetFloat(StartedAtKey, 0f)):0.0}s\n" +
                $"- Agent found: {brain != null}\n" +
                $"- Agent on NavMesh: {agent != null && agent.isOnNavMesh}\n" +
                $"- Moved from start: {moved:0.00}m\n" +
                $"- Last status: {Safe(brain == null ? string.Empty : brain.LastStatus)}\n" +
                $"- Last dialogue: {Safe(brain == null ? string.Empty : brain.LastDialogue)}\n" +
                $"- Last intent: {Safe(brain == null ? string.Empty : brain.CurrentIntent)}\n" +
                $"- Last destination: {Safe(brain == null ? string.Empty : brain.LastDestinationId)}\n" +
                $"- Last target: {Safe(brain == null ? string.Empty : brain.LastTargetId)}\n" +
                $"- Last region: {Safe(brain == null ? string.Empty : brain.LastRegionId)}\n" +
                $"- Last LLM error: {Safe(brain == null ? string.Empty : brain.LastDecisionError)}\n" +
                $"- Memory events: {memories.Length}\n" +
                $"- Decisions: {decisionCount}\n" +
                $"- LLM successes: {llmSuccessCount}\n" +
                $"- LLM failures: {llmFailureCount}\n" +
                $"- Interactions: {interactionCount}\n" +
                $"- Memory file: {memoryPath}\n");
            Debug.Log($"DigiCreatures minimal smoke test report written: {reportPath}");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static void WriteTextIfChanged(string assetPath, string text)
        {
            string absolutePath = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            if (File.Exists(absolutePath) && File.ReadAllText(absolutePath) == text)
            {
                return;
            }

            File.WriteAllText(absolutePath, text);
        }

        private static void ApplyMaterial(GameObject target, string materialName, Color color)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            string materialPath = RootFolder + "/" + materialName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            renderer.sharedMaterial = material;
        }

        private static void SetPrivateFloat(SerializedObject serialized, string propertyName, float value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetPrivateBool(SerializedObject serialized, string propertyName, bool value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "（空）" : value.Replace("\n", " ");
        }
    }
}
