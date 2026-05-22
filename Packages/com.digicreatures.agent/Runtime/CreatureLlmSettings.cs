using System;
using UnityEngine;

namespace DigiCreatures
{
    [CreateAssetMenu(menuName = "数字生物/模型配置", fileName = "CreatureLlmSettings")]
    public class CreatureLlmSettings : ScriptableObject
    {
        public string backend = "ollama";
        public string localEndpoint = "http://localhost:11434/v1/chat/completions";
        public string localModel = "qwen2.5:3b";
        public string localStartCommand = "ollama serve";
        public string remoteEndpoint = "https://api.openai.com/v1/chat/completions";
        public string remoteModel = "gpt-4.1-mini";
        public string remoteApiKeyEnvironmentVariable = "OPENAI_API_KEY";
        [NonSerialized] private string runtimeRemoteApiKey;
        public float temperature = 0.7f;
        public int requestTimeoutSeconds = 60;

        public bool UseRemoteBackend =>
            string.Equals(backend, "remote", StringComparison.OrdinalIgnoreCase);

        public string Endpoint => UseRemoteBackend ? remoteEndpoint : localEndpoint;

        public string Model => UseRemoteBackend ? remoteModel : localModel;

        public string ApiKeyEnvironmentVariable => UseRemoteBackend ? remoteApiKeyEnvironmentVariable : string.Empty;

        public string RuntimeRemoteApiKey
        {
            get => runtimeRemoteApiKey;
            set => runtimeRemoteApiKey = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
