using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace DigiCreatures
{
    public interface ILlmClient
    {
        long LastLatencyMs { get; }
        string LastRawContent { get; }
        string LastRequestSummary { get; }
        IEnumerator RequestDecision(string prompt, CreatureLlmSettings settings, Action<CreatureDecision, string> onComplete);
        IEnumerator RequestText(string prompt, CreatureLlmSettings settings, Action<string, string> onComplete);
    }

    public class OpenAICompatibleLlmClient : ILlmClient
    {
        public long LastLatencyMs { get; private set; }
        public string LastRawContent { get; private set; }
        public string LastRequestSummary { get; private set; }

        public IEnumerator RequestDecision(string prompt, CreatureLlmSettings settings, Action<CreatureDecision, string> onComplete)
        {
            yield return RequestText(prompt, settings, (content, error) =>
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    onComplete(null, error);
                    return;
                }

                if (LlmResponseParser.TryParseDecision(content, out CreatureDecision decision, out string reason, out _))
                {
                    onComplete(decision, null);
                    return;
                }

                onComplete(null, reason + BuildRawSummary(content));
            });
        }

        public IEnumerator RequestText(string prompt, CreatureLlmSettings settings, Action<string, string> onComplete)
        {
            if (settings == null)
            {
                LastRequestSummary = "缺少模型配置";
                LastRawContent = string.Empty;
                onComplete(null, "Missing CreatureLlmSettings.");
                yield break;
            }

            string endpoint = LlmEndpointUtility.NormalizeChatCompletionsEndpoint(settings.Endpoint);
            string apiKey = settings.UseRemoteBackend
                ? ResolveApiKey(settings)
                : string.Empty;

            if (settings.UseRemoteBackend && string.IsNullOrWhiteSpace(apiKey))
            {
                LastRequestSummary = $"模型={settings.Model}; 端点={endpoint}";
                LastRawContent = string.Empty;
                onComplete(null, "Missing remote API key.");
                yield break;
            }

            LastRequestSummary = $"模型={settings.Model}; 端点={endpoint}";
            LastRawContent = string.Empty;

            string body = JsonUtility.ToJson(new ChatRequest
            {
                model = settings.Model,
                temperature = settings.temperature,
                max_tokens = 1024,
                stream = false,
                messages = new[]
                {
                    new ChatMessage { role = "system", content = "You choose actions for a Unity virtual creature. Return only valid JSON." },
                    new ChatMessage { role = "user", content = prompt }
                }
            });

            using UnityWebRequest request = new UnityWebRequest(endpoint, "POST");
            byte[] payload = Encoding.UTF8.GetBytes(body);
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(1, settings.requestTimeoutSeconds);
            request.SetRequestHeader("Content-Type", "application/json");

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            }

            float startTime = Time.realtimeSinceStartup;
            yield return request.SendWebRequest();
            LastLatencyMs = (long)((Time.realtimeSinceStartup - startTime) * 1000f);

            if (request.result != UnityWebRequest.Result.Success)
            {
                LastRawContent = request.downloadHandler == null ? string.Empty : request.downloadHandler.text;
                onComplete(null, $"{request.error}. {LastRequestSummary}{BuildRawSummary(LastRawContent)}");
                yield break;
            }

            string responseText = request.downloadHandler.text;
            string content = LlmResponseParser.ExtractAssistantContent(responseText);
            LastRawContent = content;
            Debug.Log($"DigiCreatures LLM 返回：{LastRequestSummary}; latency={LastLatencyMs}ms; content={Summarize(content, 180)}");
            onComplete(content, null);
        }

        private static string ResolveApiKey(CreatureLlmSettings settings)
        {
            string environmentVariable = settings.ApiKeyEnvironmentVariable;
            if (!string.IsNullOrWhiteSpace(environmentVariable))
            {
                string environmentApiKey = Environment.GetEnvironmentVariable(environmentVariable.Trim());
                if (!string.IsNullOrWhiteSpace(environmentApiKey))
                {
                    return environmentApiKey.Trim();
                }
            }

            return string.IsNullOrWhiteSpace(settings.RuntimeRemoteApiKey)
                ? string.Empty
                : settings.RuntimeRemoteApiKey.Trim();
        }

        public static CreatureDecision ParseDecision(string content)
        {
            bool parsed = LlmResponseParser.TryParseDecision(content, out CreatureDecision decision, out string reason, out _);
            if (!parsed && !string.IsNullOrWhiteSpace(reason))
            {
                Debug.LogWarning("LLM decision parse failed: " + reason);
            }

            return decision;
        }

        private static string BuildRawSummary(string content)
        {
            string summary = Summarize(content, 240);
            return string.IsNullOrWhiteSpace(summary) ? string.Empty : $" 原始摘要：{summary}";
        }

        private static string Summarize(string content, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            string cleaned = content.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return cleaned.Length <= maxCharacters ? cleaned : cleaned.Substring(0, maxCharacters - 1) + "...";
        }

        [Serializable]
        private class ChatRequest
        {
            public string model;
            public float temperature;
            public int max_tokens;
            public bool stream;
            public ChatMessage[] messages;
        }

        [Serializable]
        private class ChatMessage
        {
            public string role;
            public string content;
        }

    }
}
