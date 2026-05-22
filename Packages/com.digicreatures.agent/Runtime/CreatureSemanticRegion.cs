using UnityEngine;
using UnityEngine.AI;

namespace DigiCreatures
{
    public enum CreatureRegionShape
    {
        Box,
        Sphere
    }

    public sealed class CreatureSemanticRegion : MonoBehaviour
    {
        public string regionId = "semantic_region";
        public string displayName = "语义区域";
        [TextArea] public string description = "场景中一个适合智能体探索的区域。";
        public string tags = "region,explore";
        public int priority = 1;
        [Range(0, 5)] public int dangerLevel;
        public CreatureRegionShape shape = CreatureRegionShape.Box;
        public Vector3 boxSize = new Vector3(6f, 3f, 6f);
        public float radius = 4f;

        public string ToPromptLine(Transform creature)
        {
            float distance = creature == null ? 0f : Vector3.Distance(creature.position, transform.position);
            string size = shape == CreatureRegionShape.Box
                ? $"box=({boxSize.x:0.0},{boxSize.y:0.0},{boxSize.z:0.0})"
                : $"radius={Mathf.Max(0.2f, radius):0.0}";
            return $"{regionId}: {displayName} | shape={shape} | {size} | tags={tags} | priority={Mathf.Max(1, priority)} | danger={dangerLevel} | distance={distance:0.0} | {description}";
        }

        public bool TrySampleNavPoint(Vector3 from, float scale, out Vector3 position)
        {
            position = transform.position;
            NavMeshPath path = new NavMeshPath();
            float safeScale = Mathf.Max(0.2f, scale);
            float sampleDistance = Mathf.Max(2.5f, shape == CreatureRegionShape.Box ? boxSize.y * 0.5f + 2f : radius * 0.35f + 2f);

            for (int i = 0; i < 36; i++)
            {
                Vector3 candidate = SampleWorldPoint(safeScale);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas))
                {
                    continue;
                }

                if (NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, path) &&
                    path.status == NavMeshPathStatus.PathComplete)
                {
                    position = hit.position;
                    return true;
                }
            }

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit centerHit, sampleDistance, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(from, centerHit.position, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete)
            {
                position = centerHit.position;
                return true;
            }

            return false;
        }

        private Vector3 SampleWorldPoint(float scale)
        {
            if (shape == CreatureRegionShape.Sphere)
            {
                Vector3 local = Random.insideUnitSphere * Mathf.Max(0.2f, radius * scale);
                return transform.TransformPoint(local);
            }

            Vector3 size = new Vector3(
                Mathf.Max(0.2f, boxSize.x * scale),
                Mathf.Max(0.2f, boxSize.y * scale),
                Mathf.Max(0.2f, boxSize.z * scale));
            Vector3 localBox = new Vector3(
                Random.Range(-size.x, size.x) * 0.5f,
                Random.Range(-size.y, size.y) * 0.5f,
                Random.Range(-size.z, size.z) * 0.5f);
            return transform.TransformPoint(localBox);
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(regionId) ||
                string.Equals(regionId, "semantic_region", System.StringComparison.OrdinalIgnoreCase))
            {
                regionId = CreatureSemanticTarget.MakeId(gameObject.name);
            }

            if (string.IsNullOrWhiteSpace(displayName) ||
                string.Equals(displayName, "语义区域", System.StringComparison.Ordinal))
            {
                displayName = gameObject.name;
            }

            priority = Mathf.Max(1, priority);
            radius = Mathf.Max(0.2f, radius);
            boxSize = new Vector3(
                Mathf.Max(0.2f, boxSize.x),
                Mathf.Max(0.2f, boxSize.y),
                Mathf.Max(0.2f, boxSize.z));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.45f);
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            if (shape == CreatureRegionShape.Sphere)
            {
                Gizmos.DrawWireSphere(Vector3.zero, Mathf.Max(0.2f, radius));
            }
            else
            {
                Gizmos.DrawWireCube(Vector3.zero, boxSize);
            }

            Gizmos.matrix = previous;
        }
    }
}
