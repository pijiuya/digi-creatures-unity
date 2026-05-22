using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DigiCreatures
{
    public class CreatureMemoryStore
    {
        private readonly string rootPath;
        private readonly string soulPath;
        private readonly string summaryPath;
        private readonly string memoryPath;
        private readonly string defaultSoulText;
        private readonly string defaultSummaryText;

        public CreatureMemoryStore(
            string rootPath,
            string memoryFileName = "memory.jsonl",
            string summaryFileName = "summary.md",
            string defaultSoulText = "# DigiSoul\n\nA quiet digital creature.\n",
            string defaultSummaryText = "# Memory Summary\n\nNo long-term memories yet.\n")
        {
            this.rootPath = rootPath;
            soulPath = Path.Combine(rootPath, "soul.md");
            summaryPath = Path.Combine(rootPath, string.IsNullOrWhiteSpace(summaryFileName) ? "summary.md" : summaryFileName);
            memoryPath = Path.Combine(rootPath, string.IsNullOrWhiteSpace(memoryFileName) ? "memory.jsonl" : memoryFileName);
            this.defaultSoulText = string.IsNullOrWhiteSpace(defaultSoulText) ? "# DigiSoul\n\nA quiet digital creature.\n" : defaultSoulText;
            this.defaultSummaryText = string.IsNullOrWhiteSpace(defaultSummaryText) ? "# Memory Summary\n\nNo long-term memories yet.\n" : defaultSummaryText;
        }

        public string RootPath => rootPath;

        public string Soul => ReadText(soulPath, defaultSoulText);

        public string Summary => ReadText(summaryPath, defaultSummaryText);

        public List<string> ReadRecentMemories(int limit)
        {
            EnsureFiles();
            if (!File.Exists(memoryPath))
            {
                return new List<string>();
            }

            string[] lines = File.ReadAllLines(memoryPath);
            return lines.Where(line => !string.IsNullOrWhiteSpace(line))
                .Reverse()
                .Take(Mathf.Max(1, limit))
                .Reverse()
                .ToList();
        }

        public void AppendEvent(string type, string text)
        {
            EnsureFiles();
            string safeType = Escape(type);
            string safeText = Escape(text);
            string timestamp = DateTime.UtcNow.ToString("o");
            File.AppendAllText(memoryPath, $"{{\"timestamp\":\"{timestamp}\",\"type\":\"{safeType}\",\"text\":\"{safeText}\"}}\n");
        }

        public void RefreshSimpleSummary(int everyEvents)
        {
            if (everyEvents <= 0 || !File.Exists(memoryPath))
            {
                return;
            }

            string[] lines = File.ReadAllLines(memoryPath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            if (lines.Length == 0 || lines.Length % everyEvents != 0)
            {
                return;
            }

            IEnumerable<string> recent = lines.Reverse().Take(12).Reverse();
            string summary = "# Memory Summary\n\nRecent memory fragments:\n" +
                             string.Join("\n", recent.Select(line => "- " + line));
            File.WriteAllText(summaryPath, summary + "\n");
        }

        private void EnsureFiles()
        {
            Directory.CreateDirectory(rootPath);
            EnsureFile(soulPath, defaultSoulText);
            EnsureFile(summaryPath, defaultSummaryText);
            EnsureFile(memoryPath, "");
        }

        private static void EnsureFile(string path, string defaultText)
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, defaultText);
            }
        }

        private static string ReadText(string path, string fallback)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : fallback;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Creature memory read failed for {path}: {ex.Message}");
                return fallback;
            }
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "");
        }
    }
}
