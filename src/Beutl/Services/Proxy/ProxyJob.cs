using Beutl.Configuration;

namespace Beutl.Services.Proxy;

public sealed class ProxyJob
{
    public ProxyJob(string originalPath, ProxyPresetKind preset)
    {
        OriginalPath = originalPath;
        Preset = preset;
    }

    public string OriginalPath { get; }

    public ProxyPresetKind Preset { get; }

    public double Progress { get; internal set; }

    public ProxyJobState State { get; internal set; } = ProxyJobState.Pending;

    public string? ErrorMessage { get; internal set; }
}

public enum ProxyJobState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed record ProxyGenerationResult(bool Success, string? ProxyPath, string? ErrorMessage);
