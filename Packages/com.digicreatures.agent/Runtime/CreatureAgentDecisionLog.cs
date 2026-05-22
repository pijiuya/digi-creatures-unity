using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigiCreatures
{
    public sealed class CreatureAgentDecisionEntry
    {
        public DateTime time;
        public string agentName;
        public string status;
        public string activity;
        public string target;
        public string destination;
        public string region;
        public string targetInterest;
        public string reason;
        public string dialogue;
        public string error;
        public long latencyMs;
    }

    public static class CreatureAgentDecisionLog
    {
        private const int MaxEntries = 80;
        private static readonly List<CreatureAgentDecisionEntry> Entries = new List<CreatureAgentDecisionEntry>();

        public static event Action Changed;

        public static IReadOnlyList<CreatureAgentDecisionEntry> Snapshot => Entries;

        public static void Add(
            Component agent,
            string status,
            string activity,
            string target,
            string destination,
            string region,
            string targetInterest,
            string reason,
            string dialogue,
            string error,
            long latencyMs)
        {
            Entries.Insert(0, new CreatureAgentDecisionEntry
            {
                time = DateTime.Now,
                agentName = agent == null ? "未知智能体" : agent.name,
                status = status,
                activity = activity,
                target = target,
                destination = destination,
                region = region,
                targetInterest = targetInterest,
                reason = reason,
                dialogue = dialogue,
                error = error,
                latencyMs = latencyMs
            });

            if (Entries.Count > MaxEntries)
            {
                Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);
            }

            Changed?.Invoke();
        }

        public static void Clear()
        {
            Entries.Clear();
            Changed?.Invoke();
        }
    }
}
