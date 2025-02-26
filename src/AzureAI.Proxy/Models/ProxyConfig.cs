namespace AzureAI.Proxy.Models;

public class ProxyConfig
{
    public List<Route> Routes { get; set; } = new List<Route>();
}

public class Route
{
    public string? Name { get; set; }
    public List<Endpoint> Endpoints { get; set; } = new List<Endpoint>();
}

public class Endpoint
{
    public string? Address { get; set; }
    public int Priority { get; set; }
}
