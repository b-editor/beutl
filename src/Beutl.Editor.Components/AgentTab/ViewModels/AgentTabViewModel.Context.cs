using Beutl.Agents;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    public bool CanManageContext => CanManageSelected && _controller?.CanConfigure == true;
    public bool ShowContextMeter => Transcript.ContextUsage is not null;
    public double ContextMeterProgress => Transcript.ContextUsage?.UsedTokens is { } used && ContextWindow is > 0
        ? Math.Clamp((double)used / ContextWindow.Value, 0, 1) : 0;
    public bool IsContextCritical => ContextMeterProgress >= .95;
    public bool IsContextWarning => ContextMeterProgress is >= .80 and < .95;
    public string ContextMeterLabel => Transcript.ContextUsage?.UsedTokens switch
    {
        0 => 0d.ToString("P0", CultureInfo.CurrentCulture),
        not null when ContextWindow is > 0 => ContextMeterProgress.ToString("P0", CultureInfo.CurrentCulture),
        _ => "—"
    };

    public string ContextMeterSummary => string.Format(CultureInfo.CurrentCulture, AgentStrings.ContextMeterSummary,
        FormatTokenCount(Transcript.ContextUsage?.UsedTokens), FormatTokenCount(ContextWindow), ContextMeterLabel);
    public string ContextMeterInput => FormatMeterLine(AgentStrings.TurnInputTokens, Transcript.ContextUsage?.InputTokens);
    public string ContextMeterOutput => FormatMeterLine(AgentStrings.TurnOutputTokens, Transcript.ContextUsage?.OutputTokens);
    public string ContextMeterCache => FormatMeterLine(AgentStrings.TurnCachedInputTokens, Transcript.ContextUsage?.CachedInputTokens);

    private long? ContextWindow => Transcript.ContextUsage?.ContextWindowTokens ?? CurrentModel()?.ContextWindowTokens;

    public Task ClearContextAsync()
        => ConfigureThreadAsync((controller, ct) => controller.ClearContextAsync(ct));

    public Task CompactContextAsync() => SelectedProvider is { } provider
        ? ConfigureThreadAsync((controller, ct) => controller.CompactContextAsync(provider.Descriptor, ct))
        : Task.CompletedTask;

    private static string? ContextCommand(string? prompt) => prompt?.Trim().ToLowerInvariant() switch
    {
        "/clear" => "/clear",
        "/compact" => "/compact",
        _ => null
    };

    private static string FormatTokenCount(long? count) => count?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";

    private static string FormatMeterLine(string format, long? count)
        => string.Format(CultureInfo.CurrentCulture, format, FormatTokenCount(count));

    private void NotifyContextMeter() => Notify(
        nameof(ShowContextMeter),
        nameof(ContextMeterProgress),
        nameof(ContextMeterLabel),
        nameof(IsContextCritical),
        nameof(IsContextWarning),
        nameof(ContextMeterSummary),
        nameof(ContextMeterInput),
        nameof(ContextMeterOutput),
        nameof(ContextMeterCache));
}
