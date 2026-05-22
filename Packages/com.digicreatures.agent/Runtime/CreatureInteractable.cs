using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace DigiCreatures
{
    public class CreatureInteractable : MonoBehaviour
    {
        public string interactionId = "interactable";
        public string displayName = "可互动对象";
        [TextArea] public string description = "一个可以被智能体互动的物体。";
        public string tags = "neutral";
        public float interactionRadius = 2f;
        public List<CreatureInteractionAction> actions = new List<CreatureInteractionAction>
        {
            new CreatureInteractionAction { actionId = "inspect", actionType = CreatureInteractionActionType.CustomEvent }
        };

        public string ToPromptLine(Transform creature)
        {
            float distance = creature == null ? 0f : Vector3.Distance(creature.position, transform.position);
            string actionList = string.Join(", ", actions.Where(action => action != null && action.IsUsable)
                .Select(action => action.ToPromptToken()));
            return $"{interactionId}: {displayName} | tags={tags} | distance={distance:0.0} | radius={interactionRadius:0.0} | actions={actionList} | {description}";
        }

        public bool CanInteract(Transform creature)
        {
            return CanInteract(creature, 0f);
        }

        public bool CanInteract(Transform creature, float extraDistance)
        {
            return creature == null ||
                   Vector3.Distance(creature.position, transform.position) <=
                   Mathf.Max(0.05f, interactionRadius + Mathf.Max(0f, extraDistance));
        }

        public bool TryExecute(string actionId, Transform creature, out string result)
        {
            return TryExecute(actionId, creature, 0f, out result);
        }

        public bool TryExecute(string actionId, Transform creature, float extraDistance, out string result)
        {
            if (!CanInteract(creature, extraDistance))
            {
                result = $"{displayName} is outside interaction radius.";
                return false;
            }

            CreatureInteractionAction action = actions.FirstOrDefault(candidate =>
                candidate != null &&
                candidate.IsUsable &&
                string.Equals(candidate.actionId, actionId, StringComparison.OrdinalIgnoreCase));

            if (action == null)
            {
                result = $"No enabled action named '{actionId}' on {displayName}.";
                return false;
            }

            action.Execute(gameObject);
            result = $"{displayName} executed {action.actionId}.";
            return true;
        }

        public string PickActionId()
        {
            string[] preferred =
            {
                "jump_animation",
                "move_object",
                "spawn_prefab",
                "inspect"
            };

            foreach (string preferredId in preferred)
            {
                CreatureInteractionAction preferredAction = actions.FirstOrDefault(action =>
                    action != null &&
                    action.IsUsable &&
                    string.Equals(action.actionId, preferredId, StringComparison.OrdinalIgnoreCase));
                if (preferredAction != null)
                {
                    return preferredAction.actionId;
                }
            }

            return actions.FirstOrDefault(action => action != null && action.IsUsable)?.actionId ?? string.Empty;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.65f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.1f, interactionRadius));
        }
    }

    public enum CreatureInteractionActionType
    {
        DestroySelf = 0,
        SpawnPrefab = 1,
        MoveObject = 2,
        CustomEvent = 3,
        PlayAnimation = 4
    }

    [Serializable]
    public class CreatureInteractionAction
    {
        public string actionId = "inspect";
        public CreatureInteractionActionType actionType = CreatureInteractionActionType.CustomEvent;
        public bool enabled = true;
        public GameObject prefabToSpawn;
        public Transform spawnPoint;
        public Transform moveTarget;
        public Vector3 moveOffset = Vector3.up;
        public AnimationClip animationClip;
        public float jumpHeight = 0.6f;
        public float jumpDuration = 0.55f;
        public UnityEvent customEvent;

        public bool Enabled => enabled && !string.IsNullOrWhiteSpace(actionId);

        public bool IsUsable
        {
            get
            {
                if (!Enabled)
                {
                    return false;
                }

                return actionType != CreatureInteractionActionType.SpawnPrefab || prefabToSpawn != null;
            }
        }

        public string ToPromptToken()
        {
            return $"{actionId}:{actionType}";
        }

        public void Execute(GameObject owner)
        {
            switch (actionType)
            {
                case CreatureInteractionActionType.DestroySelf:
                    UnityEngine.Object.Destroy(owner);
                    break;
                case CreatureInteractionActionType.SpawnPrefab:
                    Spawn(owner);
                    break;
                case CreatureInteractionActionType.MoveObject:
                    Move(owner);
                    break;
                case CreatureInteractionActionType.PlayAnimation:
                    PlayAnimation(owner);
                    break;
                case CreatureInteractionActionType.CustomEvent:
                    bool hasPersistentListeners = customEvent != null && customEvent.GetPersistentEventCount() > 0;
                    customEvent?.Invoke();
                    if (!hasPersistentListeners)
                    {
                        PlayDefaultResponse(owner);
                    }
                    break;
            }
        }

        private void Spawn(GameObject owner)
        {
            if (prefabToSpawn == null)
            {
                return;
            }

            Transform source = spawnPoint != null ? spawnPoint : owner.transform;
            UnityEngine.Object.Instantiate(prefabToSpawn, source.position, source.rotation);
        }

        private void Move(GameObject owner)
        {
            if (moveTarget != null)
            {
                owner.transform.position = moveTarget.position;
                owner.transform.rotation = moveTarget.rotation;
                return;
            }

            owner.transform.position += moveOffset;
        }

        private void PlayAnimation(GameObject owner)
        {
            if (animationClip != null)
            {
                Animation animation = owner.GetComponent<Animation>();
                if (animation == null)
                {
                    animation = owner.AddComponent<Animation>();
                }

                animationClip.legacy = true;
                animation.AddClip(animationClip, animationClip.name);
                animation.Play(animationClip.name);
                return;
            }

            CreatureInteractionJumpMotion motion = owner.GetComponent<CreatureInteractionJumpMotion>();
            if (motion == null)
            {
                motion = owner.AddComponent<CreatureInteractionJumpMotion>();
            }

            motion.Play(jumpHeight, jumpDuration);
        }

        private void PlayDefaultResponse(GameObject owner)
        {
            CreatureInteractionJumpMotion motion = owner.GetComponent<CreatureInteractionJumpMotion>();
            if (motion == null)
            {
                motion = owner.AddComponent<CreatureInteractionJumpMotion>();
            }

            float height = jumpHeight > 0f ? Mathf.Min(jumpHeight, 0.45f) : 0.25f;
            float duration = jumpDuration > 0f ? Mathf.Min(jumpDuration, 0.6f) : 0.4f;
            motion.Play(height, duration);
        }
    }

    public class CreatureInteractionJumpMotion : MonoBehaviour
    {
        private Coroutine routine;
        private Vector3 basePosition;

        public void Play(float height, float duration)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                transform.position = basePosition;
            }

            basePosition = transform.position;
            routine = StartCoroutine(Jump(Mathf.Max(0.05f, height), Mathf.Max(0.1f, duration)));
        }

        private System.Collections.IEnumerator Jump(float height, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float y = Mathf.Sin(t * Mathf.PI) * height;
                transform.position = basePosition + Vector3.up * y;
                yield return null;
            }

            transform.position = basePosition;
            routine = null;
        }
    }
}
