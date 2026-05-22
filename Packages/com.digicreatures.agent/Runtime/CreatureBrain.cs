using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DigiCreatures
{
    [RequireComponent(typeof(CreatureMotor))]
    public class CreatureBrain : MonoBehaviour
    {
        [SerializeField] private CreatureProfile profile;
        [SerializeField] private string creatureDataPath;
        [SerializeField] private CreatureLlmSettings llmSettings;
        [SerializeField] private bool startOnAwake = true;
        [SerializeField] private CreatureAgentMode agentMode = CreatureAgentMode.Move;
        [SerializeField] private float interactionScanRadius = 5f;
        [SerializeField] private float interactionExecutionGrace = 2.5f;
        [SerializeField] private float decisionIntervalSeconds = 2.5f;
        [SerializeField] private float maximumDwellSeconds = 4f;
        [SerializeField] private float minimumSubtitleVisibleSeconds = 8f;
        [SerializeField] private float subtitleExtraVisibleSeconds = 3f;
        [SerializeField] private float wanderRadius = 8f;
        [SerializeField] private bool preferObjectsOverFixedMarkers = true;
        [SerializeField] private bool localReactionsWhileThinking = true;
        [SerializeField] private string currentIntent;
        [SerializeField] private string lastDialogue;
        [SerializeField] private string lastDecisionError;
        [SerializeField] private string lastDestinationId;
        [SerializeField] private string lastTargetId;
        [SerializeField] private string lastRegionId;
        [SerializeField] private string lastTargetInterest;
        [SerializeField] private string lastActivity;
        [SerializeField] private string lastStatus;
        [SerializeField] private long lastLlmLatencyMs;
        [SerializeField] private bool useTestMemory;
        [SerializeField] private string testMemoryFileName = "test-memory.jsonl";
        [SerializeField] private bool recordLlmMetrics;

        private CreatureConfig config;
        private CreatureMemoryStore memory;
        private CreatureMotor motor;
        private CreatureIdleMotion idleMotion;
        private ILlmClient llmClient;
        private CreatureLocationMarker[] locations;
        private CreatureInteractable[] interactables;
        private CreatureSemanticTarget[] semanticTargets;
        private CreatureSemanticRegion[] regions;
        private readonly Queue<string> recentDialogueKeys = new Queue<string>();
        private readonly Queue<string> recentIntentKeys = new Queue<string>();
        private int dialogueFallbackIndex;
        private int intentFallbackIndex;
        private string resolvedSubtitleName;

        public CreatureAgentMode AgentMode
        {
            get => agentMode;
            set => agentMode = value;
        }

        public string CreatureDataPath
        {
            get => creatureDataPath;
            set => creatureDataPath = value;
        }

        public CreatureProfile Profile
        {
            get => profile;
            set => profile = value;
        }

        public CreatureLlmSettings LlmSettings
        {
            get => llmSettings;
            set => llmSettings = value;
        }

        public bool StartOnAwake
        {
            get => startOnAwake;
            set => startOnAwake = value;
        }

        public string LastDialogue => lastDialogue;
        public string CurrentIntent => currentIntent;
        public string LastDecisionError => lastDecisionError;
        public string LastDestinationId => lastDestinationId;
        public string LastTargetId => lastTargetId;
        public string LastRegionId => lastRegionId;
        public string LastTargetInterest => lastTargetInterest;
        public string LastActivity => lastActivity;
        public string LastStatus => lastStatus;
        public long LastLlmLatencyMs => lastLlmLatencyMs;

        public string DisplayName => ResolveDisplayName();

        public bool UseTestMemory
        {
            get => useTestMemory;
            set => useTestMemory = value;
        }

        public string TestMemoryFileName
        {
            get => testMemoryFileName;
            set => testMemoryFileName = value;
        }

        public bool RecordLlmMetrics
        {
            get => recordLlmMetrics;
            set => recordLlmMetrics = value;
        }

        private void Awake()
        {
            motor = GetComponent<CreatureMotor>();
            idleMotion = GetComponent<CreatureIdleMotion>();
            if (idleMotion == null)
            {
                idleMotion = gameObject.AddComponent<CreatureIdleMotion>();
            }

            llmClient = new OpenAICompatibleLlmClient();
            LoadConfig();
            EnsureLlmSettings();
            memory = new CreatureMemoryStore(
                ResolveCreatureDataPath(),
                useTestMemory ? SafeMemoryFileName(testMemoryFileName) : "memory.jsonl",
                useTestMemory ? "test-summary.md" : "summary.md",
                profile == null ? "# DigiSoul\n\n一个好奇的数字生物。\n" : profile.DefaultSoulText,
                profile == null ? "# Memory Summary\n\nNo long-term memories yet.\n" : profile.DefaultSummaryText);
            resolvedSubtitleName = ResolveDisplayName();
        }

        private void Start()
        {
            if (startOnAwake)
            {
                StartCoroutine(ThinkLoop());
            }
        }

        private IEnumerator ThinkLoop()
        {
            memory.AppendEvent("awake", $"{ResolveDisplayName()} began autonomous wandering.");
            SetStatus("已启动", new Color(0.2f, 0.65f, 1f, 0.94f), "idle");

            while (enabled)
            {
                RefreshPerception();
                CreatureDecision decision = null;
                string error = null;

                if (locations.Length > 0 || interactables.Length > 0 || semanticTargets.Length > 0 || regions.Length > 0)
                {
                    string prompt = BuildPrompt(locations, interactables, semanticTargets, regions);
                    yield return RequestDecisionWithReactions(prompt, (result, requestError) =>
                    {
                        decision = result;
                        error = requestError;
                        lastDecisionError = requestError;
                    });

                    lastLlmLatencyMs = llmClient.LastLatencyMs;
                    if (recordLlmMetrics)
                    {
                        memory.AppendEvent("llm_metrics", $"latencyMs={llmClient.LastLatencyMs}; success={decision != null}; error={error}");
                    }
                }

                if (decision != null && IsLocalActivityDecision(decision))
                {
                    HandleLocalActivityDecision(decision);
                    SetStatus(StatusForActivity(lastActivity), ColorForActivity(lastActivity), lastActivity);
                    yield return motor.Dwell(GetDwellSeconds(decision), PickLookTarget());
                    yield return new WaitForSeconds(GetDecisionInterval());
                    continue;
                }

                if (decision != null && IsDialogueDecision(decision))
                {
                    HandleDialogueDecision(decision);
                    SetStatus("对话中", new Color(0.75f, 0.38f, 1f, 0.94f), "speak");
                    yield return motor.Dwell(GetDwellSeconds(decision), transform);
                    yield return new WaitForSeconds(GetDecisionInterval());
                    continue;
                }

                if (decision == null && agentMode == CreatureAgentMode.Dialogue)
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        Debug.LogWarning($"LLM dialogue unavailable, using fallback: {error}");
                    }

                    HandleDialogueDecision(BuildFallbackDialogueDecision(error));
                    SetStatus("离线对话", new Color(1f, 0.62f, 0.18f, 0.94f), "speak");
                    yield return motor.Dwell(2f, transform);
                    yield return new WaitForSeconds(GetDecisionInterval());
                    continue;
                }

                MovementGoal destination = ResolveMovementGoal(decision);
                if (destination == null)
                {
                    CreatureDecision localDecision = BuildFallbackLocalActivityDecision(error);
                    HandleLocalActivityDecision(localDecision);
                    SetStatus(StatusForActivity(lastActivity), ColorForActivity(lastActivity), lastActivity);
                    yield return motor.Dwell(GetDwellSeconds(localDecision), PickLookTarget());
                    yield return new WaitForSeconds(GetDecisionInterval());
                    continue;
                }

                if (decision == null)
                {
                    decision = BuildFallbackDecision(destination, error);
                }
                else if (!DecisionAlreadyReferencesGoal(decision, destination))
                {
                    memory.AppendEvent("decision_fallback", $"LLM 选择的点不可直接到达，改用 '{destination.Id}'。");
                    decision.destinationId = destination.Id;
                    decision.approachPointId = destination.Id;
                }

                destination = ApplyDiversityCooldown(destination, decision);

                lastActivity = string.IsNullOrWhiteSpace(decision.activity) ? "approach" : decision.activity;
                lastDestinationId = destination.Id;
                RecordSemanticChoice(decision, destination);
                string targetText = string.IsNullOrWhiteSpace(decision.targetId)
                    ? string.IsNullOrWhiteSpace(decision.regionId) ? "no semantic target" : $"region:{decision.regionId}"
                    : $"{decision.targetId} ({decision.targetName})";
                memory.AppendEvent("decision", $"Going to {destination.Id} by {decision.movement}. Target: {targetText}. Region: {decision.regionId}. Source: {destination.SourceDescription}. Activity: {decision.activity}. Interest: {decision.targetInterest}. Intent: {decision.intent}. Dialogue: {decision.dialogue}. Interaction: {decision.interactionId}/{decision.actionId}");
                string spokenLine = BuildDisplayDialogue(decision, destination);
                string visibleIntent = BuildDisplayIntent(decision, spokenLine);
                currentIntent = visibleIntent;
                lastDialogue = spokenLine;
                CreatureCameraRig.BeginMovementShot(transform, transform.position, destination.Position);
                SetStatus(destination.IsWander ? "散步中" : "移动中", new Color(0.22f, 0.72f, 0.36f, 0.94f), "move");
                LogDecision(lastStatus, lastActivity, targetText, destination.DisplayName, decision.targetInterest, visibleIntent, spokenLine, lastDecisionError);
                ShowSubtitle(spokenLine, visibleIntent, GetDwellSeconds(decision));
                yield return motor.MoveToPosition(destination.Position, decision.movement, destination.DisplayName);
                CreatureCameraRig.EndMovementShot(transform);

                if (motor.LastMoveSucceeded)
                {
                    RefreshPerception();
                    TryExecuteInteraction(decision);
                    memory.AppendEvent("arrived", $"Arrived at {destination.Id}. {decision.memoryNote}");
                    SetStatus("观察中", new Color(0.2f, 0.65f, 1f, 0.94f), "idle");
                }
                else
                {
                    memory.AppendEvent("move_failed", $"Could not reach {destination.Id}: {motor.LastMoveError}. {decision.memoryNote}");
                    SetStatus("路径受阻", new Color(1f, 0.28f, 0.22f, 0.94f), "think");
                }

                memory.RefreshSimpleSummary(config.summaryEveryEvents);
                yield return motor.Dwell(GetDwellSeconds(decision), destination.LookAt);
                yield return new WaitForSeconds(GetDecisionInterval());
            }
        }

        private void LoadConfig()
        {
            string path = Path.Combine(ResolveCreatureDataPath(), "config.json");
            if (File.Exists(path))
            {
                config = JsonUtility.FromJson<CreatureConfig>(File.ReadAllText(path));
            }

            if (config == null)
            {
                config = new CreatureConfig();
            }
        }

        private void EnsureLlmSettings()
        {
            if (llmSettings != null)
            {
                return;
            }

            if (profile != null && profile.llmSettings != null)
            {
                llmSettings = profile.llmSettings;
                return;
            }

            llmSettings = ScriptableObject.CreateInstance<CreatureLlmSettings>();
            if (config != null)
            {
                llmSettings.backend = config.UseOnlineBackend ? "remote" : "ollama";
                llmSettings.localEndpoint = config.localEndpoint;
                llmSettings.localModel = config.localModel;
                llmSettings.remoteEndpoint = config.onlineEndpoint;
                llmSettings.remoteModel = config.onlineModel;
                llmSettings.remoteApiKeyEnvironmentVariable = config.onlineApiKeyEnvironmentVariable;
                llmSettings.temperature = config.temperature;
                llmSettings.requestTimeoutSeconds = config.requestTimeoutSeconds;
            }
        }

        private string BuildPrompt(
            IEnumerable<CreatureLocationMarker> availableLocations,
            IEnumerable<CreatureInteractable> availableInteractables,
            IEnumerable<CreatureSemanticTarget> availableSemanticTargets,
            IEnumerable<CreatureSemanticRegion> availableRegions)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Soul setting:");
            AppendLimitedText(builder, memory.Soul, 900);
            builder.AppendLine();
            builder.AppendLine("Long-term memory summary:");
            AppendLimitedText(builder, memory.Summary, 900);
            builder.AppendLine();
            builder.AppendLine("Recent memory events:");
            foreach (string line in memory.ReadRecentMemories(Mathf.Min(config.recentMemoryLimit, 5)))
            {
                AppendLimitedText(builder, line, 220);
            }

            builder.AppendLine();
            builder.AppendLine($"Current creature position: {transform.position}");
            builder.AppendLine($"Current requested mode: {ModeToPromptValue(agentMode)}.");
            if (!string.IsNullOrWhiteSpace(lastTargetId))
            {
                builder.AppendLine($"Recent semantic target cooldown: {lastTargetId}. Prefer a different targetId for the next decision unless every other reachable object is unsuitable.");
            }
            if (!string.IsNullOrWhiteSpace(lastRegionId))
            {
                builder.AppendLine($"Recent region cooldown: {lastRegionId}. If you choose it again, give a fresh reason and expect the runtime to sample a new point inside it.");
            }
            if (!string.IsNullOrWhiteSpace(lastDialogue))
            {
                builder.AppendLine($"Previous spoken line to avoid repeating: {lastDialogue}");
            }
            if (!string.IsNullOrWhiteSpace(currentIntent))
            {
                builder.AppendLine($"Previous inner thought to avoid repeating: {currentIntent}");
            }

            builder.AppendLine("Unity NavMesh areas visible to the creature:");
            builder.AppendLine(GetNavAreaSummary());
            builder.AppendLine();
            builder.AppendLine("Scene objects of interest. Prefer choosing targetId/interactionId over fixed compass-like points. The runtime can find a fresh NavMesh approach point near the object:");
            foreach (CreatureSemanticTarget target in availableSemanticTargets.Take(10))
            {
                builder.AppendLine("- " + target.ToPromptLine(transform));
            }

            builder.AppendLine();
            builder.AppendLine("Scene semantic regions. Prefer choosing regionId when you want free roaming in a meaningful area; the runtime will generate a random reachable NavMesh point inside the region:");
            foreach (CreatureSemanticRegion region in availableRegions
                         .OrderByDescending(region => Mathf.Max(1, region.priority))
                         .ThenBy(region => Vector3.Distance(transform.position, region.transform.position))
                         .Take(8))
            {
                builder.AppendLine("- " + region.ToPromptLine(transform));
            }

            builder.AppendLine();
            builder.AppendLine("Fixed backup points. Use destinationId only for special viewpoints, blocked-object observation, or when no object/region/wander/rest/roll choice is better:");
            foreach (CreatureLocationMarker location in availableLocations
                         .OrderByDescending(location => !string.IsNullOrWhiteSpace(location.semanticTargetId))
                         .ThenBy(location => Vector3.Distance(transform.position, location.transform.position))
                         .Take(8))
            {
                builder.AppendLine("- " + DescribeLocation(location));
            }

            builder.AppendLine();
            builder.AppendLine("Nearby interactable objects. You may choose one interactionId and one listed actionId if useful:");
            foreach (CreatureInteractable interactable in availableInteractables)
            {
                builder.AppendLine("- " + interactable.ToPromptLine(transform));
            }

            builder.AppendLine();
            builder.AppendLine("Return only JSON with this schema:");
            builder.AppendLine("{\"mode\":\"move|dialogue\",\"targetId\":\"optional listed target id\",\"regionId\":\"optional listed region id\",\"targetName\":\"chosen object or region display name\",\"targetInterest\":\"why this object or region is interesting\",\"navigationKind\":\"Walkable|Climbable|Blocked|JumpArea|Platform|InteractOnly\",\"approachPointId\":\"optional backup point id\",\"destinationId\":\"optional backup point id\",\"movement\":\"walk|run\",\"dwellSeconds\":2,\"dialogue\":\"spoken Chinese line\",\"interactionId\":\"optional listed id\",\"actionId\":\"optional listed action id\",\"activity\":\"approach|interact|wander|rest|roll|speak\",\"intent\":\"private inner reason\",\"memoryNote\":\"short memory note\"}");
            builder.AppendLine("Strict field rules: mode must be exactly move or dialogue. movement must be exactly walk or run. targetInterest, intent, dialogue, and memoryNote must be JSON strings, never numbers, arrays, objects, or null.");
            builder.AppendLine("Meaning rules: intent is the creature's private inner thought/reason and should not be spoken aloud. dialogue is the line the creature says aloud in the bottom subtitle. Always provide both, and make them different.");
            builder.AppendLine("Repeat rule: do not repeat the previous dialogue or intent. Continue the same theme only if you phrase it differently and add a new observation.");
            builder.AppendLine("Language rhythm: dialogue is usually 8-36 Chinese characters, but about one in four decisions may use 40-70 Chinese characters when the creature discovers a tower, skyline, region, or memory clue. intent is usually 18-60 Chinese characters, and may be 60-100 characters when explaining a meaningful choice.");
            builder.AppendLine("Subtitle rules: dialogue and intent must be natural Chinese text only. Do not put JSON, key names, English explanations, arrays, brackets, quotes, coordinates, or action tokens inside dialogue or intent.");
            builder.AppendLine("Personality rules: speak as an alien astronaut robot who loves watching stars, measuring towers, and searching for lost star maps. Use warm, curious, poetic Chinese. Avoid cold navigation phrases.");
            builder.AppendLine("Rules: targetId must be from the scene object list. regionId must be from the scene region list. For targetId or regionId movement, destinationId may be empty; the runtime will sample a fresh reachable NavMesh point.");
            builder.AppendLine("Interaction rules: if activity is interact, interactionId must be exactly one listed interactable id, and actionId must be exactly the action id before the colon. Example: for actions=inspect:CustomEvent, output actionId=\"inspect\", never \"inspect:CustomEvent\".");
            builder.AppendLine("Interaction rules: when a nearby interactable is available, occasionally perform a real interaction instead of only describing it in dialogue.");
            builder.AppendLine("Rules: fixed backup points are not primary destinations. Use them only for blocked boundaries, special viewpoints, or fallback.");
            builder.AppendLine("Rules: blocked objects are boundaries; if interested in a Blocked object, choose a nearby viewpoint marker instead of the object itself.");
            builder.AppendLine("Rules: avoid choosing the same semantic target repeatedly. Sometimes choose activity=wander for free walking, activity=rest for a short pause, or activity=roll for playful idle behavior.");
            return builder.ToString();
        }

        private static void AppendLimitedText(StringBuilder builder, string value, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string trimmed = value.Trim();
            if (trimmed.Length > maxCharacters)
            {
                trimmed = trimmed.Substring(0, Mathf.Max(1, maxCharacters - 1)) + "...";
            }

            builder.AppendLine(trimmed);
        }

        private MovementGoal ResolveMovementGoal(CreatureDecision decision)
        {
            if (decision != null && preferObjectsOverFixedMarkers)
            {
                MovementGoal objectGoal = ResolveObjectGoal(decision);
                if (objectGoal != null)
                {
                    return objectGoal;
                }
            }

            MovementGoal regionGoal = ResolveRegionGoal(decision, 1f, "LLM selected semantic region");
            if (regionGoal != null)
            {
                return regionGoal;
            }

            if (decision != null && locations != null && !string.IsNullOrWhiteSpace(decision.destinationId))
            {
                CreatureLocationMarker chosen = locations.FirstOrDefault(location => location.id == decision.destinationId);
                if (chosen != null)
                {
                    return MovementGoal.FromMarker(chosen);
                }
            }

            if (decision != null && locations != null && !string.IsNullOrWhiteSpace(decision.approachPointId))
            {
                CreatureLocationMarker approach = locations.FirstOrDefault(location => location.id == decision.approachPointId);
                if (approach != null)
                {
                    return MovementGoal.FromMarker(approach);
                }
            }

            MovementGoal targetMarker = ResolveMarkerGoalForTarget(decision);
            if (targetMarker != null)
            {
                return targetMarker;
            }

            if (decision == null ||
                IsActivity(decision.activity, "wander") ||
                Random.value > 0.35f)
            {
                MovementGoal regionWander = BuildWeightedRegionGoal(1f, "weighted semantic region wander");
                if (regionWander != null)
                {
                    return regionWander;
                }

                MovementGoal wander = BuildWanderGoal();
                if (wander != null)
                {
                    return wander;
                }
            }

            if (locations == null || locations.Length == 0)
            {
                Debug.LogWarning("CreatureBrain found no CreatureLocationMarker objects.");
                return null;
            }

            int totalWeight = locations.Sum(location => Mathf.Max(1, location.priority));
            int roll = Random.Range(0, totalWeight);
            foreach (CreatureLocationMarker location in locations)
            {
                roll -= Mathf.Max(1, location.priority);
                if (roll < 0)
                {
                    return MovementGoal.FromMarker(location);
                }
            }

            return MovementGoal.FromMarker(locations[0]);
        }

        private MovementGoal ApplyDiversityCooldown(MovementGoal destination, CreatureDecision decision)
        {
            if (destination == null || decision == null)
            {
                return destination;
            }

            string chosenTarget = GetSemanticTargetKey(decision, destination);
            string chosenRegion = GetRegionKey(decision, destination);
            if (!string.Equals(chosenRegion, lastRegionId, System.StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(chosenTarget, lastTargetId, System.StringComparison.OrdinalIgnoreCase))
            {
                return destination;
            }

            MovementGoal alternative = null;
            if (!string.IsNullOrWhiteSpace(chosenRegion))
            {
                alternative = ResolveRegionGoal(decision, 1.35f, "region cooldown resample");
            }

            if (alternative == null &&
                !string.IsNullOrWhiteSpace(chosenTarget) &&
                semanticTargets != null)
            {
                CreatureSemanticTarget repeatedTarget = semanticTargets.FirstOrDefault(target =>
                    target != null &&
                    string.Equals(target.targetId, chosenTarget, System.StringComparison.OrdinalIgnoreCase));
                alternative = repeatedTarget == null ? null : BuildGoalNearSemanticTarget(repeatedTarget, 1.5f, "target cooldown resample");
            }

            CreatureSemanticTarget alternativeTarget = alternative == null && semanticTargets != null
                ? semanticTargets
                .Where(target => target != null &&
                                 !string.Equals(target.targetId, lastTargetId, System.StringComparison.OrdinalIgnoreCase) &&
                                 target.navigationKind != CreatureNavigationKind.Blocked)
                .OrderByDescending(target => Mathf.Max(1, target.interestWeight))
                .ThenBy(target => Vector3.Distance(transform.position, target.transform.position))
                .FirstOrDefault()
                : null;

            alternative = alternative ?? (alternativeTarget == null ? null : BuildGoalNearSemanticTarget(alternativeTarget, 1f, "alternate semantic target"));
            if (alternative == null && locations != null)
            {
                CreatureLocationMarker marker = locations
                    .Where(location => location != null &&
                                       location.id != destination.Id &&
                                       !string.IsNullOrWhiteSpace(location.semanticTargetId) &&
                                       !string.Equals(location.semanticTargetId, lastTargetId, System.StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(location => Mathf.Max(1, location.priority))
                    .ThenBy(location => Vector3.Distance(transform.position, location.transform.position))
                    .FirstOrDefault();
                alternative = marker == null ? null : MovementGoal.FromMarker(marker);
            }

            if (alternative == null)
            {
                return destination;
            }

            string repeated = string.IsNullOrWhiteSpace(chosenRegion) ? chosenTarget : chosenRegion;
            memory.AppendEvent("decision_fallback", $"近期目标冷却：LLM 连续选择 '{repeated}'，动态改去 '{alternative.Id}'。Source: {alternative.SourceDescription}");
            decision.destinationId = alternative.Id;
            decision.approachPointId = alternative.Id;
            decision.targetId = alternative.SemanticTargetId;
            decision.regionId = alternative.RegionId;
            decision.targetName = alternative.DisplayName;
            decision.navigationKind = alternative.NavigationKind.ToString();
            if (string.IsNullOrWhiteSpace(decision.targetInterest))
            {
                decision.targetInterest = "近期目标冷却后探索另一个语义对象。";
            }

            return alternative;
        }

        private CreatureDecision BuildFallbackDecision(MovementGoal destination, string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogWarning($"LLM decision unavailable, using fallback: {error}");
            }

            return new CreatureDecision
            {
                mode = ModeToPromptValue(agentMode),
                destinationId = destination.Id,
                movement = Random.value > 0.8f ? "run" : "walk",
                dwellSeconds = Random.Range(1.2f, 3.5f),
                targetId = destination.SemanticTargetId,
                targetName = destination.DisplayName,
                targetInterest = "Fallback chose a reachable scene point.",
                regionId = destination.RegionId,
                navigationKind = destination.NavigationKind.ToString(),
                approachPointId = destination.Id,
                activity = destination.IsWander ? "wander" : "approach",
                dialogue = destination.IsWander
                    ? "我沿着这片区域慢慢巡航，看看有没有遗失星图的微光。"
                    : "我靠近那里观星，也许能测到回家的方向。",
                intent = "模型信号暂时安静，我先按外星宇航员机器人的直觉，寻找能对齐天空和塔影的可观测点。",
                memoryNote = $"Fallback wandering led to {destination.DisplayName}."
            };
        }

        private CreatureDecision BuildFallbackLocalActivityDecision(string error)
        {
            bool roll = Random.value > 0.55f;
            return new CreatureDecision
            {
                mode = "move",
                activity = roll ? "roll" : "rest",
                dwellSeconds = Random.Range(1.5f, 3f),
                dialogue = roll ? "我在这里轻轻打个滚，给星图接收器换个角度。" : "我先安静一会儿，听听这座场景里有没有星尘回声。",
                intent = string.IsNullOrWhiteSpace(error)
                    ? "短暂停留，让姿态传感器重新校准天空方向。"
                    : "模型暂时不可用，我先做一个本地待机动作，同时保持外星宇航员的观测习惯。",
                memoryNote = roll ? "The creature rolled in place." : "The creature rested in place."
            };
        }

        private CreatureDecision BuildFallbackDialogueDecision(string error)
        {
            return new CreatureDecision
            {
                mode = "dialogue",
                dwellSeconds = 4f,
                dialogue = string.IsNullOrWhiteSpace(error)
                    ? "我在听星图的回声，它像很远的故乡在闪烁。"
                    : "信号静默，我先把目光交给天空。",
                intent = "暂停并用外星宇航员机器人的方式确认自身信号，等待下一次能指向星空的行动。",
                memoryNote = "Fallback dialogue was used while the LLM was unavailable."
            };
        }

        private CreatureSemanticTarget[] FindSemanticTargets()
        {
            return FindObjectsByType<CreatureSemanticTarget>(FindObjectsInactive.Exclude)
                .Where(target => target != null && target.isActiveAndEnabled)
                .OrderByDescending(target => Mathf.Max(1, target.interestWeight))
                .ThenBy(target => Vector3.Distance(transform.position, target.transform.position))
                .Take(18)
                .ToArray();
        }

        private CreatureSemanticRegion[] FindSemanticRegions()
        {
            return FindObjectsByType<CreatureSemanticRegion>(FindObjectsInactive.Exclude)
                .Where(region => region != null && region.isActiveAndEnabled)
                .OrderByDescending(region => Mathf.Max(1, region.priority))
                .ThenBy(region => Vector3.Distance(transform.position, region.transform.position))
                .Take(12)
                .ToArray();
        }

        private CreatureInteractable[] FindNearbyInteractables()
        {
            return FindObjectsByType<CreatureInteractable>(FindObjectsInactive.Exclude)
                .Where(interactable => interactable != null &&
                                       Vector3.Distance(transform.position, interactable.transform.position) <=
                                       Mathf.Max(interactionScanRadius, interactable.interactionRadius))
                .ToArray();
        }

        private void RefreshPerception()
        {
            locations = FindObjectsByType<CreatureLocationMarker>(FindObjectsInactive.Exclude);
            interactables = FindNearbyInteractables();
            semanticTargets = FindSemanticTargets();
            regions = FindSemanticRegions();
        }

        private IEnumerator RequestDecisionWithReactions(string prompt, System.Action<CreatureDecision, string> onComplete)
        {
            bool done = false;
            CreatureDecision result = null;
            string requestError = null;
            float startedAt = Time.time;
            float nextReactionAt = Time.time + 1.2f;
            bool showedThinking = false;

            SetStatus("思考中", new Color(1f, 0.72f, 0.16f, 0.94f), "think");
            lastDecisionError = string.Empty;
            string modelSummary = llmSettings == null
                ? "没有绑定模型配置。"
                : $"正在请求 {llmSettings.Model}，等待模型返回 CreatureDecision JSON。";
            LogDecision("请求模型", "think", string.Empty, string.Empty, modelSummary, string.Empty, string.Empty);
            StartCoroutine(llmClient.RequestDecision(prompt, llmSettings, (decision, error) =>
            {
                result = decision;
                requestError = error;
                done = true;
            }));

            while (!done)
            {
                if (localReactionsWhileThinking && Time.time >= nextReactionAt)
                {
                    lastActivity = "等待模型";
                    Transform lookTarget = PickLookTarget();
                    if (lookTarget != null)
                    {
                        TurnToward(lookTarget.position, 2.5f);
                    }

                    if (!showedThinking && Time.time - startedAt > 2f)
                    {
                        LogDecision("等待模型", "think", string.Empty, string.Empty, "LLM 仍在生成回复；场景气泡不再显示等待占位文本。", string.Empty, string.Empty);
                        showedThinking = true;
                    }

                    nextReactionAt = Time.time + 1.4f;
                }

                yield return null;
            }

            onComplete?.Invoke(result, requestError);
            if (result == null && !string.IsNullOrWhiteSpace(requestError))
            {
                SetStatus("模型未响应", new Color(1f, 0.36f, 0.22f, 0.94f), "think");
                string requestSummary = string.IsNullOrWhiteSpace(llmClient.LastRequestSummary) ? string.Empty : llmClient.LastRequestSummary;
                LogDecision(lastStatus, lastActivity, string.Empty, string.Empty, "LLM 请求失败，智能体使用本地兜底行为。" + requestSummary, string.Empty, requestError);
            }
        }

        private static bool IsDialogueDecision(CreatureDecision decision)
        {
            return string.Equals(decision.mode, "dialogue", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLocalActivityDecision(CreatureDecision decision)
        {
            return decision != null &&
                   (IsActivity(decision.activity, "rest") ||
                    IsActivity(decision.activity, "roll") ||
                    IsActivity(decision.activity, "idle"));
        }

        private static bool IsActivity(string activity, string expected)
        {
            return string.Equals(activity, expected, System.StringComparison.OrdinalIgnoreCase);
        }

        private void HandleDialogueDecision(CreatureDecision decision)
        {
            lastActivity = "speak";
            lastDialogue = BuildDisplayDialogue(decision, null);
            currentIntent = BuildDisplayIntent(decision, lastDialogue);
            TryExecuteInteraction(decision);
            SetStatus("对话中", new Color(0.75f, 0.38f, 1f, 0.94f), "speak");
            LogDecision(lastStatus, lastActivity, decision.targetName, decision.destinationId, decision.targetInterest, currentIntent, lastDialogue, lastDecisionError);
            ShowSubtitle(lastDialogue, currentIntent, GetDwellSeconds(decision));
            memory.AppendEvent("dialogue", $"{ResolveDisplayName()}: {lastDialogue}. Intent: {currentIntent}");
            memory.RefreshSimpleSummary(config.summaryEveryEvents);
        }

        private void HandleLocalActivityDecision(CreatureDecision decision)
        {
            lastActivity = string.IsNullOrWhiteSpace(decision.activity) ? "rest" : decision.activity;
            string line = CleanModelTextForDisplay(decision.dialogue);
            if (string.IsNullOrWhiteSpace(line))
            {
                line = IsActivity(lastActivity, "roll") ? "我打个滚。" : "我在这里休息一下。";
            }

            if (WasRecentlyUsed(recentDialogueKeys, line))
            {
                line = PickDialogueFallback(null);
            }

            RememberText(recentDialogueKeys, line);
            currentIntent = BuildDisplayIntent(decision, line);
            lastDialogue = line;
            SetStatus(StatusForActivity(lastActivity), ColorForActivity(lastActivity), lastActivity);
            LogDecision(lastStatus, lastActivity, decision.targetName, decision.destinationId, decision.targetInterest, currentIntent, lastDialogue, lastDecisionError);
            ShowSubtitle(line, currentIntent, GetDwellSeconds(decision));
            memory.AppendEvent("local_activity", $"{ResolveDisplayName()}: {lastActivity}. {decision.memoryNote}");
            memory.RefreshSimpleSummary(config.summaryEveryEvents);
        }

        private void TryExecuteInteraction(CreatureDecision decision)
        {
            if (decision == null)
            {
                return;
            }

            NormalizeInteractionFields(decision);
            bool shouldInspectArrivedTarget =
                !string.IsNullOrWhiteSpace(decision.targetId) &&
                !IsActivity(decision.activity, "wander") &&
                !string.Equals(decision.navigationKind, CreatureNavigationKind.Blocked.ToString(), System.StringComparison.OrdinalIgnoreCase);
            bool wantsInteraction =
                IsActivity(decision.activity, "interact") ||
                !string.IsNullOrWhiteSpace(decision.interactionId) ||
                !string.IsNullOrWhiteSpace(decision.actionId) ||
                shouldInspectArrivedTarget;
            if (!wantsInteraction)
            {
                return;
            }

            CreatureInteractable interactable = ResolveInteractable(decision);

            if (interactable == null)
            {
                if (TryExecuteSemanticTargetFallback(decision, out string semanticResult))
                {
                    LogDecision(
                        "互动成功",
                        "interact",
                        decision.targetName,
                        decision.destinationId,
                        decision.targetInterest,
                        currentIntent,
                        lastDialogue,
                        string.Empty);
                    memory.AppendEvent("interaction", semanticResult);
                    return;
                }

                memory.AppendEvent(
                    "interaction_failed",
                    $"No usable interactable was nearby. Requested interactionId={decision.interactionId}, actionId={decision.actionId}, targetId={decision.targetId}.");
                return;
            }

            string actionId = NormalizeActionId(decision.actionId);
            if (string.IsNullOrWhiteSpace(actionId))
            {
                actionId = interactable.PickActionId();
            }

            if (string.IsNullOrWhiteSpace(actionId))
            {
                memory.AppendEvent("interaction_failed", $"{interactable.displayName} has no usable actions.");
                return;
            }

            decision.interactionId = interactable.interactionId;
            decision.actionId = actionId;
            bool success = interactable.TryExecute(actionId, transform, interactionExecutionGrace, out string result);
            LogDecision(
                success ? "互动成功" : "互动失败",
                "interact",
                interactable.displayName,
                decision.destinationId,
                decision.targetInterest,
                currentIntent,
                lastDialogue,
                success ? string.Empty : result);
            memory.AppendEvent(success ? "interaction" : "interaction_failed", result);
        }

        private bool TryExecuteSemanticTargetFallback(CreatureDecision decision, out string result)
        {
            result = string.Empty;
            string targetId = FirstNonEmpty(decision.targetId, decision.interactionId, lastTargetId);
            if (string.IsNullOrWhiteSpace(targetId))
            {
                return false;
            }

            CreatureSemanticTarget[] candidates = semanticTargets != null && semanticTargets.Length > 0
                ? semanticTargets
                : FindSemanticTargets();
            CreatureSemanticTarget target = candidates
                .Where(candidate => candidate != null && MatchesSemanticTarget(candidate, targetId))
                .OrderBy(candidate => Vector3.Distance(transform.position, candidate.transform.position))
                .FirstOrDefault();
            if (target == null)
            {
                return false;
            }

            float distance = Vector3.Distance(transform.position, target.transform.position);
            float allowedDistance = Mathf.Max(interactionScanRadius, target.approachRadius + interactionExecutionGrace);
            if (distance > allowedDistance)
            {
                result = $"{target.displayName} is too far for semantic fallback interaction.";
                return false;
            }

            CreatureInteractionJumpMotion motion = target.GetComponent<CreatureInteractionJumpMotion>();
            if (motion == null)
            {
                motion = target.gameObject.AddComponent<CreatureInteractionJumpMotion>();
            }

            motion.Play(0.35f, 0.45f);
            result = $"{target.displayName} responded with a default semantic jump.";
            return true;
        }

        private void NormalizeInteractionFields(CreatureDecision decision)
        {
            decision.interactionId = ClearLlmPlaceholder(decision.interactionId);
            decision.actionId = NormalizeActionId(ClearLlmPlaceholder(decision.actionId));

            if (string.IsNullOrWhiteSpace(decision.actionId) && LooksLikeActionToken(decision.interactionId))
            {
                decision.actionId = NormalizeActionId(decision.interactionId);
                decision.interactionId = string.Empty;
            }
        }

        private CreatureInteractable ResolveInteractable(CreatureDecision decision)
        {
            CreatureInteractable[] all = FindObjectsByType<CreatureInteractable>(FindObjectsInactive.Exclude)
                .Where(candidate => candidate != null && candidate.isActiveAndEnabled)
                .ToArray();
            if (all.Length == 0)
            {
                return null;
            }

            string requestedId = FirstNonEmpty(decision.interactionId, decision.targetId, lastTargetId);
            if (!string.IsNullOrWhiteSpace(requestedId))
            {
                CreatureInteractable exact = all
                    .Where(candidate => MatchesInteractable(candidate, requestedId))
                    .OrderBy(candidate => Vector3.Distance(transform.position, candidate.transform.position))
                    .FirstOrDefault();
                if (exact != null)
                {
                    return exact;
                }
            }

            return all
                .Where(candidate =>
                    candidate.CanInteract(transform, interactionExecutionGrace) ||
                    Vector3.Distance(transform.position, candidate.transform.position) <= Mathf.Max(interactionScanRadius, candidate.interactionRadius + interactionExecutionGrace))
                .OrderBy(candidate => ActionMatchScore(candidate, decision.actionId))
                .ThenBy(candidate => Vector3.Distance(transform.position, candidate.transform.position))
                .FirstOrDefault();
        }

        private static bool MatchesInteractable(CreatureInteractable interactable, string id)
        {
            if (interactable == null || string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            if (string.Equals(interactable.interactionId, id, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(interactable.displayName, id, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(interactable.gameObject.name, id, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            CreatureSemanticTarget semanticTarget = interactable.GetComponent<CreatureSemanticTarget>();
            return semanticTarget != null &&
                   string.Equals(semanticTarget.targetId, id, System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesSemanticTarget(CreatureSemanticTarget target, string id)
        {
            return target != null &&
                   !string.IsNullOrWhiteSpace(id) &&
                   (string.Equals(target.targetId, id, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(target.displayName, id, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(target.gameObject.name, id, System.StringComparison.OrdinalIgnoreCase));
        }

        private static int ActionMatchScore(CreatureInteractable interactable, string actionId)
        {
            if (interactable == null || interactable.actions == null)
            {
                return 10;
            }

            if (string.IsNullOrWhiteSpace(actionId))
            {
                return 1;
            }

            return interactable.actions.Any(action =>
                action != null &&
                action.IsUsable &&
                string.Equals(action.actionId, actionId, System.StringComparison.OrdinalIgnoreCase))
                ? 0
                : 2;
        }

        private static string NormalizeActionId(string value)
        {
            value = ClearLlmPlaceholder(value);
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            int colon = value.IndexOf(':');
            string action = colon < 0 ? value : value.Substring(0, colon);
            return action.Trim('/', '\\', '"', '\'', ' ', '\t');
        }

        private static bool LooksLikeActionToken(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Contains(":");
        }

        private static string ClearLlmPlaceholder(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            string lower = trimmed.ToLowerInvariant();
            if (lower == "none" ||
                lower == "n/a" ||
                lower == "optional" ||
                lower == "optional listed id" ||
                lower == "optional listed action id" ||
                lower.Contains("optional listed"))
            {
                return string.Empty;
            }

            return trimmed;
        }

        private bool DecisionAlreadyReferencesGoal(CreatureDecision decision, MovementGoal goal)
        {
            if (decision == null || goal == null)
            {
                return false;
            }

            return string.Equals(decision.destinationId, goal.Id, System.StringComparison.Ordinal) ||
                   string.Equals(decision.approachPointId, goal.Id, System.StringComparison.Ordinal) ||
                   (!string.IsNullOrWhiteSpace(goal.RegionId) &&
                    string.Equals(decision.regionId, goal.RegionId, System.StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(goal.SemanticTargetId) &&
                    string.Equals(decision.targetId, goal.SemanticTargetId, System.StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(decision.interactionId) &&
                    string.Equals(decision.interactionId, goal.SemanticTargetId, System.StringComparison.OrdinalIgnoreCase));
        }

        private float GetDwellSeconds(CreatureDecision decision)
        {
            float dwell = decision == null ? 1.5f : decision.dwellSeconds;
            return Mathf.Clamp(dwell, 0.8f, Mathf.Max(0.8f, maximumDwellSeconds));
        }

        private float GetDecisionInterval()
        {
            float configured = decisionIntervalSeconds > 0f ? decisionIntervalSeconds : config.decisionIntervalSeconds;
            return Mathf.Clamp(configured, 0.2f, 12f);
        }

        private Transform PickLookTarget()
        {
            if (interactables != null && interactables.Length > 0)
            {
                return interactables
                    .OrderBy(target => Vector3.Distance(transform.position, target.transform.position))
                    .First()
                    .transform;
            }

            if (semanticTargets != null && semanticTargets.Length > 0)
            {
                return semanticTargets
                    .OrderByDescending(target => Mathf.Max(1, target.interestWeight))
                    .ThenBy(target => Vector3.Distance(transform.position, target.transform.position))
                    .First()
                    .transform;
            }

            if (regions != null && regions.Length > 0)
            {
                return regions
                    .OrderByDescending(region => Mathf.Max(1, region.priority))
                    .ThenBy(region => Vector3.Distance(transform.position, region.transform.position))
                    .First()
                    .transform;
            }

            return transform;
        }

        private void TurnToward(Vector3 point, float speed)
        {
            Vector3 direction = point - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f)
            {
                return;
            }

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(direction.normalized),
                Time.deltaTime * Mathf.Max(0.1f, speed));
        }

        private void ShowSubtitle(string dialogue, string intent, float seconds)
        {
            if (string.IsNullOrWhiteSpace(dialogue) && string.IsNullOrWhiteSpace(intent))
            {
                return;
            }

            float visibleSeconds = Mathf.Max(
                Mathf.Max(1f, minimumSubtitleVisibleSeconds),
                Mathf.Max(0f, seconds) + Mathf.Max(0f, subtitleExtraVisibleSeconds));
            CreatureSubtitleHud.Show(ResolveDisplayName(), dialogue, intent, visibleSeconds);
        }

        private void SetStatus(string status, Color color, string activity)
        {
            lastStatus = status;
            if (!string.IsNullOrWhiteSpace(activity))
            {
                lastActivity = activity;
            }

            idleMotion = idleMotion != null ? idleMotion : GetComponent<CreatureIdleMotion>();
            if (idleMotion != null)
            {
                idleMotion.SetActivity(activity);
            }

            CreatureSubtitleHud.SetStatus(status, activity);
        }

        private void LogDecision(string status, string activity, string target, string destination, string reason, string dialogue, string error)
        {
            LogDecision(status, activity, target, destination, string.Empty, reason, dialogue, error);
        }

        private void LogDecision(string status, string activity, string target, string destination, string targetInterest, string reason, string dialogue, string error)
        {
            CreatureAgentDecisionLog.Add(this, status, activity, target, destination, lastRegionId, targetInterest, reason, dialogue, error, lastLlmLatencyMs);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string StatusForActivity(string activity)
        {
            if (IsActivity(activity, "roll"))
            {
                return "玩耍中";
            }

            if (IsActivity(activity, "rest") || IsActivity(activity, "idle"))
            {
                return "休息中";
            }

            if (IsActivity(activity, "wander"))
            {
                return "散步中";
            }

            return "观察中";
        }

        private static Color ColorForActivity(string activity)
        {
            if (IsActivity(activity, "roll"))
            {
                return new Color(1f, 0.45f, 0.72f, 0.94f);
            }

            if (IsActivity(activity, "rest") || IsActivity(activity, "idle"))
            {
                return new Color(0.2f, 0.65f, 1f, 0.94f);
            }

            if (IsActivity(activity, "wander"))
            {
                return new Color(0.22f, 0.72f, 0.36f, 0.94f);
            }

            return new Color(0.45f, 0.55f, 0.66f, 0.94f);
        }

        private string BuildDisplayDialogue(CreatureDecision decision, MovementGoal destination)
        {
            string line = CleanModelTextForDisplay(BuildSpokenLine(decision, destination));
            if (string.IsNullOrWhiteSpace(line))
            {
                line = PickDialogueFallback(destination);
            }

            if (WasRecentlyUsed(recentDialogueKeys, line))
            {
                line = PickDialogueFallback(destination);
            }

            RememberText(recentDialogueKeys, line);
            return line;
        }

        private string BuildDisplayIntent(CreatureDecision decision, string visibleDialogue)
        {
            string intent = CleanModelTextForDisplay(FirstNonEmpty(
                decision == null ? string.Empty : decision.intent,
                decision == null ? string.Empty : decision.targetInterest,
                decision == null ? string.Empty : decision.memoryNote));
            if (string.IsNullOrWhiteSpace(intent))
            {
                intent = PickIntentFallback();
            }

            if (string.Equals(NormalizeTextKey(intent), NormalizeTextKey(visibleDialogue), System.StringComparison.Ordinal) ||
                WasRecentlyUsed(recentIntentKeys, intent))
            {
                intent = PickIntentFallback();
            }

            RememberText(recentIntentKeys, intent);
            return intent;
        }

        private string PickDialogueFallback(MovementGoal destination)
        {
            string[] lines;
            if (destination != null && !string.IsNullOrWhiteSpace(destination.RegionId))
            {
                lines = new[]
                {
                    "我换到这片区域的另一处位置继续观察。",
                    "这片区域像一页折起的星图，我再沿边缘走一段。",
                    "我让脚步慢一点，听这片地面里的微弱回声。"
                };
            }
            else if (destination != null && !string.IsNullOrWhiteSpace(destination.SemanticTargetId))
            {
                lines = new[]
                {
                    "我绕到这个目标旁边，听听它有没有星图回声。",
                    "这个目标的轮廓不普通，我想换一个侧面确认。",
                    "我靠近一点，让传感器读清它的影子。"
                };
            }
            else
            {
                lines = new[]
                {
                    "我换个角度继续观测星光。",
                    "这处结构还在呼唤我，我要换一种看法。",
                    "我把星图天线转向另一束微光。",
                    "这里的轮廓像远方轨道，我再靠近一点确认。"
                };
            }

            string line = lines[dialogueFallbackIndex % lines.Length];
            dialogueFallbackIndex++;
            return line;
        }

        private string PickIntentFallback()
        {
            string[] lines =
            {
                "重复的信号不够可靠，我要寻找新的星图线索。",
                "同一束回声已经记录过了，我需要换个观察角度。",
                "我正在把场景轮廓和失散星图重新对齐。",
                "如果这里藏着返航坐标，它不会只用一种方式发光。"
            };

            string line = lines[intentFallbackIndex % lines.Length];
            intentFallbackIndex++;
            return line;
        }

        private static bool WasRecentlyUsed(Queue<string> recentKeys, string text)
        {
            string key = NormalizeTextKey(text);
            return !string.IsNullOrWhiteSpace(key) && recentKeys.Contains(key);
        }

        private static void RememberText(Queue<string> recentKeys, string text)
        {
            string key = NormalizeTextKey(text);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            recentKeys.Enqueue(key);
            while (recentKeys.Count > 6)
            {
                recentKeys.Dequeue();
            }
        }

        private static string NormalizeTextKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (IsChinese(c) || char.IsDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        private static string CleanModelTextForDisplay(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string source = ExtractBestJsonText(value);
            if (string.IsNullOrWhiteSpace(source))
            {
                source = value;
            }

            source = source
                .Replace("\\n", " ")
                .Replace("\\r", " ")
                .Replace("\n", " ")
                .Replace("\r", " ")
                .Replace("　", " ")
                .Replace("...", "……");

            if (CountChinese(source) == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(source.Length);
            foreach (char c in source)
            {
                if (IsChinese(c) || char.IsDigit(c))
                {
                    builder.Append(c);
                }
                else if (char.IsWhiteSpace(c))
                {
                    builder.Append(' ');
                }
                else
                {
                    char mapped = MapSubtitlePunctuation(c);
                    if (mapped != '\0')
                    {
                        builder.Append(mapped);
                    }
                }
            }

            string cleaned = CollapseSubtitleSpaces(builder.ToString());
            return cleaned.Trim(' ', '，', '。', '！', '？', '、', '；', '：', '…');
        }

        private static string ExtractBestJsonText(string value)
        {
            string[] keys =
            {
                "Chinese",
                "chinese",
                "zh",
                "speech",
                "content",
                "string",
                "display",
                "reason",
                "intent",
                "dialogue",
                "text",
                "message",
                "note",
                "memoryNote",
                "targetName",
                "name",
                "position"
            };

            string best = string.Empty;
            int bestScore = 0;
            foreach (string key in keys)
            {
                string candidate = LlmResponseParser.ExtractJsonStringValue(value, key);
                int score = CountChinese(candidate);
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private static int CountChinese(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            int count = 0;
            foreach (char c in value)
            {
                if (IsChinese(c))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsChinese(char c)
        {
            return (c >= '\u3400' && c <= '\u4DBF') ||
                   (c >= '\u4E00' && c <= '\u9FFF') ||
                   (c >= '\uF900' && c <= '\uFAFF');
        }

        private static char MapSubtitlePunctuation(char c)
        {
            switch (c)
            {
                case '，':
                case '。':
                case '！':
                case '？':
                case '、':
                case '；':
                case '：':
                case '…':
                    return c;
                case ',':
                    return '，';
                case '.':
                    return '。';
                case '!':
                    return '！';
                case '?':
                    return '？';
                case ';':
                    return '；';
                default:
                    return '\0';
            }
        }

        private static string CollapseSubtitleSpaces(string value)
        {
            string collapsed = value;
            while (collapsed.Contains("  "))
            {
                collapsed = collapsed.Replace("  ", " ");
            }

            string[] beforePunctuation = { " ，", " 。", " ！", " ？", " 、", " ；", " ：", " …" };
            string[] afterPunctuation = { "，", "。", "！", "？", "、", "；", "：", "…" };
            for (int i = 0; i < beforePunctuation.Length; i++)
            {
                collapsed = collapsed.Replace(beforePunctuation[i], afterPunctuation[i]);
            }

            return collapsed;
        }

        private static string BuildSpokenLine(CreatureDecision decision, MovementGoal destination)
        {
            if (decision == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(decision.dialogue))
            {
                return decision.dialogue;
            }

            if (destination == null)
            {
                return "我去校准星图，也许下一束光会指出方向。";
            }

            if (!string.IsNullOrWhiteSpace(destination.RegionId))
            {
                return "我去那片区域观星，看看地面和天空能不能重新对齐。";
            }

            return "我靠近那里测量星光，像在寻找回家的坐标。";
        }

        private MovementGoal ResolveObjectGoal(CreatureDecision decision)
        {
            if (decision == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(decision.interactionId) && interactables != null)
            {
                CreatureInteractable interactable = interactables.FirstOrDefault(candidate =>
                    string.Equals(candidate.interactionId, decision.interactionId, System.StringComparison.OrdinalIgnoreCase));
                MovementGoal interactableGoal = BuildGoalNearInteractable(interactable);
                if (interactableGoal != null)
                {
                    return interactableGoal;
                }
            }

            if (!string.IsNullOrWhiteSpace(decision.targetId) && semanticTargets != null)
            {
                CreatureSemanticTarget target = semanticTargets.FirstOrDefault(candidate =>
                    string.Equals(candidate.targetId, decision.targetId, System.StringComparison.OrdinalIgnoreCase));
                MovementGoal semanticGoal = BuildGoalNearSemanticTarget(target);
                if (semanticGoal != null)
                {
                    return semanticGoal;
                }
            }

            return null;
        }

        private MovementGoal ResolveRegionGoal(CreatureDecision decision, float radiusScale, string source)
        {
            if (decision == null || string.IsNullOrWhiteSpace(decision.regionId) || regions == null)
            {
                return null;
            }

            CreatureSemanticRegion region = regions.FirstOrDefault(candidate =>
                string.Equals(candidate.regionId, decision.regionId, System.StringComparison.OrdinalIgnoreCase));
            return BuildGoalInsideRegion(region, radiusScale, source);
        }

        private MovementGoal ResolveMarkerGoalForTarget(CreatureDecision decision)
        {
            if (decision == null || string.IsNullOrWhiteSpace(decision.targetId) || semanticTargets == null)
            {
                return null;
            }

            CreatureSemanticTarget target = semanticTargets.FirstOrDefault(candidate =>
                string.Equals(candidate.targetId, decision.targetId, System.StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                return null;
            }

            CreatureLocationMarker linked = target.FirstReachableMarker();
            if (linked != null && locations != null && locations.Contains(linked) && HasCompletePath(linked.transform.position))
            {
                memory.AppendEvent("decision_fallback", $"LLM chose target '{target.targetId}' without a valid destination; using linked marker '{linked.id}'.");
                decision.destinationId = linked.id;
                decision.approachPointId = linked.id;
                return MovementGoal.FromMarker(linked);
            }

            if (locations == null || locations.Length == 0)
            {
                return null;
            }

            CreatureLocationMarker closest = locations
                .Where(location => HasCompletePath(location.transform.position))
                .OrderBy(location => Vector3.Distance(location.transform.position, target.transform.position))
                .FirstOrDefault();
            if (closest != null)
            {
                memory.AppendEvent("decision_fallback", $"Target '{target.targetId}' had no linked marker; using closest marker '{closest.id}'.");
                decision.destinationId = closest.id;
                decision.approachPointId = closest.id;
            }

            return closest == null ? null : MovementGoal.FromMarker(closest);
        }

        private MovementGoal BuildGoalNearSemanticTarget(CreatureSemanticTarget target)
        {
            return BuildGoalNearSemanticTarget(target, 1f, "semantic target");
        }

        private MovementGoal BuildGoalNearSemanticTarget(CreatureSemanticTarget target, float radiusScale, string source)
        {
            if (target == null)
            {
                return null;
            }

            if (target.navigationKind == CreatureNavigationKind.Blocked)
            {
                CreatureLocationMarker viewpoint = target.FirstReachableMarker();
                if (viewpoint != null && locations != null && locations.Contains(viewpoint) && HasCompletePath(viewpoint.transform.position))
                {
                    return MovementGoal.FromMarker(viewpoint, "blocked target viewpoint marker");
                }
            }

            float maxRadius = Mathf.Max(2f, target.approachRadius * Mathf.Max(0.5f, radiusScale) + 1.5f);
            if (TrySampleNavAround(target.transform.position, 0.6f, maxRadius, out Vector3 targetPosition))
            {
                return new MovementGoal(
                    "near_" + target.targetId + "_" + Time.frameCount,
                    target.displayName,
                    target.targetId,
                    string.Empty,
                    target.navigationKind,
                    targetPosition,
                    target.transform,
                    false,
                    source);
            }

            if (target.linkedMarkers != null)
            {
                foreach (CreatureLocationMarker marker in target.linkedMarkers.Where(marker => marker != null))
                {
                    if (TrySampleNavAround(marker.transform.position, 0.2f, Mathf.Max(2f, target.approachRadius * Mathf.Max(0.5f, radiusScale)), out Vector3 markerPosition))
                    {
                        return new MovementGoal(
                            "near_" + target.targetId + "_" + Time.frameCount,
                            target.displayName,
                            target.targetId,
                            string.Empty,
                            target.navigationKind,
                            markerPosition,
                            target.transform,
                            false,
                            source + " via linked marker sample");
                    }

                    if (HasCompletePath(marker.transform.position))
                    {
                        return MovementGoal.FromMarker(marker, "semantic target linked marker fallback");
                    }
                }
            }

            CreatureLocationMarker fallbackMarker = target.FirstReachableMarker();
            return fallbackMarker == null || locations == null || !locations.Contains(fallbackMarker) || !HasCompletePath(fallbackMarker.transform.position)
                ? null
                : MovementGoal.FromMarker(fallbackMarker, "semantic target fixed marker fallback");
        }

        private MovementGoal BuildGoalInsideRegion(CreatureSemanticRegion region, float radiusScale, string source)
        {
            if (region == null ||
                !region.TrySampleNavPoint(transform.position, radiusScale, out Vector3 position))
            {
                return null;
            }

            return new MovementGoal(
                "region_" + region.regionId + "_" + Time.frameCount,
                region.displayName,
                string.Empty,
                region.regionId,
                CreatureNavigationKind.Walkable,
                position,
                region.transform,
                false,
                source);
        }

        private MovementGoal BuildWeightedRegionGoal(float radiusScale, string source)
        {
            if (regions == null || regions.Length == 0)
            {
                return null;
            }

            List<CreatureSemanticRegion> candidates = regions
                .Where(region => region != null && !string.IsNullOrWhiteSpace(region.regionId))
                .ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            for (int attempt = 0; attempt < Mathf.Max(8, candidates.Count * 2); attempt++)
            {
                CreatureSemanticRegion picked = PickWeightedRegion(candidates);
                if (picked == null)
                {
                    continue;
                }

                if (attempt < 4 &&
                    candidates.Count > 1 &&
                    string.Equals(picked.regionId, lastRegionId, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MovementGoal goal = BuildGoalInsideRegion(picked, radiusScale, source);
                if (goal != null)
                {
                    return goal;
                }
            }

            foreach (CreatureSemanticRegion region in candidates
                         .OrderBy(region => string.Equals(region.regionId, lastRegionId, System.StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                         .ThenByDescending(region => Mathf.Max(1, region.priority)))
            {
                MovementGoal goal = BuildGoalInsideRegion(region, radiusScale, source + " fallback");
                if (goal != null)
                {
                    return goal;
                }
            }

            return null;
        }

        private static CreatureSemanticRegion PickWeightedRegion(IReadOnlyList<CreatureSemanticRegion> candidates)
        {
            int totalWeight = candidates.Sum(region => Mathf.Max(1, region.priority));
            int roll = Random.Range(0, Mathf.Max(1, totalWeight));
            foreach (CreatureSemanticRegion region in candidates)
            {
                roll -= Mathf.Max(1, region.priority);
                if (roll < 0)
                {
                    return region;
                }
            }

            return candidates.Count == 0 ? null : candidates[0];
        }

        private MovementGoal BuildGoalNearInteractable(CreatureInteractable interactable)
        {
            if (interactable == null)
            {
                return null;
            }

            float radius = Mathf.Max(0.8f, interactable.interactionRadius * 0.85f);
            if (!TrySampleNavAround(interactable.transform.position, 0.25f, radius, out Vector3 position))
            {
                return null;
            }

            return new MovementGoal(
                "near_" + interactable.interactionId + "_" + Time.frameCount,
                interactable.displayName,
                interactable.interactionId,
                string.Empty,
                CreatureNavigationKind.InteractOnly,
                position,
                interactable.transform,
                false,
                "near interactable");
        }

        private MovementGoal BuildWanderGoal()
        {
            if (!TrySampleNavAround(transform.position, 2f, Mathf.Max(2.5f, wanderRadius), out Vector3 position))
            {
                return null;
            }

            return new MovementGoal(
                "wander_" + Time.frameCount,
                "自由散步点",
                string.Empty,
                string.Empty,
                CreatureNavigationKind.Walkable,
                position,
                PickLookTarget(),
                true,
                "wander NavMesh sample");
        }

        private bool TrySampleNavAround(Vector3 center, float minRadius, float maxRadius, out Vector3 position)
        {
            position = center;
            NavMeshPath path = new NavMeshPath();
            for (int i = 0; i < 28; i++)
            {
                float radius = Random.Range(Mathf.Max(0f, minRadius), Mathf.Max(minRadius + 0.1f, maxRadius));
                Vector2 circle = Random.insideUnitCircle.normalized * radius;
                if (circle.sqrMagnitude <= 0.001f)
                {
                    circle = Random.insideUnitCircle * radius;
                }

                Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                {
                    continue;
                }

                if (NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, path) &&
                    path.status == NavMeshPathStatus.PathComplete)
                {
                    position = hit.position;
                    return true;
                }
            }

            if (NavMesh.SamplePosition(center, out NavMeshHit centerHit, 2.5f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(transform.position, centerHit.position, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete)
            {
                position = centerHit.position;
                return true;
            }

            return false;
        }

        private bool HasCompletePath(Vector3 position)
        {
            NavMeshPath path = new NavMeshPath();
            return NavMesh.CalculatePath(transform.position, position, NavMesh.AllAreas, path) &&
                   path.status == NavMeshPathStatus.PathComplete;
        }

        private void RecordSemanticChoice(CreatureDecision decision, MovementGoal destination)
        {
            lastTargetId = GetSemanticTargetKey(decision, destination);
            lastRegionId = GetRegionKey(decision, destination);
            lastTargetInterest = decision == null ? string.Empty : decision.targetInterest;

            if (decision != null && !string.IsNullOrWhiteSpace(lastRegionId))
            {
                decision.regionId = lastRegionId;
            }

            if (string.IsNullOrWhiteSpace(lastTargetId) || semanticTargets == null)
            {
                return;
            }

            CreatureSemanticTarget target = semanticTargets.FirstOrDefault(candidate =>
                string.Equals(candidate.targetId, lastTargetId, System.StringComparison.OrdinalIgnoreCase));
            if (target != null && decision != null)
            {
                decision.targetId = target.targetId;
                decision.targetName = target.displayName;
                target.RecordSelection(lastTargetInterest);
            }
        }

        private static string GetSemanticTargetKey(CreatureDecision decision, MovementGoal destination)
        {
            if (destination != null && !string.IsNullOrWhiteSpace(destination.SemanticTargetId))
            {
                return destination.SemanticTargetId;
            }

            return decision == null ? string.Empty : decision.targetId;
        }

        private static string GetRegionKey(CreatureDecision decision, MovementGoal destination)
        {
            if (destination != null && !string.IsNullOrWhiteSpace(destination.RegionId))
            {
                return destination.RegionId;
            }

            return decision == null ? string.Empty : decision.regionId;
        }

        private static string DescribeLocation(CreatureLocationMarker marker)
        {
            string line = marker.ToPromptLine();
            if (NavMesh.SamplePosition(marker.transform.position, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
            {
                line += $" | sampledNavArea={AreaMaskToName(hit.mask)}";
            }
            else
            {
                line += " | sampledNavArea=unreachable";
            }

            return line;
        }

        private static string GetNavAreaSummary()
        {
            List<string> names = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                string areaName = KnownNavAreaName(i);
                if (!string.IsNullOrWhiteSpace(areaName))
                {
                    names.Add($"{i}:{areaName}");
                }
            }

            return names.Count == 0 ? "No named NavMesh areas." : string.Join(", ", names);
        }

        private static string AreaMaskToName(int mask)
        {
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0)
                {
                    continue;
                }

                string areaName = KnownNavAreaName(i);
                if (!string.IsNullOrWhiteSpace(areaName))
                {
                    return areaName;
                }
            }

            return "Unknown";
        }

        private sealed class MovementGoal
        {
            public MovementGoal(
                string id,
                string displayName,
                string semanticTargetId,
                string regionId,
                CreatureNavigationKind navigationKind,
                Vector3 position,
                Transform lookAt,
                bool isWander,
                string sourceDescription)
            {
                Id = string.IsNullOrWhiteSpace(id) ? "runtime_goal" : id;
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
                SemanticTargetId = semanticTargetId;
                RegionId = regionId;
                NavigationKind = navigationKind;
                Position = position;
                LookAt = lookAt;
                IsWander = isWander;
                SourceDescription = string.IsNullOrWhiteSpace(sourceDescription) ? "unknown" : sourceDescription;
            }

            public string Id { get; }
            public string DisplayName { get; }
            public string SemanticTargetId { get; }
            public string RegionId { get; }
            public CreatureNavigationKind NavigationKind { get; }
            public Vector3 Position { get; }
            public Transform LookAt { get; }
            public bool IsWander { get; }
            public string SourceDescription { get; }

            public static MovementGoal FromMarker(CreatureLocationMarker marker, string sourceDescription = "fixed marker")
            {
                return marker == null
                    ? null
                    : new MovementGoal(
                        marker.id,
                        marker.displayName,
                        marker.semanticTargetId,
                        string.Empty,
                        marker.navigationKind,
                        marker.transform.position,
                        marker.transform,
                        false,
                        sourceDescription);
            }
        }

        private static string KnownNavAreaName(int areaIndex)
        {
            switch (areaIndex)
            {
                case 0:
                    return "Walkable";
                case 1:
                    return "Not Walkable";
                case 2:
                    return "Jump";
                default:
                    return string.Empty;
            }
        }

        private static string ModeToPromptValue(CreatureAgentMode mode)
        {
            return mode == CreatureAgentMode.Dialogue ? "dialogue" : "move";
        }

        private string ResolveCreatureDataPath()
        {
            if (!string.IsNullOrWhiteSpace(creatureDataPath))
            {
                string configuredPath = ResolveProjectPath(creatureDataPath);
                if (Directory.Exists(configuredPath))
                {
                    return configuredPath;
                }

                string samplePath = ResolveSampleDataPathNearActiveScene(creatureDataPath);
                if (!string.IsNullOrWhiteSpace(samplePath))
                {
                    return samplePath;
                }

                return configuredPath;
            }

            string folderName = profile == null ? CreatureProfile.SanitizePathSegment(gameObject.name) : profile.DataFolderName;
#if UNITY_EDITOR
            return ResolveProjectPath(Path.Combine("Assets", "DigiCreaturesData", folderName));
#else
            if (profile != null && !profile.usePersistentDataPathInPlayer)
            {
                return Path.Combine(Application.dataPath, "DigiCreaturesData", folderName);
            }

            return Path.Combine(Application.persistentDataPath, "DigiCreatures", folderName);
#endif
        }

        private string ResolveDisplayName()
        {
            string soulName = FirstNonEmpty(
                ExtractSoulFrontMatter(memory == null ? string.Empty : memory.Soul, "subtitleName"),
                ExtractSoulFrontMatter(memory == null ? string.Empty : memory.Soul, "displayName"));
            if (!string.IsNullOrWhiteSpace(soulName))
            {
                resolvedSubtitleName = soulName;
                return soulName;
            }

            if (profile != null)
            {
                string profileName = profile.SubtitleName;
                resolvedSubtitleName = profileName;
                return profileName;
            }

            if (!string.IsNullOrWhiteSpace(resolvedSubtitleName))
            {
                return resolvedSubtitleName;
            }

            return string.IsNullOrWhiteSpace(gameObject.name) ? "数字生物" : gameObject.name;
        }

        private static string ExtractSoulFrontMatter(string soulText, string key)
        {
            if (string.IsNullOrWhiteSpace(soulText) || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            using StringReader reader = new StringReader(soulText);
            string first = reader.ReadLine();
            bool yamlBlock = string.Equals(first?.Trim(), "---", System.StringComparison.Ordinal);
            string line = yamlBlock ? reader.ReadLine() : first;
            while (line != null)
            {
                string trimmed = line.Trim();
                if (yamlBlock && string.Equals(trimmed, "---", System.StringComparison.Ordinal))
                {
                    break;
                }

                if (trimmed.StartsWith("#", System.StringComparison.Ordinal))
                {
                    if (!yamlBlock)
                    {
                        break;
                    }

                    line = reader.ReadLine();
                    continue;
                }

                int colon = trimmed.IndexOf(':');
                if (colon > 0)
                {
                    string currentKey = trimmed.Substring(0, colon).Trim();
                    if (string.Equals(currentKey, key, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return trimmed.Substring(colon + 1).Trim().Trim('"', '\'');
                    }
                }

                if (!yamlBlock && !string.IsNullOrWhiteSpace(trimmed))
                {
                    break;
                }

                line = reader.ReadLine();
            }

            return string.Empty;
        }

        private static string ResolveProjectPath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        }

        private static string ResolveSampleDataPathNearActiveScene(string configuredPath)
        {
            string scenePath = SceneManager.GetActiveScene().path;
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                return string.Empty;
            }

            string folderName = Path.GetFileName(configuredPath.TrimEnd('/', '\\'));
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return string.Empty;
            }

            string current = Path.GetDirectoryName(scenePath);
            for (int i = 0; i < 5 && !string.IsNullOrWhiteSpace(current); i++)
            {
                string candidateAssetPath = Path.Combine(current, "Creatures", folderName).Replace("\\", "/");
                string candidateFullPath = ResolveProjectPath(candidateAssetPath);
                if (Directory.Exists(candidateFullPath))
                {
                    return candidateFullPath;
                }

                current = Path.GetDirectoryName(current);
            }

            return string.Empty;
        }

        private static string SafeMemoryFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "test-memory.jsonl";
            }

            string safe = Path.GetFileName(fileName);
            return string.IsNullOrWhiteSpace(safe) ? "test-memory.jsonl" : safe;
        }
    }

    public enum CreatureAgentMode
    {
        Move,
        Dialogue
    }
}
