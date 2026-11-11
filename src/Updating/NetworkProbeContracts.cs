#nullable enable

namespace ModConfigEnhance.Updating;

public sealed class ProbeRequest
{
    public UpdateNetworkOptions Network { get; set; } = new();
    public string Namespace { get; set; } = "";
    public string Package { get; set; } = "";
}

public sealed class ProbeResult
{
    public bool Complete { get; set; }
    public ProbeEndpoint Store { get; set; } = new();
    public ProbeEndpoint Download { get; set; } = new();
}

public sealed class ProbeEndpoint
{
    public bool Success { get; set; }
    public long Milliseconds { get; set; }
    public string Error { get; set; } = "";
}
