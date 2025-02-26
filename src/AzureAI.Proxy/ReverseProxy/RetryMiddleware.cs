using Yarp.ReverseProxy.Model;

namespace AzureAI.Proxy.ReverseProxy;

public class RetryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger _logger;

    public RetryMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
    {
        _next = next;
        _logger = loggerFactory.CreateLogger<RetryMiddleware>();
    }

    /// <summary>
    /// The code in this method is based on comments from https://github.com/microsoft/reverse-proxy/issues/56
    /// When YARP natively supports retries, this will probably be greatly simplified.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        context.Request.EnableBuffering();

        var shouldRetry = true;
        var retryCount = 0;

        while (shouldRetry)
        {
            var reverseProxyFeature = context.GetReverseProxyFeature();
            if (reverseProxyFeature == null)
            {
                _logger.LogWarning("ReverseProxyFeature is null, cannot continue with retry logic");
                await _next(context);
                return;
            }

            var selectedDestination = PickOneDestination(reverseProxyFeature);
            if (selectedDestination == null)
            {
                _logger.LogWarning("No destination available, cannot continue with retry logic");
                await _next(context);
                return;
            }

            reverseProxyFeature.AvailableDestinations = new List<DestinationState>{ selectedDestination };

            if (retryCount > 0)
            {
                //If this is a retry, we must reset the request body to initial position and clear the current response
                context.Request.Body.Position = 0;
                reverseProxyFeature.ProxiedDestination = null;
                context.Response.Clear();
            }

            await _next(context);

            var statusCode = context.Response.StatusCode;
            var atLeastOneBackendHealthy = GetNumberHealthyEndpoints(context) > 0;
            retryCount++;

            shouldRetry = (statusCode is 429 or >= 500) && atLeastOneBackendHealthy;
        }
    }

    private static int GetNumberHealthyEndpoints(HttpContext context)
    {
        var reverseProxyFeature = context.GetReverseProxyFeature();
        if (reverseProxyFeature?.AllDestinations == null)
        {
            return 0;
        }
        
        return reverseProxyFeature.AllDestinations.Count(m => 
            m != null && m.Health.Passive is DestinationHealth.Healthy or DestinationHealth.Unknown);
    }


    /// <summary>
    /// The native YARP ILoadBalancingPolicy interface does not play well with HTTP retries, that's why we're adding this custom load-balancing code.
    /// This needs to be reevaluated to a ILoadBalancingPolicy implementation when YARP supports natively HTTP retries.
    /// </summary>
    private DestinationState? PickOneDestination(IReverseProxyFeature reverseProxyFeature)
    {
        var allDestinations = reverseProxyFeature?.AllDestinations;
        if (allDestinations == null || allDestinations.Count == 0)
        {
            _logger.LogWarning("No destinations available in the proxy feature");
            return null;
        }
        
        var selectedPriority = int.MaxValue;
        var availableBackends = new List<int>();

        for (var i = 0; i < allDestinations.Count; i++)
        {
            var currentDestination = allDestinations[i];
            if (currentDestination == null || currentDestination.Model?.Config == null)
            {
                continue;
            }

            if (currentDestination.Health.Passive != DestinationHealth.Unhealthy)
            {
                // Check if metadata and priority key exist
                if (currentDestination.Model.Config.Metadata != null && 
                    currentDestination.Model.Config.Metadata.TryGetValue("priority", out var priorityValue) &&
                    !string.IsNullOrEmpty(priorityValue) &&
                    int.TryParse(priorityValue, out var destinationPriority))
                {
                    if (destinationPriority < selectedPriority)
                    {
                        selectedPriority = destinationPriority;
                        availableBackends.Clear();
                        availableBackends.Add(i);
                    }
                    else if (destinationPriority == selectedPriority)
                    {
                        availableBackends.Add(i);
                    }
                }
                else
                {
                    // Default priority if not specified
                    availableBackends.Add(i);
                }
            }
        }
        
        int backendIndex;
        if (availableBackends.Count == 0)
        {
            //Returns a random backend if all backends are unhealthy
            _logger.LogWarning($"All backends are unhealthy or have invalid configuration. Picking a random backend...");
            
            // Ensure there's at least one destination
            if (allDestinations.Count > 0)
            {
                backendIndex = Random.Shared.Next(0, allDestinations.Count);
                var pickedDestination = allDestinations[backendIndex];
                var address = pickedDestination?.Model?.Config?.Address ?? "unknown address";
                _logger.LogInformation($"Picked backend: {address}");
                return pickedDestination;
            }
            
            return null;
        }
        
        
        if (availableBackends.Count == 1)
        {
            //Returns the only available backend if we have only one available
            backendIndex = availableBackends[0];
        }
        else
        {
            //Returns a random backend from the list if we have more than one available with the same priority
            backendIndex = availableBackends[Random.Shared.Next(0, availableBackends.Count)];
        }

        var finalDestination = allDestinations[backendIndex];
        if (finalDestination?.Model?.Config != null)
        {
            _logger.LogInformation($"Picked backend: {finalDestination.Model.Config.Address}");
        }

        return finalDestination;
    }
}
