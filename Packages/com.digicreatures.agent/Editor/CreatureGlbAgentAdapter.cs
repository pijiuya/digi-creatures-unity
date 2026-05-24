using System.IO;
using System.Linq;
using DigiCreatures;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DigiCreaturesEditor
{
    public static class CreatureGlbAgentAdapter
    {
        private const string DigiRoomScenePath = "Assets/Scenes/DigiRoom/digiroom.unity";
        private const string GeneratedControllerFolder = "Assets/DigiCreatures/Generated";
        private const string LocomotionStateName = "Agent Model Locomotion";

        [MenuItem("数字生物/高级设置/多智能体/适配选中智能体模型动画")]
        public static void AdaptSelectedAgentModel()
        {
            GameObject target = ResolveSelectedAgentRoot();
            if (target == null)
            {
                EditorUtility.DisplayDialog("多智能体模型动画适配", "请选择一个带 CreatureBrain 的智能体根对象，或选择它下面的模型子物体。", "OK");
                return;
            }

            string report = AdaptModelAgent(target);
            EditorSceneManager.MarkSceneDirty(target.scene);
            EditorUtility.DisplayDialog("多智能体模型动画适配", report, "OK");
        }

        [MenuItem("数字生物/高级设置/多智能体/绑定辅助对象到选中智能体")]
        public static void BindHelpersToSelectedAgent()
        {
            GameObject target = ResolveSelectedAgentRoot();
            if (target == null)
            {
                EditorUtility.DisplayDialog("多智能体辅助对象绑定", "请选择一个带 CreatureBrain 的智能体根对象，或选择它下面的模型子物体。", "OK");
                return;
            }

            CreatureBrain brain = target.GetComponent<CreatureBrain>();
            Transform visualTarget = FindModelRoot(target) ?? target.transform;
            RetargetSceneHelpers(brain, visualTarget);
            EditorSceneManager.MarkSceneDirty(target.scene);
            EditorUtility.DisplayDialog("多智能体辅助对象绑定", $"已将相机和调试器绑定到“{target.name}”。模型控制配置未改动其它智能体。", "OK");
        }

        [MenuItem("数字生物/高级设置/调试/适配 DigiRoom agent2 FBX-GLB 动画")]
        public static void AdaptDigiRoomAgent2()
        {
            Scene scene = EditorSceneManager.OpenScene(DigiRoomScenePath, OpenSceneMode.Single);
            GameObject target = GameObject.Find("agent2");
            if (target == null)
            {
                Debug.LogError("DigiRoom 场景中没有找到 agent2。");
                return;
            }

            string report = AdaptModelAgent(target);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(report);
        }

        public static string AdaptModelAgent(GameObject target)
        {
            NavMeshAgent navMeshAgent = AddOrGet<NavMeshAgent>(target);
            CreatureBrain brain = AddOrGet<CreatureBrain>(target);
            CreatureMotor motor = AddOrGet<CreatureMotor>(target);
            CreatureIdleMotion idleMotion = AddOrGet<CreatureIdleMotion>(target);
            Transform modelRoot = FindModelRoot(target);
            Animator animator = target.GetComponentInChildren<Animator>(true);
            if (animator == null && modelRoot != null)
            {
                animator = Undo.AddComponent<Animator>(modelRoot.gameObject);
            }

            if (animator == null)
            {
                return $"“{target.name}” 下没有找到可挂 Animator 的 FBX/GLB 模型根节点。";
            }

            string assetPath = ResolveModelAssetPath(animator, modelRoot);
            EnsureLoopingAnimationImport(assetPath);
            AnimationClip[] clips = FindLocomotionClips(assetPath);
            if (clips.Length == 0)
            {
                return $"模型里没有找到可用动画 clip。当前资源路径：{assetPath}";
            }

            Avatar avatar = FindAvatar(assetPath);
            string controllerPath = ResolveControllerPath(target, assetPath);
            RuntimeAnimatorController controller = CreateOrReplaceLocomotionController(controllerPath, clips);

            Undo.RecordObjects(new Object[] { navMeshAgent, brain, motor, idleMotion, animator, target.transform }, "适配智能体模型动画");
            navMeshAgent.agentTypeID = 0;
            navMeshAgent.speed = Mathf.Max(1.8f, navMeshAgent.speed);
            navMeshAgent.angularSpeed = Mathf.Max(360f, navMeshAgent.angularSpeed);
            navMeshAgent.acceleration = Mathf.Max(8f, navMeshAgent.acceleration);
            navMeshAgent.stoppingDistance = Mathf.Max(0.35f, navMeshAgent.stoppingDistance);
            navMeshAgent.radius = Mathf.Max(0.25f, navMeshAgent.radius);
            navMeshAgent.height = Mathf.Max(1.2f, navMeshAgent.height);

            animator.runtimeAnimatorController = controller;
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            SerializedObject motorObject = new SerializedObject(motor);
            motorObject.FindProperty("agent").objectReferenceValue = navMeshAgent;
            motorObject.FindProperty("animator").objectReferenceValue = animator;
            motorObject.FindProperty("visualRoot").objectReferenceValue = modelRoot == null ? animator.transform : modelRoot;
            motorObject.FindProperty("visualYawOffsetDegrees").floatValue = 0f;
            motorObject.FindProperty("walkSpeed").floatValue = 1.8f;
            motorObject.FindProperty("runSpeed").floatValue = 3.8f;
            motorObject.FindProperty("arriveDistance").floatValue = 0.35f;
            motorObject.ApplyModifiedProperties();

            SerializedObject idleObject = new SerializedObject(idleMotion);
            idleObject.FindProperty("animator").objectReferenceValue = animator;
            idleObject.FindProperty("idleClipName").stringValue = clips[0].name;
            idleObject.FindProperty("thinkingClipName").stringValue = clips[0].name;
            idleObject.FindProperty("controllerStateName").stringValue = LocomotionStateName;
            idleObject.FindProperty("idleClipPath").stringValue = assetPath;
            idleObject.FindProperty("thinkingClipPath").stringValue = assetPath;
            idleObject.FindProperty("nativeAnimationNote").stringValue = "FBX/GLB locomotion uses the imported looping clip. Speed and MotionSpeed are kept for controller compatibility.";
            idleObject.ApplyModifiedProperties();

            if (brain.LlmSettings == null)
            {
                brain.LlmSettings = CreatureAgentConsoleWindow.LoadOrCreateSettings();
            }

            brain.StartOnAwake = true;

            foreach (Object dirty in new Object[] { navMeshAgent, brain, motor, idleMotion, animator, target.transform })
            {
                EditorUtility.SetDirty(dirty);
            }

            string clipNames = string.Join(", ", clips.Select(clip => clip.name));
            return $"已适配“{target.name}”：绑定 {Path.GetFileName(assetPath)} 的动画（{clipNames}），生成 {controllerPath}，并接入 CreatureMotor。";
        }

        private static GameObject ResolveSelectedAgentRoot()
        {
            if (Selection.activeGameObject == null)
            {
                return null;
            }

            CreatureBrain selectedBrain = Selection.activeGameObject.GetComponentInParent<CreatureBrain>();
            return selectedBrain == null ? null : selectedBrain.gameObject;
        }

        private static T AddOrGet<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(target);
        }

        private static Transform FindModelRoot(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            foreach (Transform child in target.transform)
            {
                string childAssetPath = ResolvePrefabAssetPath(child.gameObject);
                if (IsModelAssetPath(childAssetPath))
                {
                    return child;
                }
            }

            Animator animator = target.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                return animator.transform;
            }

            SkinnedMeshRenderer skinnedMesh = target.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skinnedMesh != null)
            {
                return PrefabUtility.GetNearestPrefabInstanceRoot(skinnedMesh.gameObject)?.transform ?? skinnedMesh.transform;
            }

            return target.transform.childCount > 0 ? target.transform.GetChild(0) : target.transform;
        }

        private static string ResolveModelAssetPath(Animator animator, Transform modelRoot)
        {
            string path = modelRoot == null ? string.Empty : ResolvePrefabAssetPath(modelRoot.gameObject);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            path = ResolvePrefabAssetPath(animator.gameObject);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            return AssetDatabase.GetAssetPath(animator.runtimeAnimatorController);
        }

        private static string ResolvePrefabAssetPath(GameObject instanceObject)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(instanceObject);
            if (source == null)
            {
                GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(instanceObject);
                source = root == null ? null : PrefabUtility.GetCorrespondingObjectFromSource(root);
            }

            return source == null ? string.Empty : AssetDatabase.GetAssetPath(source);
        }

        private static void RetargetSceneHelpers(CreatureBrain brain, Transform visualTarget)
        {
            foreach (CreatureCameraRig rig in CreatureObjectFinder.FindObjectsByType<CreatureCameraRig>(true))
            {
                Undo.RecordObject(rig, "绑定智能体摄像机目标");
                rig.Target = visualTarget;
                EditorUtility.SetDirty(rig);
            }

            foreach (CreatureAgentDebugger debugger in CreatureObjectFinder.FindObjectsByType<CreatureAgentDebugger>(true))
            {
                Undo.RecordObject(debugger, "绑定智能体调试器目标");
                debugger.targetAgent = brain;
                EditorUtility.SetDirty(debugger);
            }
        }

        private static bool IsModelAssetPath(string path)
        {
            return path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".gltf", System.StringComparison.OrdinalIgnoreCase);
        }

        private static Avatar FindAvatar(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Avatar>()
                .FirstOrDefault();
        }

        private static AnimationClip[] FindLocomotionClips(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return new AnimationClip[0];
            }

            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(ClipSortKey)
                .ToArray();

            return clips;
        }

        private static void EnsureLoopingAnimationImport(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null || !importer.importAnimation)
            {
                return;
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            bool changed = false;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                if (!clip.loopTime || !clip.loopPose || clip.wrapMode != WrapMode.Loop)
                {
                    clip.loopTime = true;
                    clip.loopPose = true;
                    clip.wrapMode = WrapMode.Loop;
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static int ClipSortKey(AnimationClip clip)
        {
            string lowerName = clip.name.ToLowerInvariant();
            if (lowerName.Contains("walk"))
            {
                return 0;
            }

            if (lowerName.Contains("run"))
            {
                return 1;
            }

            return 2;
        }

        private static string ResolveControllerPath(GameObject target, string assetPath)
        {
            Directory.CreateDirectory(GeneratedControllerFolder);
            string agentName = CreatureProfile.SanitizePathSegment(target.name);
            string modelName = CreatureProfile.SanitizePathSegment(Path.GetFileNameWithoutExtension(assetPath));
            return Path.Combine(GeneratedControllerFolder, $"{agentName}_{modelName}_Locomotion.controller").Replace("\\", "/");
        }

        private static RuntimeAnimatorController CreateOrReplaceLocomotionController(string controllerPath, AnimationClip[] clips)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(controllerPath));
            AssetDatabase.DeleteAsset(controllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("MotionSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);

            if (clips.Length == 1)
            {
                AnimatorState singleState = controller.layers[0].stateMachine.AddState(LocomotionStateName);
                controller.layers[0].stateMachine.defaultState = singleState;
                singleState.motion = clips[0];
                singleState.speedParameterActive = false;
                singleState.speedParameter = string.Empty;
                singleState.speed = 1f;
                singleState.writeDefaultValues = true;
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return controller;
            }

            AnimatorState state = controller.CreateBlendTreeInController(LocomotionStateName, out BlendTree blendTree);
            controller.layers[0].stateMachine.defaultState = state;
            state.speedParameterActive = false;
            state.speedParameter = string.Empty;
            state.speed = 1f;
            state.writeDefaultValues = true;

            blendTree.blendType = BlendTreeType.Simple1D;
            blendTree.blendParameter = "Speed";
            blendTree.useAutomaticThresholds = false;
            for (int i = 0; i < clips.Length; i++)
            {
                blendTree.AddChild(clips[i], i == 0 ? 0.05f : 2.2f + i);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return controller;
        }
    }
}
