using Azure.Core;
using AzureAI.Proxy.OpenAIHandlers;
using AzureAI.Proxy.Services;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace AzureAI.Proxy.ReverseProxy;

internal class OpenAIChargebackTransformProvider : ITransformProvider
{
    private readonly IConfiguration _config;
    private readonly IManagedIdentityService _managedIdentityService;
    private readonly ILogIngestionService _logIngestionService;
   
    private string accessToken = "";

    private TokenCredential _managedIdentityCredential;

    public OpenAIChargebackTransformProvider(
        IConfiguration config, 
        IManagedIdentityService managedIdentityService,
        ILogIngestionService logIngestionService)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _managedIdentityService = managedIdentityService ?? throw new ArgumentNullException(nameof(managedIdentityService));
        _logIngestionService = logIngestionService ?? throw new ArgumentNullException(nameof(logIngestionService));
               
        _managedIdentityCredential = _managedIdentityService.GetTokenCredential() ?? 
            throw new InvalidOperationException("Failed to get token credential from managed identity service");
    }

    public void ValidateRoute(TransformRouteValidationContext context) { return; }

    public void ValidateCluster(TransformClusterValidationContext context) { return; }
    
    public void Apply(TransformBuilderContext context)
    {
        context.AddRequestTransform(async requestContext => {
            //enable buffering allows us to read the requestbody twice (one for forwarding, one for analysis)
            requestContext.HttpContext.Request.EnableBuffering();

            //check accessToken before replacing the Auth Header
            if (String.IsNullOrEmpty(accessToken) || OpenAIAccessToken.IsTokenExpired(accessToken))
            {
                accessToken = await OpenAIAccessToken.GetAccessTokenAsync(_managedIdentityCredential, CancellationToken.None);
            }

            //replace auth header with the accesstoken of the managed indentity of the proxy
            requestContext.ProxyRequest.Headers.Remove("api-key");
            requestContext.ProxyRequest.Headers.Remove("Authorization");
            requestContext.ProxyRequest.Headers.Add("Authorization", $"Bearer {accessToken}");

            // Read the request body as a string
            var requestBody = requestContext.HttpContext.Request.Body;
            if (requestBody != null)
            {
                using (var reader = new StreamReader(requestBody, Encoding.UTF8, leaveOpen: true))
                {
                    var requestBodyString = await reader.ReadToEndAsync();
                    // Reset the stream position to the beginning
                    requestBody.Position = 0;
                    
                    if (!string.IsNullOrEmpty(requestBodyString))
                    {
                        try
                        {
                            // Deserialize the JSON string into a JsonNode
                            JsonNode? jsonNode = JsonSerializer.Deserialize<JsonNode>(requestBodyString);
                            
                            // Check if the JSON node and stream property exist and is true
                            if (jsonNode != null && 
                                jsonNode["stream"] != null &&
                                jsonNode["stream"]?.ToString() == "true")
                            {
                                //is streaming Request?
                                //if yes and usage is not included, add it to the request
                                bool includeUsage = false;
                                
                                var streamOptions = jsonNode["stream_options"];
                                if (streamOptions != null)
                                {
                                    var includeUsageNode = streamOptions["include_usage"];
                                    if (includeUsageNode != null)
                                    {
                                        includeUsage = includeUsageNode.ToString() == "true";
                                    }
                                }
                                
                                if (!includeUsage)
                                {
                                    // Add property to the jsonNode
                                    if (jsonNode["stream_options"] is JsonObject chatCompletionStreamOptions)
                                    {
                                        chatCompletionStreamOptions["include_usage"] = true;
                                    }
                                    else
                                    {
                                        jsonNode["stream_options"] = new JsonObject
                                        {
                                            ["include_usage"] = true
                                        };
                                    }
                                    
                                    // Serialize the JsonNode back to a JSON string
                                    var updatedRequestBodyString = jsonNode.ToJsonString();
                                    var requestContent = new StringContent(updatedRequestBodyString, Encoding.UTF8, "application/json");
                                    //update the proxy request
                                    requestContext.ProxyRequest.Content = requestContent;
                                }
                            }
                        }
                        catch (JsonException ex)
                        {
                            // Log but don't fail if JSON parsing fails
                            // This allows non-JSON requests to still be forwarded
                            System.Diagnostics.Debug.WriteLine($"Error parsing request JSON: {ex.Message}");
                        }
                    }
                }
            }
        });
        
        context.AddResponseTransform(async responseContext =>
        {
            //hit 429 or internal server error, can we retry on another node?
            if (responseContext.ProxyResponse?.StatusCode is HttpStatusCode.TooManyRequests
                or >= HttpStatusCode.InternalServerError)
            {
                var reverseProxyContext = responseContext.HttpContext.GetReverseProxyFeature();
                
                var canRetry = reverseProxyContext != null && 
                               reverseProxyContext.AllDestinations != null && 
                               reverseProxyContext.AllDestinations.Count(m =>
                                   m.Health.Passive != DestinationHealth.Unhealthy
                                   && reverseProxyContext.ProxiedDestination != null
                                   && m.DestinationId != reverseProxyContext.ProxiedDestination.DestinationId) > 0;

                if (canRetry)
                {
                    // Suppress the response body from being written when we will retry
                    responseContext.SuppressResponseBody = true;
                }
            }
            else if (responseContext.ProxyResponse?.Content != null)
            {
                var originalStream = await responseContext.ProxyResponse.Content.ReadAsStreamAsync();
                var stringBuilder = new StringBuilder();

                // Buffer for reading chunks
                byte[] buffer = new byte[8192];
                int bytesRead;

                // Read, inspect, and write the data in chunks - this is especially needed for streaming content
                while ((bytesRead = await originalStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    // Convert the chunk to a string for inspection
                    var chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    stringBuilder.Append(chunk);

                    // Write the unmodified chunk back to the response
                    await responseContext.HttpContext.Response.Body.WriteAsync(buffer, 0, bytesRead);
                }

                //flush any remaining content to the client
                await responseContext.HttpContext.Response.CompleteAsync();

                //now perform the analysis and create a log record
                var record = new LogAnalyticsRecord
                {
                    TimeGenerated = DateTime.UtcNow,
                    Consumer = "Unknown Consumer"
                };

                var consumerHeader = responseContext.HttpContext.Request.Headers["X-Consumer"].ToString();
                if (!string.IsNullOrEmpty(consumerHeader))
                {
                    record.Consumer = consumerHeader;
                }

                var capturedBody = stringBuilder.ToString();
                if (!string.IsNullOrEmpty(capturedBody))
                {
                    var chunks = capturedBody.Split("data:");
                    foreach (var chunk in chunks)
                    {
                        var trimmedChunck = chunk.Trim();
                        if (!string.IsNullOrEmpty(trimmedChunck) && trimmedChunck != "[DONE]")
                        {
                            try
                            {
                                JsonNode? jsonNode = JsonSerializer.Deserialize<JsonNode>(trimmedChunck);
                                
                                if (jsonNode != null)
                                {
                                    if (jsonNode["error"] != null)
                                    {
                                        Error.Handle(jsonNode);
                                    }
                                    else 
                                    {
                                        var objectNode = jsonNode["object"];
                                        if (objectNode != null)
                                        {
                                            string objectValue = objectNode.ToString();

                                            switch (objectValue)
                                            {
                                                case "chat.completion":
                                                    Usage.Handle(jsonNode, ref record);
                                                    record.ObjectType = objectValue;
                                                    break;
                                                case "chat.completion.chunk":
                                                    //does jsonNode contain Usage?
                                                    //Usage is the last chunk before "DONE"
                                                    if (jsonNode["usage"] != null)
                                                    {
                                                        Usage.Handle(jsonNode, ref record);
                                                        record.ObjectType = objectValue;
                                                    }
                                                    break;
                                                case "list":
                                                    var dataNode = jsonNode["data"];
                                                    if (dataNode != null && dataNode.AsArray().Count > 0) 
                                                    {
                                                        var firstItem = dataNode[0];
                                                        if (firstItem != null)
                                                        {
                                                            var firstItemObjectNode = firstItem["object"];
                                                            if (firstItemObjectNode != null)
                                                            {
                                                                record.ObjectType = firstItemObjectNode.ToString();
                                                                //it's an embedding
                                                                Usage.Handle(jsonNode, ref record);
                                                            }
                                                        }
                                                    }
                                                    break;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (JsonException ex)
                            {
                                // Log but continue with other chunks if one fails to parse
                                System.Diagnostics.Debug.WriteLine($"Error parsing response chunk: {ex.Message}");
                            }
                            catch (InvalidOperationException ex)
                            {
                                // This can happen if we try to access a property that doesn't exist
                                System.Diagnostics.Debug.WriteLine($"Invalid operation while processing chunk: {ex.Message}");
                            }
                        }
                    }
                }

                record.TotalTokens = record.InputTokens + record.OutputTokens;
                await _logIngestionService.LogAsync(record);
            }
        });
    }
}
