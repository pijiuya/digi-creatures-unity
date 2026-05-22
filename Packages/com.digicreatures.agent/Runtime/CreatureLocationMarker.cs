using UnityEngine;

namespace DigiCreatures
{
    public class CreatureLocationMarker : MonoBehaviour
    {
        public string id = "location";
        public string displayName = "Location";
        [TextArea] public string description = "A place in DigiPlace.";
        public string tags = "neutral";
        public int priority = 1;
        public string semanticTargetId;
        public CreatureNavigationKind navigationKind = CreatureNavigationKind.Walkable;
        public string navAreaName = "Walkable";
        public bool isSemanticGenerated;

        public string ToPromptLine()
        {
            Vector3 p = transform.position;
            string target = string.IsNullOrWhiteSpace(semanticTargetId) ? "none" : semanticTargetId;
            return $"{id}: {displayName} | target={target} | kind={navigationKind} | navArea={navAreaName} | tags={tags} | priority={priority} | position=({p.x:0.0},{p.y:0.0},{p.z:0.0}) | {description}";
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.25f, 0.2f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.7f);
        }
    }
}
