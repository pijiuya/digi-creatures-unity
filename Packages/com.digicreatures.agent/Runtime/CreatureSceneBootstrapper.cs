using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DigiCreatures
{
    public class CreatureSceneBootstrapper : MonoBehaviour
    {
        [SerializeField] private bool createDemoWorld = true;
        [SerializeField] private bool spawnCreature = true;
        [SerializeField] private string playerRobotPrefabPath = "Assets/Prefabs/PlayerRobot.prefab";
        [SerializeField] private Vector3 creatureSpawnPosition = new Vector3(0f, 0.05f, 0f);
        [SerializeField] private Vector3 navMeshCenter = Vector3.zero;
        [SerializeField] private Vector3 navMeshSize = new Vector3(80f, 20f, 80f);
        [SerializeField] private NavMeshCollectGeometry collectGeometry = NavMeshCollectGeometry.PhysicsColliders;

        private NavMeshDataInstance navMeshDataInstance;

        private void Start()
        {
            if (createDemoWorld)
            {
                EnsureDemoWorld();
                BuildRuntimeNavMesh();
            }

            if (spawnCreature)
            {
                EnsureCreature();
            }
        }

        private void OnDestroy()
        {
            navMeshDataInstance.Remove();
        }

        private void EnsureDemoWorld()
        {
            if (GameObject.Find("DigiPlace_Ground") == null)
            {
                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "DigiPlace_Ground";
                ground.transform.position = new Vector3(0f, -0.05f, 0f);
                ground.transform.localScale = new Vector3(18f, 0.1f, 18f);
            }

            EnsureLocation("origin", "Awakening Point", "neutral,spawn", 2, new Vector3(0f, 0f, 0f),
                "The center of DigiPlace, where a new digital soul first stabilizes.");
            EnsureLocation("north_light", "North Light", "light,curiosity", 3, new Vector3(0f, 0f, 6f),
                "A bright northern landmark that feels like a beacon.");
            EnsureLocation("west_memory", "West Memory Edge", "memory,quiet", 3, new Vector3(-6f, 0f, 1.5f),
                "A quiet edge where old signals seem to gather.");
            EnsureLocation("east_gate", "East Gate", "gate,pattern", 2, new Vector3(6f, 0f, -1.5f),
                "A patterned threshold that suggests there may be more beyond the scene.");
            EnsureLocation("south_rest", "South Rest", "rest,safe", 2, new Vector3(0f, 0f, -6f),
                "A calm resting point with a broad view back across DigiPlace.");

            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.transform.position = new Vector3(0f, 8f, -11f);
                camera.transform.rotation = Quaternion.Euler(38f, 0f, 0f);
            }
        }

        private static void EnsureLocation(string id, string displayName, string tags, int priority, Vector3 position, string description)
        {
            CreatureLocationMarker existing = null;
            foreach (CreatureLocationMarker candidate in CreatureObjectFinder.FindObjectsByType<CreatureLocationMarker>(false))
            {
                if (candidate.id == id)
                {
                    existing = candidate;
                    break;
                }
            }

            if (existing != null)
            {
                return;
            }

            GameObject markerObject = new GameObject();
            markerObject.name = "CreatureLocation_" + id;
            markerObject.transform.position = position;

            CreatureLocationMarker newMarker = markerObject.AddComponent<CreatureLocationMarker>();
            newMarker.id = id;
            newMarker.displayName = displayName;
            newMarker.tags = tags;
            newMarker.priority = priority;
            newMarker.description = description;
        }

        private void BuildRuntimeNavMesh()
        {
            Bounds bounds = new Bounds(navMeshCenter, navMeshSize);
            List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, collectGeometry, 0, new List<NavMeshBuildMarkup>(), sources);

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data != null)
            {
                navMeshDataInstance.Remove();
                navMeshDataInstance = NavMesh.AddNavMeshData(data);
            }
        }

        private void EnsureCreature()
        {
            if (FindAnyObjectByType<CreatureBrain>() != null)
            {
                return;
            }

            GameObject creature = LoadPlayerRobotPrefab();
            if (creature == null)
            {
                creature = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                creature.name = "FallbackCreature";
            }

            creature.transform.position = creatureSpawnPosition;
            creature.transform.rotation = Quaternion.identity;

            Animator animator = creature.GetComponentInChildren<Animator>();
            GameObject drivenObject = animator != null ? animator.gameObject : creature;
            drivenObject.name = "DigiSoul01";

            DisablePlayerControl(drivenObject);

            NavMeshAgent agent = drivenObject.GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                agent = drivenObject.AddComponent<NavMeshAgent>();
            }

            agent.radius = 0.35f;
            agent.height = 1.7f;
            agent.acceleration = 8f;
            agent.angularSpeed = 360f;
            agent.stoppingDistance = 0.35f;

            if (drivenObject.GetComponent<CreatureMotor>() == null)
            {
                drivenObject.AddComponent<CreatureMotor>();
            }

            if (drivenObject.GetComponent<CreatureBrain>() == null)
            {
                drivenObject.AddComponent<CreatureBrain>();
            }
        }

        private GameObject LoadPlayerRobotPrefab()
        {
#if UNITY_EDITOR
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerRobotPrefabPath);
            if (prefab != null)
            {
                GameObject instance = Instantiate(prefab);
                instance.name = "DigiSoul01_Rig";
                return instance;
            }
#endif
            return null;
        }

        private static void DisablePlayerControl(GameObject drivenObject)
        {
            foreach (MonoBehaviour behaviour in drivenObject.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null || !IsKnownPlayerControlComponent(behaviour))
                {
                    continue;
                }

                behaviour.enabled = false;
            }

#if ENABLE_INPUT_SYSTEM
            PlayerInput playerInput = drivenObject.GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                playerInput.enabled = false;
            }
#endif

            CharacterController characterController = drivenObject.GetComponent<CharacterController>();
            if (characterController != null)
            {
                characterController.enabled = false;
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
    }
}
