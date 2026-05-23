using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace DigiCreatures
{
    public enum CreatureCameraMode
    {
        Fixed,
        FixedLookAt
    }

    [DisallowMultipleComponent]
    public class CreatureCameraRig : MonoBehaviour
    {
        private const string RuntimeCameraName = "DigiCreature Runtime Movement Camera";

        private enum RuntimeCameraMode
        {
            None,
            ThirdPerson,
            MovementFrame
        }

        private static CreatureCameraRig activeRig;

        [SerializeField] private Transform target;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private CreatureCameraMode cameraMode = CreatureCameraMode.Fixed;
        [SerializeField] private bool enableMovementFraming = true;
        [SerializeField, Range(0f, 1f)] private float movementFrameChanceWhenFixedHidden = 0.3f;
        [SerializeField] private float movementShotMinDistance = 2.4f;
        [SerializeField] private float movementShotMaxDistance = 36f;
        [SerializeField] private float movementShotMargin = 1.28f;
        [SerializeField] private float movementShotHeightPadding = 1.8f;
        [SerializeField] private float fixedReturnPadding = 0.08f;
        [SerializeField] private bool useFixedCameraOcclusionCheck = true;
        [SerializeField] private LayerMask fixedCameraOcclusionLayers = ~0;
        [SerializeField, Range(0.05f, 1f)] private float fixedCameraRequiredVisibleRayRatio = 0.25f;
        [SerializeField] private float occlusionRaycastSkin = 0.08f;
        [SerializeField] private bool allowManualThirdPersonToggle = true;
        [SerializeField] private Key manualThirdPersonToggleKey = Key.Space;
        [SerializeField] private Vector3 thirdPersonOffset = new Vector3(0f, 3.2f, -6.5f);
        [SerializeField] private float thirdPersonYawOffsetDegrees;
        [SerializeField] private float lookAtHeight = 1.35f;
        [SerializeField] private float positionDamping = 7f;
        [SerializeField] private float rotationDamping = 10f;

        private Vector3 fixedCameraPosition;
        private Quaternion fixedCameraRotation;
        private bool fixedPoseCaptured;
        private Camera runtimeMovementCamera;
        private RuntimeCameraMode runtimeMode;
        private Transform runtimeTarget;
        private bool runtimeCameraInitialized;
        private bool manualThirdPersonActive;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        public CreatureCameraMode CameraMode
        {
            get => cameraMode;
            set
            {
                if (cameraMode == value)
                {
                    return;
                }

                cameraMode = value;
                fixedPoseCaptured = false;
            }
        }

        private void Awake()
        {
            activeRig = this;
            targetCamera = targetCamera != null ? targetCamera : GetComponent<Camera>();
            CaptureFixedPose();
        }

        private void OnEnable()
        {
            activeRig = this;
        }

        private void OnDisable()
        {
            if (activeRig == this)
            {
                activeRig = null;
            }

            DestroyRuntimeCamera();
        }

        private void OnDestroy()
        {
            if (activeRig == this)
            {
                activeRig = null;
            }

            DestroyRuntimeCamera();
        }

        private void LateUpdate()
        {
            targetCamera = targetCamera != null ? targetCamera : GetComponent<Camera>();
            if (targetCamera == null)
            {
                return;
            }

            UpdateFixedCamera();
            HandleManualThirdPersonToggle();
            UpdateRuntimeCamera();
        }

        public static void BeginMovementShot(Transform shotTarget, Vector3 start, Vector3 end)
        {
            CreatureCameraRig rig = ResolveRig();
            if (rig != null)
            {
                rig.BeginMovementShotInternal(shotTarget, start, end);
            }
        }

        public static void EndMovementShot(Transform shotTarget)
        {
            CreatureCameraRig rig = ResolveRig();
            if (rig != null)
            {
                rig.EndMovementShotInternal(shotTarget);
            }
        }

        public void SetModeThirdPerson()
        {
            CameraMode = CreatureCameraMode.Fixed;
        }

        public void SetModeFixedLookAt()
        {
            CameraMode = CreatureCameraMode.FixedLookAt;
            CaptureFixedPose();
        }

        private void UpdateFixedCamera()
        {
            CaptureFixedPose();
            transform.position = fixedCameraPosition;
            transform.rotation = fixedCameraRotation;
        }

        private void BeginMovementShotInternal(Transform shotTarget, Vector3 start, Vector3 end)
        {
            if (!enableMovementFraming || shotTarget == null)
            {
                return;
            }

            targetCamera = targetCamera != null ? targetCamera : GetComponent<Camera>();
            if (targetCamera == null)
            {
                return;
            }

            CaptureFixedPose();
            if (manualThirdPersonActive)
            {
                BeginThirdPersonShot(shotTarget);
                manualThirdPersonActive = true;
                return;
            }

            float travelDistance = Vector3.Distance(start, end);
            if (CanFixedCameraSee(shotTarget, fixedReturnPadding))
            {
                ReturnToFixedCamera();
                return;
            }

            if (Random.value < Mathf.Clamp01(movementFrameChanceWhenFixedHidden) &&
                TryBeginMovementFrameShot(shotTarget, start, end, travelDistance))
            {
                return;
            }

            BeginThirdPersonShot(shotTarget);
        }

        private bool TryBeginMovementFrameShot(Transform shotTarget, Vector3 start, Vector3 end, float travelDistance)
        {
            if (travelDistance < Mathf.Max(0.1f, movementShotMinDistance) ||
                travelDistance > Mathf.Max(movementShotMinDistance, movementShotMaxDistance))
            {
                return false;
            }

            Bounds movementBounds = BuildMovementBounds(shotTarget, start, end);
            if (!TryBuildMovementCameraPose(movementBounds, travelDistance, out Vector3 cameraPosition, out Quaternion cameraRotation))
            {
                return false;
            }

            Camera runtimeCamera = EnsureRuntimeCamera();
            if (runtimeCamera == null)
            {
                return false;
            }

            CopyCameraSettings(targetCamera, runtimeCamera);
            runtimeCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            runtimeCamera.depth = targetCamera.depth + 20f;
            runtimeCamera.enabled = true;
            targetCamera.enabled = false;
            runtimeMode = RuntimeCameraMode.MovementFrame;
            runtimeTarget = shotTarget;
            runtimeCameraInitialized = true;
            return true;
        }

        private void BeginThirdPersonShot(Transform shotTarget)
        {
            Camera runtimeCamera = EnsureRuntimeCamera();
            if (runtimeCamera == null || targetCamera == null)
            {
                return;
            }

            CopyCameraSettings(targetCamera, runtimeCamera);
            runtimeCamera.depth = targetCamera.depth + 20f;
            runtimeCamera.enabled = true;
            targetCamera.enabled = false;
            runtimeMode = RuntimeCameraMode.ThirdPerson;
            runtimeTarget = shotTarget;
            runtimeCameraInitialized = false;
            UpdateThirdPersonCamera(true);
        }

        private void EndMovementShotInternal(Transform shotTarget)
        {
            if (manualThirdPersonActive)
            {
                return;
            }

            if (runtimeMode == RuntimeCameraMode.None)
            {
                return;
            }

            if (shotTarget == null || CanFixedCameraSee(shotTarget, fixedReturnPadding))
            {
                ReturnToFixedCamera();
            }
        }

        private void CaptureFixedPose()
        {
            if (fixedPoseCaptured)
            {
                return;
            }

            fixedCameraPosition = transform.position;
            fixedCameraRotation = transform.rotation;
            fixedPoseCaptured = true;
        }

        private void HandleManualThirdPersonToggle()
        {
            if (!allowManualThirdPersonToggle || !Application.isPlaying || Keyboard.current == null)
            {
                return;
            }

            KeyControl key = Keyboard.current[manualThirdPersonToggleKey];
            bool pressedConfiguredKey = key != null && key.wasPressedThisFrame;
            bool pressedSpaceKey = Keyboard.current.spaceKey != null && Keyboard.current.spaceKey.wasPressedThisFrame;
            if (!pressedConfiguredKey && !pressedSpaceKey)
            {
                return;
            }

            if (manualThirdPersonActive && runtimeMode == RuntimeCameraMode.ThirdPerson)
            {
                manualThirdPersonActive = false;
                ReturnToFixedCamera();
                return;
            }

            Transform manualTarget = ResolveManualTarget();
            if (manualTarget == null)
            {
                return;
            }

            manualThirdPersonActive = true;
            BeginThirdPersonShot(manualTarget);
        }

        private Transform ResolveManualTarget()
        {
            if (target != null)
            {
                return target;
            }

            if (runtimeTarget != null)
            {
                return runtimeTarget;
            }

            CreatureBrain brain = CreatureObjectFinder.FindAnyObjectByType<CreatureBrain>(false);
            return brain == null ? null : brain.transform;
        }

        private bool CanFixedCameraSee(Transform shotTarget, float padding)
        {
            if (targetCamera == null || shotTarget == null)
            {
                return false;
            }

            Vector3 originalPosition = targetCamera.transform.position;
            Quaternion originalRotation = targetCamera.transform.rotation;
            targetCamera.transform.SetPositionAndRotation(fixedCameraPosition, fixedCameraRotation);
            Bounds bounds = CalculateTargetBounds(shotTarget);
            bool visible = BoundsVisibleToCamera(targetCamera, bounds, padding) &&
                           HasFixedCameraLineOfSight(targetCamera, shotTarget, bounds, padding);
            targetCamera.transform.SetPositionAndRotation(originalPosition, originalRotation);
            return visible;
        }

        private bool HasFixedCameraLineOfSight(Camera camera, Transform shotTarget, Bounds bounds, float padding)
        {
            if (!useFixedCameraOcclusionCheck || camera == null || shotTarget == null)
            {
                return true;
            }

            Vector3[] points = GetVisibilitySamplePoints(bounds);
            int checkedRays = 0;
            int visibleRays = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (!ViewportPointVisible(camera, points[i], padding))
                {
                    continue;
                }

                checkedRays++;
                if (HasUnblockedSightLine(camera.transform.position, points[i], shotTarget, camera.cullingMask))
                {
                    visibleRays++;
                }
            }

            if (checkedRays == 0)
            {
                return true;
            }

            int required = Mathf.Max(1, Mathf.CeilToInt(checkedRays * Mathf.Clamp01(fixedCameraRequiredVisibleRayRatio)));
            return visibleRays >= required;
        }

        private bool HasUnblockedSightLine(Vector3 origin, Vector3 point, Transform shotTarget, int cameraCullingMask)
        {
            Vector3 toPoint = point - origin;
            float distance = toPoint.magnitude;
            if (distance <= Mathf.Max(0.01f, occlusionRaycastSkin))
            {
                return true;
            }

            int mask = cameraCullingMask & fixedCameraOcclusionLayers.value;
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                toPoint / distance,
                Mathf.Max(0.01f, distance - Mathf.Max(0f, occlusionRaycastSkin)),
                mask,
                QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
            {
                return true;
            }

            RaycastHit nearest = default;
            bool foundNearest = false;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i].collider;
                if (hitCollider == null || hits[i].distance < 0.001f)
                {
                    continue;
                }

                if (hits[i].distance < nearestDistance)
                {
                    nearest = hits[i];
                    nearestDistance = hits[i].distance;
                    foundNearest = true;
                }
            }

            if (!foundNearest || nearest.collider == null)
            {
                return true;
            }

            return nearest.collider.transform == shotTarget ||
                   nearest.collider.transform.IsChildOf(shotTarget);
        }

        private void UpdateRuntimeCamera()
        {
            if (runtimeMode != RuntimeCameraMode.ThirdPerson)
            {
                return;
            }

            UpdateThirdPersonCamera(false);
        }

        private void UpdateThirdPersonCamera(bool snap)
        {
            if (runtimeMovementCamera == null || runtimeTarget == null)
            {
                return;
            }

            Vector3 lookAt = runtimeTarget.position + Vector3.up * Mathf.Max(0.1f, lookAtHeight);
            Vector3 forward = ResolveThirdPersonForward(runtimeTarget);
            forward = Quaternion.AngleAxis(thirdPersonYawOffsetDegrees, Vector3.up) * forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 desiredPosition = runtimeTarget.position +
                                      right * thirdPersonOffset.x +
                                      Vector3.up * thirdPersonOffset.y +
                                      forward * thirdPersonOffset.z;
            Quaternion desiredRotation = Quaternion.LookRotation(lookAt - desiredPosition, Vector3.up);
            if (snap || !runtimeCameraInitialized)
            {
                runtimeMovementCamera.transform.SetPositionAndRotation(desiredPosition, desiredRotation);
                runtimeCameraInitialized = true;
                return;
            }

            float positionT = 1f - Mathf.Exp(-Mathf.Max(0.1f, positionDamping) * Time.deltaTime);
            float rotationT = 1f - Mathf.Exp(-Mathf.Max(0.1f, rotationDamping) * Time.deltaTime);
            runtimeMovementCamera.transform.position = Vector3.Lerp(runtimeMovementCamera.transform.position, desiredPosition, positionT);
            runtimeMovementCamera.transform.rotation = Quaternion.Slerp(runtimeMovementCamera.transform.rotation, desiredRotation, rotationT);
        }

        private Vector3 ResolveThirdPersonForward(Transform shotTarget)
        {
            NavMeshAgent agent = shotTarget.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                Vector3 velocity = agent.velocity;
                velocity.y = 0f;
                if (velocity.sqrMagnitude > 0.05f)
                {
                    return velocity.normalized;
                }
            }

            Animator animator = shotTarget.GetComponentInChildren<Animator>(true);
            Vector3 forward = animator == null ? shotTarget.forward : animator.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = shotTarget.forward;
                forward.y = 0f;
            }

            if (forward.sqrMagnitude < 0.001f)
            {
                forward = fixedCameraRotation * Vector3.forward;
                forward.y = 0f;
            }

            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            return forward.normalized;
        }

        private bool TryBuildMovementCameraPose(Bounds movementBounds, float travelDistance, out Vector3 cameraPosition, out Quaternion cameraRotation)
        {
            Camera referenceCamera = targetCamera != null ? targetCamera : GetComponent<Camera>();
            cameraPosition = fixedCameraPosition;
            cameraRotation = fixedCameraRotation;
            if (referenceCamera == null)
            {
                return false;
            }

            Vector3 viewDirection = fixedCameraRotation * Vector3.forward;
            if (viewDirection.sqrMagnitude < 0.01f)
            {
                viewDirection = Vector3.forward;
            }

            viewDirection.Normalize();
            float aspect = Mathf.Max(0.1f, referenceCamera.aspect);
            float verticalFov = Mathf.Clamp(referenceCamera.fieldOfView, 25f, 85f) * Mathf.Deg2Rad;
            float horizontalFov = 2f * Mathf.Atan(Mathf.Tan(verticalFov * 0.5f) * aspect);
            float verticalDistance = (movementBounds.extents.y + movementShotHeightPadding) / Mathf.Tan(verticalFov * 0.5f);
            float horizontalDistance = HorizontalExtentInView(movementBounds, viewDirection) / Mathf.Tan(horizontalFov * 0.5f);
            float distance = Mathf.Max(verticalDistance, horizontalDistance, travelDistance * 0.72f + 3.5f) * Mathf.Max(1f, movementShotMargin);
            distance = Mathf.Clamp(distance, 4f, Mathf.Max(5f, movementShotMaxDistance + 18f));

            Vector3 elevatedCenter = movementBounds.center + Vector3.up * Mathf.Clamp(travelDistance * 0.08f, 0.8f, 4f);
            cameraPosition = elevatedCenter - viewDirection * distance + Vector3.up * movementShotHeightPadding;
            cameraRotation = Quaternion.LookRotation(elevatedCenter - cameraPosition, Vector3.up);

            Camera runtimeCamera = EnsureRuntimeCamera();
            if (runtimeCamera == null)
            {
                return true;
            }

            CopyCameraSettings(referenceCamera, runtimeCamera);
            runtimeCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            return BoundsVisibleToCamera(runtimeCamera, movementBounds, 0.04f);
        }

        private static float HorizontalExtentInView(Bounds bounds, Vector3 viewDirection)
        {
            Vector3 right = Vector3.Cross(Vector3.up, viewDirection);
            if (right.sqrMagnitude < 0.01f)
            {
                right = Vector3.right;
            }

            right.Normalize();
            Vector3 extents = bounds.extents;
            return Mathf.Abs(right.x) * extents.x + Mathf.Abs(right.y) * extents.y + Mathf.Abs(right.z) * extents.z;
        }

        private static Bounds BuildMovementBounds(Transform shotTarget, Vector3 start, Vector3 end)
        {
            Bounds current = CalculateTargetBounds(shotTarget);
            Vector3 localCenterOffset = current.center - shotTarget.position;
            Bounds startBounds = new Bounds(start + localCenterOffset, current.size);
            Bounds endBounds = new Bounds(end + localCenterOffset, current.size);
            startBounds.Encapsulate(endBounds);
            startBounds.Expand(new Vector3(1.2f, 1f, 1.2f));
            return startBounds;
        }

        private static Bounds CalculateTargetBounds(Transform shotTarget)
        {
            Renderer[] renderers = shotTarget.GetComponentsInChildren<Renderer>();
            bool hasBounds = false;
            Bounds bounds = new Bounds(shotTarget.position + Vector3.up, new Vector3(1f, 2f, 1f));
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        private static bool BoundsVisibleToCamera(Camera camera, Bounds bounds, float padding)
        {
            if (camera == null)
            {
                return false;
            }

            Vector3[] corners = GetBoundsCorners(bounds);
            bool anyInFront = false;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 point = camera.WorldToViewportPoint(corners[i]);
                if (point.z <= Mathf.Max(0.01f, camera.nearClipPlane))
                {
                    continue;
                }

                anyInFront = true;
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }

            return anyInFront &&
                   minX >= -padding &&
                   maxX <= 1f + padding &&
                   minY >= -padding &&
                   maxY <= 1f + padding;
        }

        private static bool ViewportPointVisible(Camera camera, Vector3 worldPoint, float padding)
        {
            Vector3 point = camera.WorldToViewportPoint(worldPoint);
            return point.z > Mathf.Max(0.01f, camera.nearClipPlane) &&
                   point.x >= -padding &&
                   point.x <= 1f + padding &&
                   point.y >= -padding &&
                   point.y <= 1f + padding;
        }

        private static Vector3[] GetVisibilitySamplePoints(Bounds bounds)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            return new[]
            {
                center,
                center + Vector3.up * extents.y * 0.85f,
                center - Vector3.up * extents.y * 0.65f,
                center + Vector3.right * extents.x * 0.65f,
                center - Vector3.right * extents.x * 0.65f,
                center + Vector3.forward * extents.z * 0.65f,
                center - Vector3.forward * extents.z * 0.65f,
                center + new Vector3(extents.x, extents.y, extents.z) * 0.65f,
                center + new Vector3(-extents.x, extents.y, extents.z) * 0.65f,
                center + new Vector3(extents.x, extents.y, -extents.z) * 0.65f,
                center + new Vector3(-extents.x, extents.y, -extents.z) * 0.65f
            };
        }

        private static Vector3[] GetBoundsCorners(Bounds bounds)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            return new[]
            {
                center + new Vector3(-extents.x, -extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y, extents.z),
                center + new Vector3(-extents.x, extents.y, -extents.z),
                center + new Vector3(-extents.x, extents.y, extents.z),
                center + new Vector3(extents.x, -extents.y, -extents.z),
                center + new Vector3(extents.x, -extents.y, extents.z),
                center + new Vector3(extents.x, extents.y, -extents.z),
                center + new Vector3(extents.x, extents.y, extents.z)
            };
        }

        private Camera EnsureRuntimeCamera()
        {
            if (runtimeMovementCamera != null)
            {
                return runtimeMovementCamera;
            }

            GameObject cameraObject = new GameObject(RuntimeCameraName)
            {
                hideFlags = HideFlags.DontSave
            };
            runtimeMovementCamera = cameraObject.AddComponent<Camera>();
            runtimeMovementCamera.enabled = false;
            return runtimeMovementCamera;
        }

        private static void CopyCameraSettings(Camera source, Camera destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            destination.clearFlags = source.clearFlags;
            destination.backgroundColor = source.backgroundColor;
            destination.cullingMask = source.cullingMask;
            destination.orthographic = source.orthographic;
            destination.orthographicSize = source.orthographicSize;
            destination.fieldOfView = source.fieldOfView;
            destination.nearClipPlane = source.nearClipPlane;
            destination.farClipPlane = source.farClipPlane;
            destination.allowHDR = source.allowHDR;
            destination.allowMSAA = source.allowMSAA;
            destination.useOcclusionCulling = source.useOcclusionCulling;
        }

        private void ReturnToFixedCamera()
        {
            if (runtimeMovementCamera != null)
            {
                runtimeMovementCamera.enabled = false;
            }

            if (targetCamera != null)
            {
                targetCamera.enabled = true;
            }

            runtimeMode = RuntimeCameraMode.None;
            runtimeTarget = null;
            runtimeCameraInitialized = false;
            manualThirdPersonActive = false;
        }

        private void DestroyRuntimeCamera()
        {
            if (targetCamera != null)
            {
                targetCamera.enabled = true;
            }

            if (runtimeMovementCamera == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(runtimeMovementCamera.gameObject);
            }
            else
            {
                DestroyImmediate(runtimeMovementCamera.gameObject);
            }

            runtimeMovementCamera = null;
            runtimeMode = RuntimeCameraMode.None;
            runtimeTarget = null;
            runtimeCameraInitialized = false;
            manualThirdPersonActive = false;
        }

        private static CreatureCameraRig ResolveRig()
        {
            if (activeRig != null && activeRig.isActiveAndEnabled)
            {
                return activeRig;
            }

            CreatureCameraRig rig = CreatureObjectFinder.FindAnyObjectByType<CreatureCameraRig>(false);
            if (rig != null)
            {
                activeRig = rig;
                return rig;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                mainCamera = CreatureObjectFinder.FindAnyObjectByType<Camera>(false);
            }

            if (mainCamera == null || !Application.isPlaying)
            {
                return null;
            }

            rig = mainCamera.GetComponent<CreatureCameraRig>();
            if (rig == null)
            {
                rig = mainCamera.gameObject.AddComponent<CreatureCameraRig>();
            }

            rig.targetCamera = mainCamera;
            activeRig = rig;
            return rig;
        }
    }
}
