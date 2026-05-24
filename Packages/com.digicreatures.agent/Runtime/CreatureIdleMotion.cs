using UnityEngine;

namespace DigiCreatures
{
    [DisallowMultipleComponent]
    public class CreatureIdleMotion : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private string idleClipName = "Idle";
        [SerializeField] private string idleClipPath = "";
        [SerializeField] private string thinkingClipName = "Walk_N";
        [SerializeField] private string thinkingClipPath = "";
        [SerializeField] private string controllerStateName = "Idle Walk Run Blend";
        [SerializeField] private string nativeAnimationNote = "Optional note for demo locomotion clips. Runtime animation is driven by Animator parameters, so customer projects can leave clip paths empty.";
        [SerializeField] private bool forceIdleParameters = true;
        [SerializeField] private float thinkingBlendSpeed = 0.45f;
        [SerializeField] private float thinkingMotionSpeed = 0.55f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
        private string activity = "idle";
        private bool hasSpeedParameter;
        private bool hasGroundedParameter;
        private bool hasMotionSpeedParameter;

        public string IdleClipName => idleClipName;
        public string IdleClipPath => idleClipPath;
        public string ThinkingClipName => thinkingClipName;
        public string ThinkingClipPath => thinkingClipPath;
        public string ControllerStateName => controllerStateName;
        public string NativeAnimationNote => nativeAnimationNote;

        private void Awake()
        {
            animator = animator != null ? animator : GetComponentInChildren<Animator>(true);
            CacheAnimatorParameters();
        }

        private void LateUpdate()
        {
            if (!forceIdleParameters || animator == null)
            {
                return;
            }

            if (IsThinkingActivity(activity))
            {
                ApplyThinkingParameters();
            }
            else if (IsStillActivity(activity))
            {
                ApplyIdleParameters();
            }
        }

        public void SetActivity(string nextActivity)
        {
            activity = string.IsNullOrWhiteSpace(nextActivity) ? "idle" : nextActivity.Trim().ToLowerInvariant();
            if (IsThinkingActivity(activity))
            {
                ApplyThinkingParameters();
            }
            else if (IsStillActivity(activity))
            {
                ApplyIdleParameters();
            }
        }

        public void ClearActivity()
        {
            activity = "none";
        }

        private void ApplyIdleParameters()
        {
            if (animator == null)
            {
                return;
            }

            if (hasGroundedParameter)
            {
                animator.SetBool(GroundedId, true);
            }

            if (hasSpeedParameter)
            {
                animator.SetFloat(SpeedId, 0f);
            }

            if (hasMotionSpeedParameter)
            {
                animator.SetFloat(MotionSpeedId, 0f);
            }
        }

        private void ApplyThinkingParameters()
        {
            if (animator == null)
            {
                return;
            }

            if (hasGroundedParameter)
            {
                animator.SetBool(GroundedId, true);
            }

            if (hasSpeedParameter)
            {
                animator.SetFloat(SpeedId, Mathf.Max(0.05f, thinkingBlendSpeed));
            }

            if (hasMotionSpeedParameter)
            {
                animator.SetFloat(MotionSpeedId, Mathf.Max(0.05f, thinkingMotionSpeed));
            }
        }

        private void CacheAnimatorParameters()
        {
            hasSpeedParameter = false;
            hasGroundedParameter = false;
            hasMotionSpeedParameter = false;

            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == SpeedId && parameter.type == AnimatorControllerParameterType.Float)
                {
                    hasSpeedParameter = true;
                }
                else if (parameter.nameHash == GroundedId && parameter.type == AnimatorControllerParameterType.Bool)
                {
                    hasGroundedParameter = true;
                }
                else if (parameter.nameHash == MotionSpeedId && parameter.type == AnimatorControllerParameterType.Float)
                {
                    hasMotionSpeedParameter = true;
                }
            }
        }

        private static bool IsThinkingActivity(string currentActivity)
        {
            return string.Equals(currentActivity, "think", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(currentActivity, "waiting", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(currentActivity, "等待模型", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStillActivity(string currentActivity)
        {
            return string.Equals(currentActivity, "idle", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(currentActivity, "rest", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(currentActivity, "roll", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(currentActivity, "speak", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
