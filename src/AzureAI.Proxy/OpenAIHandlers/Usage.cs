using System.Text.Json.Nodes;
using AzureAI.Proxy.Models;

namespace AzureAI.Proxy.OpenAIHandlers
{
    public static class Usage
    {
        public static void Handle(JsonNode jsonNode, ref LogAnalyticsRecord record)
        {
            // Check if model node exists before accessing it
            var modelNode = jsonNode["model"];
            if (modelNode != null)
            {
                record.Model = modelNode.ToString();
            }
            
            // Check if usage node exists
            var usage = jsonNode["usage"];
            if (usage == null)
            {
                // If usage information is not available, we can't extract token counts
                return;
            }
            
            // Handle completion tokens (may not exist in all responses)
            var completionTokensNode = usage["completion_tokens"];
            if (completionTokensNode != null)
            {
                if (int.TryParse(completionTokensNode.ToString(), out int completionTokens))
                {
                    record.OutputTokens = completionTokens;
                }
            }
            
            // Handle prompt tokens (should exist, but verify)
            var promptTokensNode = usage["prompt_tokens"];
            if (promptTokensNode != null)
            {
                if (int.TryParse(promptTokensNode.ToString(), out int promptTokens))
                {
                    record.InputTokens = promptTokens;
                }
            }
        }
    }
}
