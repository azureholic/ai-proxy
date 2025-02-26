using Azure;
using Azure.Core;
using Azure.Monitor.Ingestion;
using System.Text.Json;

namespace AzureAI.Proxy.Services
{
    public class LogIngestionService : ILogIngestionService
    {
        private readonly IConfiguration _config;
        private readonly LogsIngestionClient _logsIngestionClient;
        private readonly ILogger _logger;
        
        

        public LogIngestionService(
            LogsIngestionClient logsIngestionClient,
            IConfiguration config
,           ILogger<LogIngestionService> logger
            )
        {
            _config = config;
            _logsIngestionClient = logsIngestionClient;
            _logger = logger;
         }

        public async Task LogAsync(LogAnalyticsRecord record)
        {
            try
            {
                _logger.LogInformation("Writing logs...");
                var jsonContent = new List<LogAnalyticsRecord>();
                jsonContent.Add(record);

                //RBAC Monitoring Metrics Publisher needed
                RequestContent content = RequestContent.Create(JsonSerializer.Serialize(jsonContent));
                
                // Get configuration values with null checks
                var ruleIdValue = _config.GetSection("AzureMonitor")["DataCollectionRuleImmutableId"];
                var streamValue = _config.GetSection("AzureMonitor")["DataCollectionRuleStream"];
                
                if (string.IsNullOrEmpty(ruleIdValue))
                {
                    throw new InvalidOperationException("AzureMonitor:DataCollectionRuleImmutableId configuration is missing or empty");
                }
                
                if (string.IsNullOrEmpty(streamValue))
                {
                    throw new InvalidOperationException("AzureMonitor:DataCollectionRuleStream configuration is missing or empty");
                }
                
                Response response = await _logsIngestionClient.UploadAsync(ruleIdValue, streamValue, content);

            }
            catch (Exception ex)
            {
                _logger.LogError($"Writing to LogAnalytics Failed: {ex.Message}");
            }


        }
    }
}
