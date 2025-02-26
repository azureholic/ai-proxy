using AzureAI.Proxy.Models;
using Yarp.ReverseProxy.Configuration;
using System.Text.Json;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Forwarder;

namespace AzureAI.Proxy.ReverseProxy;

public class ProxyConfiguration
{
    private readonly ProxyConfig _proxyConfig;

    public ProxyConfiguration(string configJson)
    {
        if (string.IsNullOrEmpty(configJson))
        {
            throw new ArgumentNullException(nameof(configJson), "Configuration JSON cannot be null or empty");
        }

        JsonSerializerOptions options = new();
        options.PropertyNameCaseInsensitive = true;

        _proxyConfig = JsonSerializer.Deserialize<ProxyConfig>(configJson, options) ?? new ProxyConfig();
    }

    public IReadOnlyList<RouteConfig> GetRoutes()
    {
        List<RouteConfig> routes = new();

        if (_proxyConfig.Routes == null)
        {
            return routes.AsReadOnly();
        }

        foreach (var route in _proxyConfig.Routes)
        {
            if (string.IsNullOrEmpty(route.Name))
            {
                continue; // Skip routes without names
            }

            RouteConfig routeConfig = new()
            {
                RouteId = route.Name,
                ClusterId = route.Name,
                Match = new RouteMatch()
                {
                    Path = $"openai/deployments/{route.Name}/" + "{**catch-all}"
                }
            };

            routes.Add(routeConfig);
        }

        return routes.AsReadOnly();
    }

    public IReadOnlyList<ClusterConfig> GetClusters()
    {
        List<ClusterConfig> clusters = new();
        
        if (_proxyConfig.Routes == null)
        {
            return clusters.AsReadOnly();
        }
        
        foreach (var route in _proxyConfig.Routes)
        {
            if (string.IsNullOrEmpty(route.Name) || route.Endpoints == null || !route.Endpoints.Any())
            {
                continue; // Skip invalid routes
            }

            Dictionary<string, DestinationConfig> destinations = new();

            foreach (var destination in route.Endpoints)
            {
                if (string.IsNullOrEmpty(destination.Address))
                {
                    continue; // Skip endpoints with no address
                }

                Dictionary<string, string> metadata = new()
                {
                    { "url", destination.Address },
                    { "priority", destination.Priority.ToString() }
                };

                DestinationConfig destinationConfig = new()
                {
                    Address = destination.Address,
                    Metadata = metadata
                };

                destinations[destination.Address] = destinationConfig;
            }

            if (destinations.Count == 0)
            {
                continue; // Skip clusters with no destinations
            }

            ClusterConfig clusterConfig = new()
            {
                ClusterId = route.Name,
                Destinations = destinations,
                HealthCheck = new HealthCheckConfig
                {
                    Passive = new PassiveHealthCheckConfig
                    {
                        Enabled = true,
                        Policy = ThrottlingHealthPolicy.ThrottlingPolicyName
                    }
                },
                HttpRequest = new ForwarderRequestConfig()
            };

            clusters.Add(clusterConfig);
        }
        
        return clusters.AsReadOnly();
    }
}
