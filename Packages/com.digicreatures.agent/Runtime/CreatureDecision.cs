using System;

namespace DigiCreatures
{
    [Serializable]
    public class CreatureDecision
    {
        public string mode = "move";
        public string destinationId;
        public string movement = "walk";
        public float dwellSeconds = 8f;
        public string dialogue;
        public string interactionId;
        public string actionId;
        public string activity = "approach";
        public string intent = "Observe this place quietly.";
        public string memoryNote = "The creature noticed a place in DigiPlace.";
        public string targetId;
        public string targetName;
        public string targetInterest;
        public string regionId;
        public string navigationKind = "Walkable";
        public string approachPointId;

        public void Clamp()
        {
            mode = Clean(mode);
            destinationId = Clean(destinationId);
            movement = Clean(movement);
            dialogue = Clean(dialogue);
            interactionId = Clean(interactionId);
            actionId = Clean(actionId);
            activity = Clean(activity);
            intent = Clean(intent);
            memoryNote = Clean(memoryNote);
            targetId = Clean(targetId);
            targetName = Clean(targetName);
            targetInterest = Clean(targetInterest);
            regionId = Clean(regionId);
            navigationKind = Clean(navigationKind);
            approachPointId = Clean(approachPointId);

            ClearOptionalPlaceholders();
            NormalizeInteractionTokens();

            if (string.Equals(mode, "explore", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "exploration", StringComparison.OrdinalIgnoreCase))
            {
                mode = "move";
            }

            if (!string.Equals(mode, "dialogue", StringComparison.OrdinalIgnoreCase))
            {
                mode = "move";
            }

            if (!string.Equals(movement, "run", StringComparison.OrdinalIgnoreCase))
            {
                movement = "walk";
            }

            dwellSeconds = Math.Max(0.8f, Math.Min(12f, dwellSeconds));

            if (string.IsNullOrWhiteSpace(activity))
            {
                activity = "approach";
            }

            if (string.IsNullOrWhiteSpace(approachPointId))
            {
                approachPointId = destinationId;
            }
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            return string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase) ? string.Empty : trimmed;
        }

        private void ClearOptionalPlaceholders()
        {
            destinationId = ClearPlaceholder(destinationId);
            interactionId = ClearPlaceholder(interactionId);
            actionId = ClearPlaceholder(actionId);
            targetId = ClearPlaceholder(targetId);
            targetName = ClearPlaceholder(targetName);
            targetInterest = ClearPlaceholder(targetInterest);
            regionId = ClearPlaceholder(regionId);
            approachPointId = ClearPlaceholder(approachPointId);
        }

        private void NormalizeInteractionTokens()
        {
            if (string.IsNullOrWhiteSpace(actionId) && LooksLikeActionToken(interactionId))
            {
                actionId = ExtractActionId(interactionId);
                interactionId = string.Empty;
            }

            if (LooksLikeActionToken(actionId))
            {
                actionId = ExtractActionId(actionId);
            }
        }

        private static string ClearPlaceholder(string value)
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

        private static bool LooksLikeActionToken(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Contains(":");
        }

        private static string ExtractActionId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            int colon = value.IndexOf(':');
            string action = colon < 0 ? value : value.Substring(0, colon);
            return Clean(action)?.Trim('/', '\\', '"', '\'', ' ', '\t') ?? string.Empty;
        }
    }
}
