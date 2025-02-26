using Azure.Core.Diagnostics;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using AzureAI.Proxy.ReverseProxy;
using AzureAI.Proxy.Services;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Yarp.ReverseProxy.Health;

var builder = WebApplication.CreateBuilder(args);

//Application Insights
var instanceId = Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME") ?? "local";

var resourceAttributes = new Dictionary<string, object> {
    { "service.name", "Proxy" },
    { "service.namespace", "AzureAI" },
    { "service.instance.id", instanceId }
};

builder.Services.AddOpenTelemetry().UseAzureMonitor();
builder.Services.ConfigureOpenTelemetryTracerProvider((sp, builder) =>
    builder.ConfigureResource(resourceBuilder =>
        resourceBuilder.AddAttributes(resourceAttributes)));

//diagnostics for troubleshooting
if (builder.Environment.IsDevelopment())
{     
    using AzureEventSourceListener listener = AzureEventSourceListener.CreateConsoleLogger();
}

//Managed Identity Service
builder.Services.AddSingleton<IManagedIdentityService, ManagedIdentityService>();
// Build service provider only once to get the managed identity service


var serviceProvider = builder.Services.BuildServiceProvider();
var managedIdentityService = serviceProvider.GetService<IManagedIdentityService>();
if (managedIdentityService == null)
{
    throw new InvalidOperationException("Failed to resolve IManagedIdentityService");
}

//Azure App Configuration
var appConfigEndpoint = builder.Configuration["APPCONFIG_ENDPOINT"];
if (!string.IsNullOrEmpty(appConfigEndpoint))
{
    var tokenCredential = managedIdentityService.GetTokenCredential();
    if (tokenCredential == null)
    {
        throw new InvalidOperationException("Token credential is null");
    }
    
    builder.Configuration.AddAzureAppConfiguration(options =>
        options.Connect(new Uri(appConfigEndpoint), tokenCredential)
    );
}

var config = builder.Configuration;

// Add null check and default value for the DataCollectionEndpoint
var monitorSection = config.GetSection("AzureMonitor");
var endpointString = monitorSection["DataCollectionEndpoint"];
if (string.IsNullOrEmpty(endpointString))
{
    throw new InvalidOperationException("AzureMonitor:DataCollectionEndpoint configuration is missing or empty");
}
var endpoint = new Uri(endpointString);

builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddLogsIngestionClient(endpoint);
    var credential = managedIdentityService.GetTokenCredential();
    if (credential == null)
    {
        throw new InvalidOperationException("Token credential is null");
    }
    clientBuilder.UseCredential(credential);
});

//Log Ingestion for charge back data
builder.Services.AddTransient<ILogIngestionService, LogIngestionService>();

//Setup Reverse Proxy
var proxyConfigPath = config["AzureAIProxy:ProxyConfig"];
if (string.IsNullOrEmpty(proxyConfigPath))
{
    throw new InvalidOperationException("AzureAIProxy:ProxyConfig configuration is missing or empty");
}
var proxyConfig = new ProxyConfiguration(proxyConfigPath);
var routes = proxyConfig.GetRoutes() ?? throw new InvalidOperationException("Proxy routes cannot be null");
var clusters = proxyConfig.GetClusters() ?? throw new InvalidOperationException("Proxy clusters cannot be null");

builder.Services.AddSingleton<IPassiveHealthCheckPolicy, ThrottlingHealthPolicy>();

builder.Services.AddReverseProxy()
    .LoadFromMemory(routes, clusters)
    .ConfigureHttpClient((sp, options) =>
    {
        //decompress the Response so we can read it
        options.AutomaticDecompression = System.Net.DecompressionMethods.All;
    })
    .AddTransforms<OpenAIChargebackTransformProvider>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapReverseProxy(m =>
{
    m.UseMiddleware<RetryMiddleware>();
    m.UsePassiveHealthChecks();
});

app.Run();









