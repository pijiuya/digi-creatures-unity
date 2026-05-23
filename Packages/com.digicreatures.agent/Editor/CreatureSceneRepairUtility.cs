using System.Collections.Generic;
using System.Linq;
using DigiCreatures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DigiCreaturesEditor
{
    public static class CreatureSceneRepairUtility
    {
        private const string DemoCreaturePrefabPath = "Assets/Prefabs/PlayerRobot.prefab";
        private const string DefaultAgentName = "数字生物智能体";

        [MenuItem("数字生物/高级设置/危险操作/清除场景中的智能体组件")]
        public static void ClearSceneCreatureAgents()
        {
            List<string> report = new List<string>();
            HashSet<GameObject> owners = new HashSet<GameObject>();
            int cleanedObjects = 0;
            int removedComponents = 0;

            foreach (CreatureBrain brain in CreatureObjectFinder.FindObjectsByType<CreatureBrain>(true))
            {
                AddOwner(owners, brain);
            }

            foreach (CreatureMotor motor in CreatureObjectFinder.FindObjectsByType<CreatureMotor>(true))
            {
                AddOwner(owners, motor);
            }

            foreach (CreatureSpeechBubble bubble in CreatureObjectFinder.FindObjectsByType<CreatureSpeechBubble>(true))
            {
                AddOwner(owners, bubble);
            }

            foreach (CreatureIdleMotion idleMotion in CreatureObjectFinder.FindObjectsByType<CreatureIdleMotion>(true))
            {
                AddOwner(owners, idleMotion);
            }

            foreach (NavMeshAgent navMeshAgent in CreatureObjectFinder.FindObjectsByType<NavMeshAgent>(true))
            {
                AddOwner(owners, navMeshAgent);
            }

            foreach (GameObject owner in owners)
            {
                removedComponents += RemoveComponentIfExists<CreatureBrain>(owner);
                removedComponents += RemoveComponentIfExists<CreatureMotor>(owner);
                removedComponents += RemoveComponentIfExists<NavMeshAgent>(owner);
                removedComponents += RemoveComponentIfExists<CreatureSpeechBubble>(owner);
                removedComponents += RemoveComponentIfExists<CreatureIdleMotion>(owner);
                cleanedObjects++;
                report.Add($"已从“{owner.name}”移除智能体大脑、移动器、待机动作、旧对话气泡和 NavMeshAgent。");
            }

            foreach (CreatureAgentDebugger debugger in CreatureObjectFinder.FindObjectsByType<CreatureAgentDebugger>(true))
            {
                if (debugger == null || debugger.targetAgent == null)
                {
                    continue;
                }

                Undo.RecordObject(debugger, "清空智能体调试器目标");
                debugger.targetAgent = null;
                EditorUtility.SetDirty(debugger);
                report.Add($"已清空调试器“{debugger.name}”的目标智能体。");
            }

            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }

            if (cleanedObjects == 0)
            {
                report.Add("场景中没有找到智能体运行组件。地图语义目标、移动点和可互动标注已保留。");
            }
            else
            {
                report.Insert(0, $"已清除 {cleanedObjects} 个误挂智能体对象，共移除 {removedComponents} 个组件。地图语义目标、移动点和可互动标注已保留。");
            }

            string message = string.Join("\n", report);
            Debug.Log("清除场景智能体完成：\n" + message);
            EditorUtility.DisplayDialog("清除场景智能体", message, "确定");
        }

        [MenuItem("数字生物/高级设置/场景验证与修复")]
        public static void ValidateAndRepairCurrentScene()
        {
            List<string> report = new List<string>();
            CreatureLlmSettings settings = CreatureAgentConsoleWindow.LoadOrCreateSettings();
            CreatureBrain brain = ResolveTargetBrain(report);
            EnsureAgentComponents(brain.gameObject, brain, settings, report);
            EnsureSingleAutonomousBrain(brain, report);
            EnsureAgentOnNavMesh(brain.transform, report);
            EnsureLocationMarkers(brain.transform.position, report);
            string semanticReport = CreatureSemanticSceneUtility.ScanAndGenerateTargets(false);
            report.Add("语义扫描：" + FirstLine(semanticReport));
            EnsureSceneInteractables(report);
            EnsureDebugger(brain, report);
            EnsureMainCameraFramesAgent(brain.transform, report);
            DisableRuntimeCreatureSpawn(report);

            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }

            string message = string.Join("\n", report);
            Debug.Log("场景验证与修复完成：\n" + message, brain);
            EditorUtility.DisplayDialog("场景验证与修复", message, "确定");
        }

        [MenuItem("数字生物/确保数字生物在场景中")]
        [MenuItem("数字生命/确保数字生命在场景中")]
        public static void EnsureCreatureAgentInScene()
        {
            List<string> report = new List<string>();
            CreatureLlmSettings settings = CreatureAgentConsoleWindow.LoadOrCreateSettings();
            CreatureBrain brain = CreateOrFindCreatureAgent(report);
            EnsureAgentComponents(brain.gameObject, brain, settings, report);
            EnsureSingleAutonomousBrain(brain, report);
            EnsureAgentOnNavMesh(brain.transform, report);
            EnsureLocationMarkers(brain.transform.position, report);
            EnsureSceneInteractables(report);
            EnsureDebugger(brain, report);
            EnsureMainCameraFramesAgent(brain.transform, report);
            DisableRuntimeCreatureSpawn(report);

            Selection.activeGameObject = brain.gameObject;
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }

            string message = string.Join("\n", report);
            Debug.Log("数字生物智能体场景安装完成：\n" + message, brain);
            EditorUtility.DisplayDialog("数字生物智能体", message, "确定");
        }

        [MenuItem("数字生物/DigiPlace Demo/安装 Timmy 示例")]
        public static void EnsureTimmyDemoAgentInScene()
        {
            EnsureCreatureAgentInScene();
        }

        private static CreatureBrain ResolveTargetBrain(List<string> report)
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null && EditorUtility.IsPersistent(selected))
            {
                selected = null;
            }

            CreatureBrain brain = selected == null ? null : selected.GetComponent<CreatureBrain>();
            if (brain != null)
            {
                report.Add($"使用当前选中的智能体：{brain.name}");
                return brain;
            }

            if (selected != null)
            {
                if (IsLikelyAgentHost(selected))
                {
                    report.Add($"当前选中对象“{selected.name}”将被修复为智能体。");
                    return AddOrGet<CreatureBrain>(selected, report, "CreatureBrain");
                }

                report.Add($"当前选中对象“{selected.name}”像地图/场景网格，已跳过，避免把地图误设为智能体。");
            }

            brain = CreatureObjectFinder.FindAnyObjectByType<CreatureBrain>(true);
            if (brain != null)
            {
                report.Add($"未选中对象，使用场景中已有智能体：{brain.name}");
                return brain;
            }

            report.Add("场景中没有智能体，改为创建一个数字生物智能体。");
            return CreateOrFindCreatureAgent(report);
        }

        private static CreatureBrain CreateOrFindCreatureAgent(List<string> report)
        {
            GameObject agentRoot = FindExistingCreatureRoot();
            if (agentRoot == null)
            {
                agentRoot = InstantiateDemoCreaturePrefab();
                if (agentRoot == null)
                {
                    agentRoot = new GameObject(DefaultAgentName);
                    Undo.RegisterCreatedObjectUndo(agentRoot, "创建数字生物智能体");
                    report.Add("没有找到 demo prefab，已创建空的数字生物智能体容器。");
                }
                else
                {
                    report.Add("已把 demo prefab 实例化到当前场景，作为数字生物智能体载体。");
                }
            }
            else
            {
                report.Add($"已找到场景中的数字生物智能体：{agentRoot.name}。");
            }

            agentRoot.name = DefaultAgentName;
            PlaceAtNavMeshStart(agentRoot.transform, report);
            DisablePlayerControlAndEmbeddedCameras(agentRoot, report);
            return AddOrGet<CreatureBrain>(agentRoot, report, "CreatureBrain");
        }

        private static GameObject FindExistingCreatureRoot()
        {
            foreach (CreatureBrain brain in CreatureObjectFinder.FindObjectsByType<CreatureBrain>(true))
            {
                if (brain != null && IsCreatureLike(brain.gameObject))
                {
                    return brain.gameObject;
                }
            }

            foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (IsCreatureLike(root))
                {
                    return root;
                }

                Transform robot = root.transform.Find("Robot");
                if (robot != null && IsCreatureLike(robot.gameObject))
                {
                    return root;
                }
            }

            return null;
        }

        private static bool IsCreatureLike(GameObject candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            string lowerName = candidate.name.ToLowerInvariant();
            if (lowerName.Contains("agent") ||
                lowerName.Contains("creature") ||
                lowerName.Contains("数字生物") ||
                lowerName.Contains("智能体") ||
                lowerName.Contains("timmy") ||
                lowerName.Contains("playerrobot") ||
                lowerName.Contains("timmy robot"))
            {
                return true;
            }

            return PrefabUtility.GetCorrespondingObjectFromSource(candidate) != null &&
                   string.Equals(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(candidate)), DemoCreaturePrefabPath, System.StringComparison.OrdinalIgnoreCase);
        }

        private static GameObject InstantiateDemoCreaturePrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemoCreaturePrefabPath);
            if (prefab == null)
            {
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, EditorSceneManager.GetActiveScene()) as GameObject;
            if (instance != null)
            {
                Undo.RegisterCreatedObjectUndo(instance, "创建数字生物智能体");
                instance.name = DefaultAgentName;
            }

            return instance;
        }

        private static void PlaceAtNavMeshStart(Transform agent, List<string> report)
        {
            Vector3 preferred = Vector3.zero;
            CreatureLocationMarker origin = CreatureObjectFinder.FindObjectsByType<CreatureLocationMarker>(true)
                .FirstOrDefault(marker => marker != null && string.Equals(marker.id, "origin", System.StringComparison.OrdinalIgnoreCase));
            if (origin != null)
            {
                preferred = origin.transform.position;
            }

            if (NavMesh.SamplePosition(preferred, out NavMeshHit hit, 20f, NavMesh.AllAreas))
            {
                Undo.RecordObject(agent, "放置数字生物到 NavMesh");
                agent.position = hit.position;
                agent.rotation = Quaternion.identity;
                EditorUtility.SetDirty(agent);
                report.Add("已把数字生物放到最近的 NavMesh 可行走位置。");
                return;
            }

            Undo.RecordObject(agent, "放置数字生物到场景原点");
            agent.position = preferred;
            agent.rotation = Quaternion.identity;
            EditorUtility.SetDirty(agent);
            report.Add("暂时没有采样到 NavMesh，已把数字生物放到默认起点；请确认场景 NavMesh 已烘焙。");
        }

        private static void DisablePlayerControlAndEmbeddedCameras(GameObject root, List<string> report)
        {
            int disabledControls = 0;
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || !IsKnownPlayerControlComponent(behaviour) || !behaviour.enabled)
                {
                    continue;
                }

                Undo.RecordObject(behaviour, "关闭数字生物玩家控制组件");
                behaviour.enabled = false;
                EditorUtility.SetDirty(behaviour);
                disabledControls++;
            }

#if ENABLE_INPUT_SYSTEM
            foreach (PlayerInput playerInput in root.GetComponentsInChildren<PlayerInput>(true))
            {
                if (!playerInput.enabled)
                {
                    continue;
                }

                Undo.RecordObject(playerInput, "关闭数字生物 PlayerInput");
                playerInput.enabled = false;
                EditorUtility.SetDirty(playerInput);
                disabledControls++;
            }
#endif

            foreach (CharacterController characterController in root.GetComponentsInChildren<CharacterController>(true))
            {
                if (!characterController.enabled)
                {
                    continue;
                }

                Undo.RecordObject(characterController, "关闭数字生物 CharacterController");
                characterController.enabled = false;
                EditorUtility.SetDirty(characterController);
                disabledControls++;
            }

            int disabledViewComponents = 0;
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (!camera.enabled)
                {
                    continue;
                }

                Undo.RecordObject(camera, "关闭数字生物内置摄像机");
                camera.enabled = false;
                EditorUtility.SetDirty(camera);
                disabledViewComponents++;
            }

            foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>(true))
            {
                if (!listener.enabled)
                {
                    continue;
                }

                Undo.RecordObject(listener, "关闭数字生物内置监听器");
                listener.enabled = false;
                EditorUtility.SetDirty(listener);
                disabledViewComponents++;
            }

            if (disabledControls > 0)
            {
                report.Add($"已关闭智能体 prefab 自带的 {disabledControls} 个玩家输入/角色控制组件，改由 NavMeshAgent 驱动。");
            }

            if (disabledViewComponents > 0)
            {
                report.Add($"已关闭智能体 prefab 自带的 {disabledViewComponents} 个摄像机/监听器，避免抢占场景主摄像机和 UI 显示。");
            }
        }

        private static bool IsKnownPlayerControlComponent(MonoBehaviour behaviour)
        {
            string fullName = behaviour.GetType().FullName ?? string.Empty;
            return fullName == "StarterAssets.ThirdPersonController" ||
                   fullName == "StarterAssets.StarterAssetsInputs" ||
                   fullName.EndsWith(".ThirdPersonController", System.StringComparison.Ordinal) ||
                   fullName.EndsWith(".StarterAssetsInputs", System.StringComparison.Ordinal);
        }

        private static void DisableRuntimeCreatureSpawn(List<string> report)
        {
            CreatureSceneBootstrapper bootstrapper = CreatureObjectFinder.FindAnyObjectByType<CreatureSceneBootstrapper>(true);
            if (bootstrapper == null)
            {
                return;
            }

            SerializedObject serialized = new SerializedObject(bootstrapper);
            SerializedProperty spawnCreature = serialized.FindProperty("spawnCreature");
            SerializedProperty playerRobotPrefabPath = serialized.FindProperty("playerRobotPrefabPath");
            bool changed = false;
            if (spawnCreature != null && spawnCreature.boolValue)
            {
                spawnCreature.boolValue = false;
                changed = true;
            }

            if (playerRobotPrefabPath != null && playerRobotPrefabPath.stringValue != DemoCreaturePrefabPath)
            {
                playerRobotPrefabPath.stringValue = DemoCreaturePrefabPath;
                changed = true;
            }

            if (changed)
            {
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(bootstrapper);
                report.Add("已关闭运行时临时生成智能体，避免播放时重复生成数字生物。");
            }
        }

        private static void EnsureMainCameraFramesAgent(Transform agent, List<string> report)
        {
            if (agent == null)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = CreatureObjectFinder.FindAnyObjectByType<Camera>(true);
            }

            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                Undo.RegisterCreatedObjectUndo(cameraObject, "创建主摄像机");
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                cameraObject.tag = "MainCamera";
                report.Add("已创建主摄像机，用于观察数字生物。");
            }

            Undo.RecordObject(camera, "设置主摄像机观察数字生物");
            Undo.RecordObject(camera.transform, "设置主摄像机观察数字生物");
            camera.enabled = true;
            camera.fieldOfView = Mathf.Clamp(camera.fieldOfView <= 0f ? 55f : camera.fieldOfView, 45f, 65f);
            Vector3 lookAt = agent.position + new Vector3(0f, 1.4f, 0f);
            Vector3 cameraPosition = agent.position + new Vector3(0f, 7.5f, -10f);
            camera.transform.position = cameraPosition;
            camera.transform.rotation = Quaternion.LookRotation(lookAt - cameraPosition, Vector3.up);
            CreatureCameraRig cameraRig = camera.GetComponent<CreatureCameraRig>();
            if (cameraRig == null)
            {
                cameraRig = Undo.AddComponent<CreatureCameraRig>(camera.gameObject);
                report.Add("已给主摄像机添加数字生物摄像机模式组件。");
            }

            SerializedObject rigObject = new SerializedObject(cameraRig);
            rigObject.FindProperty("target").objectReferenceValue = agent;
            rigObject.FindProperty("targetCamera").objectReferenceValue = camera;
            rigObject.FindProperty("cameraMode").enumValueIndex = (int)CreatureCameraMode.Fixed;
            rigObject.FindProperty("thirdPersonOffset").vector3Value = new Vector3(0f, 3.2f, -6.5f);
            rigObject.FindProperty("lookAtHeight").floatValue = 1.35f;
            rigObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(camera.transform);
            EditorUtility.SetDirty(cameraRig);
            report.Add("已调整主摄像机为固定机位；播放时不会主动跟随或注视数字生物。");
        }

        private static bool IsLikelyAgentHost(GameObject selected)
        {
            if (selected == null)
            {
                return false;
            }

            if (selected.GetComponent<CreatureBrain>() != null ||
                selected.GetComponent<CreatureMotor>() != null ||
                selected.GetComponent<NavMeshAgent>() != null ||
                selected.GetComponentInChildren<Animator>() != null ||
                selected.GetComponentInChildren<SkinnedMeshRenderer>() != null)
            {
                return true;
            }

            string lowerName = selected.name.ToLowerInvariant();
            if (lowerName.Contains("agent") ||
                lowerName.Contains("creature") ||
                lowerName.Contains("digimon") ||
                lowerName.Contains("智能体") ||
                lowerName.Contains("数字生物"))
            {
                return true;
            }

            bool hasSceneMesh = selected.GetComponent<MeshFilter>() != null ||
                                selected.GetComponent<MeshRenderer>() != null ||
                                selected.GetComponent<Collider>() != null;
            return !hasSceneMesh;
        }

        private static void EnsureAgentComponents(GameObject target, CreatureBrain brain, CreatureLlmSettings settings, List<string> report)
        {
            NavMeshAgent navMeshAgent = AddOrGet<NavMeshAgent>(target, report, "NavMeshAgent");
            if (navMeshAgent != null)
            {
                Undo.RecordObject(navMeshAgent, "配置智能体 NavMeshAgent");
                navMeshAgent.speed = Mathf.Max(1.5f, navMeshAgent.speed);
                navMeshAgent.angularSpeed = Mathf.Max(120f, navMeshAgent.angularSpeed);
                navMeshAgent.acceleration = Mathf.Max(8f, navMeshAgent.acceleration);
                navMeshAgent.stoppingDistance = Mathf.Max(0.1f, navMeshAgent.stoppingDistance);
                EditorUtility.SetDirty(navMeshAgent);
            }

            CreatureMotor motor = AddOrGet<CreatureMotor>(target, report, "CreatureMotor");
            CreatureIdleMotion idleMotion = AddOrGet<CreatureIdleMotion>(target, report, "CreatureIdleMotion");
            Animator animator = target.GetComponentInChildren<Animator>(true);
            if (motor != null)
            {
                SerializedObject motorObject = new SerializedObject(motor);
                motorObject.FindProperty("agent").objectReferenceValue = navMeshAgent;
                motorObject.FindProperty("animator").objectReferenceValue = animator;
                motorObject.FindProperty("visualRoot").objectReferenceValue = animator == null ? null : animator.transform;
                motorObject.FindProperty("visualYawOffsetDegrees").floatValue = -90f;
                motorObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(motor);
            }

            if (idleMotion != null && animator != null)
            {
                string demoIdleClipPath = "Assets/SourceFiles/StarterAssets/ThirdPersonController/Character/Animations/Stand--Idle.anim.fbx";
                string demoThinkingClipPath = "Assets/SourceFiles/StarterAssets/ThirdPersonController/Character/Animations/Locomotion--Walk_N.anim.fbx";
                SerializedObject idleObject = new SerializedObject(idleMotion);
                idleObject.FindProperty("animator").objectReferenceValue = animator;
                idleObject.FindProperty("idleClipName").stringValue = "Idle";
                idleObject.FindProperty("thinkingClipName").stringValue = "Walk_N";
                idleObject.FindProperty("controllerStateName").stringValue = "Idle Walk Run Blend";
                idleObject.FindProperty("idleClipPath").stringValue = AssetDatabase.LoadAssetAtPath<Object>(demoIdleClipPath) == null ? string.Empty : demoIdleClipPath;
                idleObject.FindProperty("thinkingClipPath").stringValue = AssetDatabase.LoadAssetAtPath<Object>(demoThinkingClipPath) == null ? string.Empty : demoThinkingClipPath;
                idleObject.FindProperty("nativeAnimationNote").stringValue = "Optional note for demo locomotion clips. Runtime animation is driven by Animator parameters, so customer projects can leave clip paths empty.";
                idleObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(idleMotion);
            }

            Undo.RecordObject(brain, "配置智能体大脑");
            brain.LlmSettings = settings;
            brain.StartOnAwake = true;
            EditorUtility.SetDirty(brain);
            report.Add("已确认智能体大脑使用项目内模型配置资产。");
        }

        private static void EnsureSingleAutonomousBrain(CreatureBrain activeBrain, List<string> report)
        {
            int disabled = 0;
            foreach (CreatureBrain otherBrain in CreatureObjectFinder.FindObjectsByType<CreatureBrain>(true))
            {
                if (otherBrain == null || otherBrain == activeBrain || !otherBrain.StartOnAwake)
                {
                    continue;
                }

                Undo.RecordObject(otherBrain, "关闭重复智能体自动启动");
                otherBrain.StartOnAwake = false;
                EditorUtility.SetDirty(otherBrain);
                disabled++;
            }

            if (disabled > 0)
            {
                report.Add($"已将另外 {disabled} 个 CreatureBrain 设为不自动启动，避免多个智能体同时请求同一模型和记忆。");
            }
        }

        private static void EnsureAgentOnNavMesh(Transform agent, List<string> report)
        {
            if (NavMesh.SamplePosition(agent.position, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            {
                if (Vector3.Distance(agent.position, hit.position) > 0.05f)
                {
                    Undo.RecordObject(agent, "移动智能体到 NavMesh");
                    agent.position = hit.position;
                    EditorUtility.SetDirty(agent);
                    report.Add("已把智能体移动到最近的 NavMesh 可行走位置。");
                }
                else
                {
                    report.Add("智能体已经位于 NavMesh 附近。");
                }

                return;
            }

            report.Add("警告：没有在智能体附近采样到 NavMesh。已继续补齐组件，请确认场景 NavMesh 已烘焙。");
        }

        private static void EnsureLocationMarkers(Vector3 center, List<string> report)
        {
            int before = CreatureObjectFinder.FindObjectsByType<CreatureLocationMarker>(true).Length;
            EnsureLocation("origin", "原点", "spawn,neutral", 3, center, "智能体可以回到的中心位置。", report);
            EnsureLocation("north", "北侧探索点", "explore", 1, center + new Vector3(0f, 0f, 6f), "北侧的可移动探索点。", report);
            EnsureLocation("east", "东侧探索点", "explore", 1, center + new Vector3(6f, 0f, 0f), "东侧的可移动探索点。", report);
            int after = CreatureObjectFinder.FindObjectsByType<CreatureLocationMarker>(true).Length;

            if (after == before)
            {
                report.Add($"已确认场景中有 {after} 个可供 LLM 选择的移动点。");
            }
        }

        private static void EnsureLocation(
            string id,
            string displayName,
            string tags,
            int priority,
            Vector3 desiredPosition,
            string description,
            List<string> report)
        {
            foreach (CreatureLocationMarker existingMarker in CreatureObjectFinder.FindObjectsByType<CreatureLocationMarker>(true))
            {
                if (string.Equals(existingMarker.id, id, System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            Vector3 position = desiredPosition;
            if (NavMesh.SamplePosition(desiredPosition, out NavMeshHit hit, 12f, NavMesh.AllAreas))
            {
                position = hit.position;
            }

            GameObject markerObject = new GameObject("智能体移动点_" + id);
            Undo.RegisterCreatedObjectUndo(markerObject, "创建智能体移动点");
            markerObject.transform.position = position;
            CreatureLocationMarker newMarker = markerObject.AddComponent<CreatureLocationMarker>();
            newMarker.id = id;
            newMarker.displayName = displayName;
            newMarker.tags = tags;
            newMarker.priority = priority;
            newMarker.description = description;
            EditorUtility.SetDirty(newMarker);
            report.Add($"已创建移动点：{displayName}（{id}）。");
        }

        private static void EnsureDebugger(CreatureBrain brain, List<string> report)
        {
            CreatureAgentDebugger debugger = null;
            foreach (CreatureAgentDebugger candidate in CreatureObjectFinder.FindObjectsByType<CreatureAgentDebugger>(true))
            {
                debugger = candidate;
                if (candidate.targetAgent == brain)
                {
                    break;
                }
            }

            if (debugger == null)
            {
                GameObject debuggerObject = new GameObject("智能体调试器");
                Undo.RegisterCreatedObjectUndo(debuggerObject, "创建智能体调试器");
                debugger = debuggerObject.AddComponent<CreatureAgentDebugger>();
                report.Add("已创建智能体调试器。");
            }

            Undo.RecordObject(debugger, "绑定智能体调试器");
            debugger.targetAgent = brain;
            EditorUtility.SetDirty(debugger);
            report.Add("已确认智能体调试器绑定到当前智能体。");
        }

        private static void EnsureSceneInteractables(List<string> report)
        {
            CreatureInteractable[] existing = CreatureObjectFinder.FindObjectsByType<CreatureInteractable>(true);
            if (existing.Length >= 3)
            {
                report.Add($"已确认场景中有 {existing.Length} 个可互动对象。");
                return;
            }

            int added = 0;
            IEnumerable<CreatureSemanticTarget> candidates = CreatureObjectFinder
                .FindObjectsByType<CreatureSemanticTarget>(true)
                .Where(target => target != null &&
                                 target.navigationKind != CreatureNavigationKind.Blocked &&
                                 target.navigationKind != CreatureNavigationKind.InteractOnly)
                .OrderByDescending(target => Mathf.Max(1, target.interestWeight))
                .ThenBy(target => target.name);

            foreach (CreatureSemanticTarget target in candidates)
            {
                if (existing.Length + added >= 3)
                {
                    break;
                }

                CreatureInteractable interactable = target.GetComponent<CreatureInteractable>();
                if (interactable == null)
                {
                    interactable = Undo.AddComponent<CreatureInteractable>(target.gameObject);
                    added++;
                }

                Undo.RecordObject(interactable, "配置可互动对象");
                interactable.interactionId = string.IsNullOrWhiteSpace(target.targetId)
                    ? CreatureSemanticTarget.MakeId(target.name)
                    : target.targetId;
                interactable.displayName = target.displayName;
                interactable.description = string.IsNullOrWhiteSpace(target.description)
                    ? "智能体可以观察这个场景物体。"
                    : target.description;
                interactable.tags = target.semanticTags;
                interactable.interactionRadius = Mathf.Max(2f, target.approachRadius);
                if (interactable.actions == null || interactable.actions.Count == 0)
                {
                    interactable.actions = new List<CreatureInteractionAction>
                    {
                        new CreatureInteractionAction
                        {
                            actionId = "inspect",
                            actionType = CreatureInteractionActionType.CustomEvent,
                            enabled = true
                        }
                    };
                }

                EditorUtility.SetDirty(interactable);
            }

            if (added > 0)
            {
                report.Add($"已给 {added} 个语义物体补充基础互动能力（inspect）。");
            }
            else
            {
                report.Add("未新增可互动对象；可在物体上手动添加 CreatureInteractable。");
            }
        }

        private static T AddOrGet<T>(GameObject target, List<string> report, string displayName) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component != null)
            {
                return component;
            }

            component = Undo.AddComponent<T>(target);
            report.Add($"已补齐组件：{displayName}。");
            return component;
        }

        private static int RemoveComponentIfExists<T>(GameObject owner) where T : Component
        {
            T component = owner.GetComponent<T>();
            if (component == null)
            {
                return 0;
            }

            Undo.DestroyObjectImmediate(component);
            return 1;
        }

        private static void AddOwner(HashSet<GameObject> owners, Component component)
        {
            if (component != null)
            {
                owners.Add(component.gameObject);
            }
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "没有返回报告。";
            }

            int lineBreak = text.IndexOf('\n');
            return lineBreak < 0 ? text : text.Substring(0, lineBreak);
        }
    }
}
