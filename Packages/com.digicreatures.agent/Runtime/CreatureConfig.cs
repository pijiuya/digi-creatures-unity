using System;

namespace DigiCreatures
{
    [Serializable]
    public class CreatureConfig
    {
        public string backend = "local";
        public string localEndpoint = "http://localhost:11434/v1/chat/completions";
        public string onlineEndpoint = "https://api.openai.com/v1/chat/completions";
        public string localModel = "llama3.1";
        public string onlineModel = "gpt-4.1-mini";
        public string onlineApiKeyEnvironmentVariable = "OPENAI_API_KEY";
        public float temperature = 0.7f;
        public float decisionIntervalSeconds = 12f;
        public int requestTimeoutSeconds = 20;
        public int recentMemoryLimit = 8;
        public int summaryEveryEvents = 12;

        public bool UseOnlineBackend =>
            string.Equals(backend, "online", StringComparison.OrdinalIgnoreCase);

        public string Endpoint => UseOnlineBackend ? onlineEndpoint : localEndpoint;

        public string Model => UseOnlineBackend ? onlineModel : localModel;
    }
}
