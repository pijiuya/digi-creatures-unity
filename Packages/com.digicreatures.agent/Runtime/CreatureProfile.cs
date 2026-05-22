using System;
using UnityEngine;

namespace DigiCreatures
{
    [CreateAssetMenu(menuName = "数字生物/生物档案", fileName = "CreatureProfile")]
    public class CreatureProfile : ScriptableObject
    {
        public string creatureId = "creature_01";
        public string displayName = "数字生物";
        public string subtitleName = "数字生物";
        public string dataFolderName = "Creature01";
        public bool usePersistentDataPathInPlayer = true;
        public TextAsset defaultSoul;
        public TextAsset defaultSummary;
        public CreatureLlmSettings llmSettings;

        public string CreatureId => string.IsNullOrWhiteSpace(creatureId) ? "creature_01" : SanitizePathSegment(creatureId);

        public string DataFolderName => string.IsNullOrWhiteSpace(dataFolderName) ? CreatureId : SanitizePathSegment(dataFolderName);

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? CreatureId : displayName.Trim();

        public string SubtitleName => string.IsNullOrWhiteSpace(subtitleName) ? DisplayName : subtitleName.Trim();

        public string DefaultSoulText => defaultSoul == null || string.IsNullOrWhiteSpace(defaultSoul.text)
            ? "# DigiSoul\n\n一个好奇的数字生物。\n"
            : defaultSoul.text;

        public string DefaultSummaryText => defaultSummary == null || string.IsNullOrWhiteSpace(defaultSummary.text)
            ? "# Memory Summary\n\nNo long-term memories yet.\n"
            : defaultSummary.text;

        public static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "creature_01";
            }

            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            string trimmed = value.Trim();
            foreach (char c in invalid)
            {
                trimmed = trimmed.Replace(c, '_');
            }

            return string.IsNullOrWhiteSpace(trimmed) ? "creature_01" : trimmed;
        }
    }
}
