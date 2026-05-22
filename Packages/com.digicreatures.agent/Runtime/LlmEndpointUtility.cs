using System;

namespace DigiCreatures
{
    public static class LlmEndpointUtility
    {
        public static string NormalizeChatCompletionsEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return string.Empty;
            }

            string trimmed = endpoint.Trim().TrimEnd('/');
            if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed + "/chat/completions";
            }

            return trimmed + "/v1/chat/completions";
        }
    }
}
