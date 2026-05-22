using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DigiCreatures
{
    public static class LlmResponseParser
    {
        private static readonly string[] ProtocolFields =
        {
            "mode",
            "destinationId",
            "movement",
            "dwellSeconds",
            "dialogue",
            "interactionId",
            "actionId",
            "activity",
            "intent",
            "memoryNote",
            "targetId",
            "targetName",
            "targetInterest",
            "regionId",
            "navigationKind",
            "approachPointId"
        };

        public static string[] RequiredProtocolFields => ProtocolFields;

        public static string ExtractAssistantContent(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return string.Empty;
            }

            try
            {
                ChatResponse response = JsonUtility.FromJson<ChatResponse>(responseText);
                if (response?.choices != null &&
                    response.choices.Length > 0 &&
                    response.choices[0]?.message != null &&
                    !string.IsNullOrWhiteSpace(response.choices[0].message.content))
                {
                    return response.choices[0].message.content;
                }
            }
            catch
            {
                // Some local gateways return non-OpenAI envelopes; fall through to the tolerant scanner.
            }

            string content = ExtractJsonStringValue(responseText, "content");
            return string.IsNullOrWhiteSpace(content) ? responseText : content;
        }

        public static LlmDecisionParseReport AnalyzeDecisionResponse(string responseText)
        {
            string content = ExtractAssistantContent(responseText);
            string json = ExtractProtocolJson(content);
            if (string.IsNullOrWhiteSpace(json))
            {
                json = ExtractProtocolJson(responseText);
            }

            LlmDecisionParseReport report = new LlmDecisionParseReport
            {
                AssistantContent = content,
                DecisionJson = json,
                FoundJson = !string.IsNullOrWhiteSpace(json)
            };

            report.PresentFields = GetPresentProtocolFields(json);
            report.FieldCount = report.PresentFields.Length;
            report.TotalFieldCount = ProtocolFields.Length;

            if (!report.FoundJson)
            {
                report.Error = "没有找到智能体协议 JSON。请确认模型只返回一个 JSON 对象。";
                return report;
            }

            report.Decision = TryParseDecisionJson(json, allowDialogueWithoutDestination: true, out string reason);
            report.Success = report.Decision != null;
            report.Error = report.Success ? string.Empty : reason;
            return report;
        }

        public static bool TryParseDecision(
            string responseText,
            out CreatureDecision decision,
            out string reason,
            out string decisionJson)
        {
            LlmDecisionParseReport report = AnalyzeDecisionResponse(responseText);
            decision = report.Decision;
            reason = report.Success ? string.Empty : report.Error;
            decisionJson = report.DecisionJson;
            return report.Success;
        }

        public static string ExtractProtocolJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string bestCandidate = string.Empty;
            int bestScore = -1;
            foreach (string candidate in EnumerateJsonObjects(text))
            {
                int score = CountProtocolFields(candidate);
                if (score > bestScore)
                {
                    bestCandidate = candidate;
                    bestScore = score;
                }

                if (score >= ProtocolFields.Length)
                {
                    return candidate;
                }
            }

            if (bestScore > 0)
            {
                return bestCandidate;
            }

            string repaired = TryRepairProtocolJson(text);
            if (!string.IsNullOrWhiteSpace(repaired))
            {
                return repaired;
            }

            string unescaped = UnescapeJsonString(text);
            if (!string.Equals(unescaped, text, StringComparison.Ordinal))
            {
                foreach (string candidate in EnumerateJsonObjects(unescaped))
                {
                    int score = CountProtocolFields(candidate);
                    if (score > bestScore)
                    {
                        bestCandidate = candidate;
                        bestScore = score;
                    }
                }
            }

            repaired = TryRepairProtocolJson(unescaped);
            if (!string.IsNullOrWhiteSpace(repaired))
            {
                return repaired;
            }

            return bestScore > 0 ? bestCandidate : string.Empty;
        }

        public static string[] GetPresentProtocolFields(string json)
        {
            List<string> present = new List<string>();
            foreach (string field in ProtocolFields)
            {
                if (ContainsJsonKey(json, field))
                {
                    present.Add(field);
                }
            }

            return present.ToArray();
        }

        public static string ExtractJsonStringValue(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            string token = "\"" + key + "\"";
            int searchFrom = 0;
            while (searchFrom < json.Length)
            {
                int keyIndex = json.IndexOf(token, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (keyIndex < 0)
                {
                    return string.Empty;
                }

                int colonIndex = json.IndexOf(':', keyIndex + token.Length);
                if (colonIndex < 0)
                {
                    return string.Empty;
                }

                int valueIndex = colonIndex + 1;
                while (valueIndex < json.Length && char.IsWhiteSpace(json[valueIndex]))
                {
                    valueIndex++;
                }

                if (valueIndex >= json.Length)
                {
                    return string.Empty;
                }

                if (json[valueIndex] != '"')
                {
                    searchFrom = valueIndex + 1;
                    continue;
                }

                int index = valueIndex + 1;
                bool escaping = false;
                while (index < json.Length)
                {
                    char c = json[index];
                    if (c == '"' && !escaping)
                    {
                        return UnescapeJsonString(json.Substring(valueIndex + 1, index - valueIndex - 1));
                    }

                    escaping = c == '\\' && !escaping;
                    if (c != '\\')
                    {
                        escaping = false;
                    }

                    index++;
                }

                return string.Empty;
            }

            return string.Empty;
        }

        private static CreatureDecision TryParseDecisionJson(string json, bool allowDialogueWithoutDestination, out string reason)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                reason = "没有可解析的 JSON。";
                return null;
            }

            string[] present = GetPresentProtocolFields(json);
            if (present.Length == 0)
            {
                reason = "JSON 里没有智能体协议字段。";
                return null;
            }

            try
            {
                string normalizedJson = NormalizeDecisionJson(json);
                CreatureDecision decision = JsonUtility.FromJson<CreatureDecision>(normalizedJson);
                if (decision == null)
                {
                    reason = "JsonUtility 没有生成 CreatureDecision。";
                    return null;
                }

                decision.Clamp();
                bool dialogue = string.Equals(decision.mode, "dialogue", StringComparison.OrdinalIgnoreCase);
                bool localActivity = IsLocalActivity(decision.activity);
                if (!dialogue &&
                    !localActivity &&
                    string.IsNullOrWhiteSpace(decision.destinationId) &&
                    string.IsNullOrWhiteSpace(decision.approachPointId) &&
                    string.IsNullOrWhiteSpace(decision.targetId) &&
                    string.IsNullOrWhiteSpace(decision.regionId) &&
                    string.IsNullOrWhiteSpace(decision.interactionId))
                {
                    reason = "移动模式需要 destinationId、approachPointId、targetId、regionId 或 interactionId；对话模式和原地活动可以为空。";
                    return null;
                }

                if (!allowDialogueWithoutDestination &&
                    string.IsNullOrWhiteSpace(decision.destinationId) &&
                    string.IsNullOrWhiteSpace(decision.approachPointId))
                {
                    reason = "当前调用要求 destinationId 或 approachPointId 不能为空。";
                    return null;
                }

                reason = string.Empty;
                return decision;
            }
            catch (Exception ex)
            {
                reason = "JSON 格式错误：" + ex.Message;
                return null;
            }
        }

        private static bool IsLocalActivity(string activity)
        {
            return string.Equals(activity, "rest", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(activity, "roll", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(activity, "idle", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDecisionJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return json;
            }

            string normalized = json;
            normalized = QuoteNonStringScalar(normalized, "mode");
            normalized = QuoteNonStringScalar(normalized, "movement");
            normalized = QuoteNonStringScalar(normalized, "targetInterest");
            normalized = QuoteNonStringScalar(normalized, "regionId");
            normalized = QuoteNonStringScalar(normalized, "navigationKind");
            normalized = QuoteNonStringScalar(normalized, "activity");
            normalized = QuoteNonStringScalar(normalized, "intent");
            normalized = QuoteNonStringScalar(normalized, "dialogue");
            normalized = QuoteNonStringScalar(normalized, "memoryNote");
            return normalized;
        }

        private static string QuoteNonStringScalar(string json, string key)
        {
            string token = "\"" + key + "\"";
            int keyIndex = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            while (keyIndex >= 0)
            {
                int colonIndex = json.IndexOf(':', keyIndex + token.Length);
                if (colonIndex < 0)
                {
                    return json;
                }

                int valueStart = colonIndex + 1;
                while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                {
                    valueStart++;
                }

                if (valueStart >= json.Length || json[valueStart] == '"')
                {
                    keyIndex = json.IndexOf(token, keyIndex + token.Length, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (json[valueStart] == '{' || json[valueStart] == '[')
                {
                    int complexEnd = FindJsonValueEnd(json, valueStart);
                    string replacement = string.Equals(key, "movement", StringComparison.OrdinalIgnoreCase) ? "walk" : string.Empty;
                    json = json.Substring(0, valueStart) + "\"" + replacement + "\"" + json.Substring(complexEnd);
                    keyIndex = json.IndexOf(token, keyIndex + token.Length, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (StartsWithJsonNull(json, valueStart))
                {
                    json = json.Substring(0, valueStart) + "\"\"" + json.Substring(valueStart + 4);
                    keyIndex = json.IndexOf(token, keyIndex + token.Length, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                int valueEnd = valueStart;
                while (valueEnd < json.Length && json[valueEnd] != ',' && json[valueEnd] != '}')
                {
                    valueEnd++;
                }

                string rawValue = json.Substring(valueStart, valueEnd - valueStart).Trim();
                if (!string.IsNullOrWhiteSpace(rawValue))
                {
                    string quoted = "\"" + EscapeJsonString(rawValue) + "\"";
                    json = json.Substring(0, valueStart) + quoted + json.Substring(valueEnd);
                }

                keyIndex = json.IndexOf(token, keyIndex + token.Length, StringComparison.OrdinalIgnoreCase);
            }

            return json;
        }

        private static int FindJsonValueEnd(string json, int start)
        {
            char open = json[start];
            char close = open == '{' ? '}' : ']';
            int depth = 0;
            bool inString = false;
            bool escaping = false;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (c == '"' && !escaping)
                    {
                        inString = false;
                    }

                    escaping = c == '\\' && !escaping;
                    if (c != '\\')
                    {
                        escaping = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                }
                else if (c == open)
                {
                    depth++;
                }
                else if (c == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i + 1;
                    }
                }
            }

            return json.Length;
        }

        private static bool StartsWithJsonNull(string json, int index)
        {
            return index + 4 <= json.Length &&
                   string.Equals(json.Substring(index, 4), "null", StringComparison.OrdinalIgnoreCase);
        }

        private static string EscapeJsonString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static IEnumerable<string> EnumerateJsonObjects(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '{')
                {
                    continue;
                }

                int depth = 0;
                bool inString = false;
                bool escaping = false;
                for (int j = i; j < text.Length; j++)
                {
                    char c = text[j];
                    if (inString)
                    {
                        if (c == '"' && !escaping)
                        {
                            inString = false;
                        }

                        escaping = c == '\\' && !escaping;
                        if (c != '\\')
                        {
                            escaping = false;
                        }

                        continue;
                    }

                    if (c == '"')
                    {
                        inString = true;
                    }
                    else if (c == '{')
                    {
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            yield return text.Substring(i, j - i + 1);
                            break;
                        }
                    }
                }
            }
        }

        private static string TryRepairProtocolJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            int start = text.IndexOf('{');
            if (start < 0)
            {
                return string.Empty;
            }

            string candidate = text.Substring(start).Trim();
            if (CountProtocolFields(candidate) <= 0)
            {
                return string.Empty;
            }

            string repaired = ClosePossiblyTruncatedJson(candidate);
            return CountProtocolFields(repaired) > 0 ? repaired : string.Empty;
        }

        private static string ClosePossiblyTruncatedJson(string json)
        {
            StringBuilder builder = new StringBuilder(json.Length + 8);
            int depth = 0;
            bool inString = false;
            bool escaping = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                builder.Append(c);

                if (inString)
                {
                    if (c == '"' && !escaping)
                    {
                        inString = false;
                    }

                    escaping = c == '\\' && !escaping;
                    if (c != '\\')
                    {
                        escaping = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                }
                else if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth = Math.Max(0, depth - 1);
                    if (depth == 0)
                    {
                        return builder.ToString();
                    }
                }
            }

            if (inString)
            {
                builder.Append('"');
            }

            while (depth > 0)
            {
                builder.Append('}');
                depth--;
            }

            return builder.ToString();
        }

        private static int CountProtocolFields(string json)
        {
            int count = 0;
            foreach (string field in ProtocolFields)
            {
                count += ContainsJsonKey(json, field) ? 1 : 0;
            }

            return count;
        }

        private static bool ContainsJsonKey(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            string token = "\"" + key + "\"";
            int index = 0;
            while (index < json.Length)
            {
                int keyIndex = json.IndexOf(token, index, StringComparison.OrdinalIgnoreCase);
                if (keyIndex < 0)
                {
                    return false;
                }

                if (keyIndex > 0 && json[keyIndex - 1] == '\\')
                {
                    index = keyIndex + token.Length;
                    continue;
                }

                int colonIndex = keyIndex + token.Length;
                while (colonIndex < json.Length && char.IsWhiteSpace(json[colonIndex]))
                {
                    colonIndex++;
                }

                if (colonIndex < json.Length && json[colonIndex] == ':')
                {
                    return true;
                }

                index = keyIndex + token.Length;
            }

            return false;
        }

        private static string UnescapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\' || i + 1 >= value.Length)
                {
                    builder.Append(c);
                    continue;
                }

                char next = value[++i];
                switch (next)
                {
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '/':
                        builder.Append('/');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (i + 4 < value.Length &&
                            TryReadHex(value, i + 1, out int codePoint))
                        {
                            builder.Append((char)codePoint);
                            i += 4;
                        }
                        else
                        {
                            builder.Append("\\u");
                        }
                        break;
                    default:
                        builder.Append(next);
                        break;
                }
            }

            return builder.ToString();
        }

        private static bool TryReadHex(string value, int start, out int codePoint)
        {
            codePoint = 0;
            for (int i = 0; i < 4; i++)
            {
                int digit = Uri.FromHex(value[start + i]);
                if (digit < 0)
                {
                    return false;
                }

                codePoint = (codePoint << 4) + digit;
            }

            return true;
        }

        [Serializable]
        private class ChatResponse
        {
            public ChatChoice[] choices;
        }

        [Serializable]
        private class ChatChoice
        {
            public ChatMessage message;
        }

        [Serializable]
        private class ChatMessage
        {
            public string role;
            public string content;
        }
    }

    public sealed class LlmDecisionParseReport
    {
        public string AssistantContent;
        public string DecisionJson;
        public bool FoundJson;
        public bool Success;
        public CreatureDecision Decision;
        public string Error;
        public string[] PresentFields = Array.Empty<string>();
        public int FieldCount;
        public int TotalFieldCount;
    }
}
