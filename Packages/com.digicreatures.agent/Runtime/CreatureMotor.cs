using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace DigiCreatures
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class CreatureMotor : MonoBehaviour
    {
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private float visualYawOffsetDegrees;
        [SerializeField] private float walkSpeed = 1.8f;
        [SerializeField] private float runSpeed = 3.8f;
        [SerializeField] private float arriveDistance = 0.35f;
        [SerializeField] private float directMoveTurnSpeed = 8f;
        [SerializeField] private float maxMoveSeconds = 24f;
        [SerializeField] private float stuckTimeoutSeconds = 4f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");

        private Vector3 directTarget;
        private bool directMoveActive;
        private float directSpeed;
        private Quaternion visualBaseLocalRotation = Quaternion.identity;
        private bool hasVisualBaseRotation;

        public bool IsMoving { get; private set; }
        public bool LastMoveSucceeded { get; private set; }
        public string LastMoveError { get; private set; }

        private void Awake()
        {
            agent = agent != null ? agent : GetComponent<NavMeshAgent>();
            animator = animator != null ? animator : GetComponentInChildren<Animator>();
            visualRoot = visualRoot != null ? visualRoot : animator == null ? null : animator.transform;
            CacheVisualBaseRotation();
            agent.updateRotation = true;
            agent.stoppingDistance = arriveDistance;
            ApplyVisualYawCorrection();
        }

        private void Update()
        {
            if (directMoveActive)
            {
                UpdateDirectMove();
            }

            float speed = agent != null && agent.isOnNavMesh ? agent.velocity.magnitude : (directMoveActive ? directSpeed : 0f);
            IsMoving = speed > 0.05f;
            UpdateAnimator(speed);
            ApplyVisualYawCorrection();
        }

        public IEnumerator MoveTo(CreatureLocationMarker marker, string movement)
        {
            if (marker == null)
            {
                LastMoveSucceeded = false;
                LastMoveError = "没有可移动目标点。";
                yield break;
            }

            yield return MoveToPosition(marker.transform.position, movement, marker.displayName);
        }

        public IEnumerator MoveToPosition(Vector3 target, string movement, string destinationName = "目标点")
        {
            float targetSpeed = string.Equals(movement, "run", System.StringComparison.OrdinalIgnoreCase) ? runSpeed : walkSpeed;
            directMoveActive = false;
            LastMoveSucceeded = false;
            LastMoveError = string.Empty;
            float distance = Vector3.Distance(transform.position, target);
            float travelBudget = distance / Mathf.Max(0.1f, targetSpeed) + 8f;
            float deadline = Time.time + Mathf.Max(3f, maxMoveSeconds, travelBudget);

            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.speed = targetSpeed;
                agent.isStopped = false;
                if (!agent.SetDestination(target))
                {
                    LastMoveError = $"NavMeshAgent 拒绝了目标点：{destinationName}。";
                    agent.isStopped = true;
                    UpdateAnimator(0f);
                    yield break;
                }

                while (agent.pathPending && Time.time < deadline)
                {
                    yield return null;
                }

                if (agent.pathStatus == NavMeshPathStatus.PathInvalid ||
                    agent.pathStatus == NavMeshPathStatus.PathPartial)
                {
                    LastMoveError = agent.pathStatus == NavMeshPathStatus.PathPartial
                        ? "NavMesh 路径不完整，目标点不可完全到达。"
                        : "NavMesh 路径无效。";
                    agent.isStopped = true;
                    UpdateAnimator(0f);
                    yield break;
                }

                float pathDistance = EstimatePathDistance(agent.path, transform.position, target);
                if (pathDistance > 0.01f)
                {
                    float pathBudget = pathDistance / Mathf.Max(0.1f, targetSpeed) + 8f;
                    deadline = Time.time + Mathf.Max(3f, maxMoveSeconds, pathBudget);
                }

                Vector3 lastProgressPosition = transform.position;
                float lastProgressTime = Time.time;
                while (Time.time < deadline)
                {
                    if (!agent.pathPending &&
                        (agent.pathStatus == NavMeshPathStatus.PathInvalid ||
                         agent.pathStatus == NavMeshPathStatus.PathPartial))
                    {
                        LastMoveError = agent.pathStatus == NavMeshPathStatus.PathPartial
                            ? "NavMesh 路径不完整，目标点不可完全到达。"
                            : "NavMesh 路径无效。";
                        break;
                    }

                    if (!agent.pathPending && agent.remainingDistance <= Mathf.Max(arriveDistance, agent.stoppingDistance))
                    {
                        LastMoveSucceeded = true;
                        break;
                    }

                    if (Vector3.Distance(transform.position, lastProgressPosition) > 0.08f)
                    {
                        lastProgressPosition = transform.position;
                        lastProgressTime = Time.time;
                    }
                    else if (Time.time - lastProgressTime > Mathf.Max(1f, stuckTimeoutSeconds))
                    {
                        LastMoveError = "移动进度停滞，已提前放弃这个目标。";
                        break;
                    }

                    yield return null;
                }

                if (!LastMoveSucceeded && string.IsNullOrWhiteSpace(LastMoveError))
                {
                    LastMoveError = "移动超时，可能卡住或目标点过远。";
                }

                agent.isStopped = true;
            }
            else
            {
                directTarget = target;
                directSpeed = targetSpeed;
                directMoveActive = true;
                while (Vector3.Distance(transform.position, directTarget) > arriveDistance && Time.time < deadline)
                {
                    yield return null;
                }

                LastMoveSucceeded = Vector3.Distance(transform.position, directTarget) <= arriveDistance;
                if (!LastMoveSucceeded)
                {
                    LastMoveError = "直接移动超时。";
                }

                directMoveActive = false;
            }

            UpdateAnimator(0f);
        }

        private static float EstimatePathDistance(NavMeshPath path, Vector3 fallbackStart, Vector3 fallbackEnd)
        {
            if (path == null || path.corners == null || path.corners.Length < 2)
            {
                return Vector3.Distance(fallbackStart, fallbackEnd);
            }

            float distance = Vector3.Distance(fallbackStart, path.corners[0]);
            for (int i = 1; i < path.corners.Length; i++)
            {
                distance += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            }

            distance += Vector3.Distance(path.corners[path.corners.Length - 1], fallbackEnd);
            return distance;
        }

        public IEnumerator Dwell(float seconds, Transform lookAt = null)
        {
            float endTime = Time.time + Mathf.Max(0.1f, seconds);
            while (Time.time < endTime)
            {
                if (lookAt != null)
                {
                    Vector3 direction = lookAt.position - transform.position;
                    direction.y = 0f;
                    if (direction.sqrMagnitude > 0.001f)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 2f);
                        ApplyVisualYawCorrection();
                    }
                }

                UpdateAnimator(0f);
                yield return null;
            }
        }

        private void UpdateDirectMove()
        {
            Vector3 direction = directTarget - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= arriveDistance * arriveDistance)
            {
                directMoveActive = false;
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(direction.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, Time.deltaTime * directMoveTurnSpeed);
            transform.position = Vector3.MoveTowards(transform.position, directTarget, directSpeed * Time.deltaTime);
        }

        private void UpdateAnimator(float speed)
        {
            if (animator == null)
            {
                return;
            }

            animator.SetBool(GroundedId, true);
            animator.SetFloat(SpeedId, speed);
            animator.SetFloat(MotionSpeedId, speed > 0.05f ? 1f : 0f);
        }

        private void CacheVisualBaseRotation()
        {
            if (visualRoot == null || hasVisualBaseRotation)
            {
                return;
            }

            visualBaseLocalRotation = visualRoot.localRotation;
            hasVisualBaseRotation = true;
        }

        private void ApplyVisualYawCorrection()
        {
            if (visualRoot == null || Mathf.Abs(visualYawOffsetDegrees) <= 0.001f)
            {
                return;
            }

            CacheVisualBaseRotation();
            visualRoot.localRotation = visualBaseLocalRotation * Quaternion.Euler(0f, visualYawOffsetDegrees, 0f);
        }
    }
}
