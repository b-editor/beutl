using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Beutl.Agents;
using FluentAvalonia.UI.Controls;
using FluentIcons.Common;
using LiveMarkdown.Avalonia;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal abstract class AgentBindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Notify(name);
        return true;
    }

    protected void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal abstract class AgentTimelineItemViewModel(Guid? turnId) : AgentBindable
{
    public Guid? TurnId { get; } = turnId;
}

internal abstract class AgentMessageItem(Guid? turnId, DateTimeOffset timestamp) : AgentTimelineItemViewModel(turnId)
{
    public DateTimeOffset Timestamp { get; } = timestamp;
    public string TimeLabel => Timestamp.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
    public string TimeDetail => Timestamp.ToLocalTime().ToString("F", CultureInfo.CurrentCulture);
    public bool HasTurn => TurnId.HasValue;
    public abstract string Text { get; }
}

internal sealed class AgentUserItem(AgentUserMessage entry) : AgentMessageItem(entry.TurnId, entry.Timestamp), IDisposable
{
    public AgentUserMessage Entry { get; } = entry;
    public override string Text => Entry.Text;
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public IReadOnlyList<AgentMessageAttachmentItem> Attachments { get; } =
        entry.Attachments?.Select(attachment => new AgentMessageAttachmentItem(attachment)).ToArray() ?? [];

    public void Dispose()
    {
        foreach (var attachment in Attachments)
            attachment.Preview?.Dispose();
    }
}

internal sealed class AgentMessageAttachmentItem(AgentUserAttachment entry) : AgentBindable
{
    public string Label => entry.Label;
    public string Path => entry.Path;
    public string? Detail => entry.Detail;
    public AgentImagePreview? Preview { get; private set => Set(ref field, value); } =
        entry.IsImage ? AgentImagePreview.FromFile(entry.Path, entry.Label) : null;
    public bool HasPreview => Preview is not null;
    public Icon Icon => entry.IsImage ? Icon.Image : Icon.Document;

    public void RemoveUnavailablePreview()
    {
        var preview = Preview;
        Preview = null;
        Notify(nameof(HasPreview));
        preview?.Dispose();
    }
}

internal sealed class AgentAssistantItem(Guid? turnId, DateTimeOffset timestamp) : AgentMessageItem(turnId, timestamp)
{
    public ObservableStringBuilder Markdown { get; } = new();
    public override string Text => Markdown.ToString();
    public bool ReceivedCompletion { get; private set; }
    public bool IsFinalTurnResponse { get; private set => Set(ref field, value); }
    public bool CanCopy => IsFinalTurnResponse && !string.IsNullOrWhiteSpace(Text);

    public void Append(string text)
    {
        Markdown.Append(text);
        Notify(nameof(Text));
    }

    public void Complete(string text)
    {
        ReceivedCompletion = true;
        if (Text != text)
        {
            Markdown.Clear();
            Append(text);
        }
    }

    public void MarkFinal()
    {
        IsFinalTurnResponse = true;
        Notify(nameof(CanCopy));
    }
}

internal sealed class AgentReasoningItem(Guid? turnId, DateTimeOffset startedAt) : AgentTimelineItemViewModel(turnId)
{
    public string Summary { get; private set => Set(ref field, value); } = AgentStrings.Thinking;
    public bool IsFinished { get; private set; }

    public string Text
    {
        get;
        set => Set(ref field, value);
    } = "";

    public void Finish(DateTimeOffset timestamp)
    {
        if (IsFinished)
            return;
        IsFinished = true;
        Summary = string.Format(CultureInfo.CurrentCulture, AgentStrings.ThoughtForSeconds,
            Math.Max(0, (timestamp - startedAt).TotalSeconds));
    }
}

internal sealed class AgentToolItem : AgentTimelineItemViewModel, IDisposable
{
    private const string BeutlPrefix = "mcp__beutl__";

    private AgentToolStatus _status = AgentToolStatus.Running;
    private AgentSubagent? _subagent;
    private string? _backgroundTitle;
    private bool _isBackground;
    private string? _scopedActivity;
    private bool _hasImageOutput;
    private bool _stopping;

    public AgentToolItem(AgentToolCallStarted entry) : base(entry.TurnId)
    {
        ToolCallId = entry.ToolCallId;
        Describe(entry);
        Children.CollectionChanged += (_, _) => NotifyHierarchy();
    }

    public ObservableCollection<AgentTimelineItemViewModel> Children { get; } = [];
    public string ToolCallId { get; }
    internal AgentToolItem? Parent { get; set; }
    internal AgentToolGroupItem? Group { get; set; }
    public int ChildToolCount => Children.Count(item => item is AgentToolItem);
    public string ChildToolCountLabel => string.Format(CultureInfo.CurrentCulture, AgentStrings.SubagentToolCount, ChildToolCount);
    public bool HasChildTools => ChildToolCount > 0;
    public bool IsBackground => _isBackground;
    public bool IsSubagent => _subagent is not null || ToolName is "Agent" or "Task" || (!IsBackground && Children.Count > 0);
    public bool IsBackgroundWork => IsSubagent || IsBackground;
    public bool ShowStop => StopAction is not null && IsBackgroundWork && IsInProgress;
    public bool CanStop => ShowStop && !_stopping;
    internal Func<AgentToolItem, Task>? StopAction
    {
        get;
        set
        {
            if (Set(ref field, value))
                NotifyStop();
        }
    }
    public string Title => _subagent?.Description ?? _backgroundTitle ?? _subagent?.Role ?? (IsSubagent ? AgentStrings.Subagent : ToolName);
    public string WorkKindLabel => IsSubagent ? _subagent?.Role ?? AgentStrings.Subagent : AgentStrings.BackgroundCommand;
    public string WorkPreview => _scopedActivity ?? ActivitySummary ?? Status;
    public bool CanExpand => HasDetails || IsBackgroundWork || Children.Count > 0;
    public bool CanGroup => TurnId.HasValue && Parent is null && !IsInProgress && !IsBackgroundWork && !_hasImageOutput;
    public bool IsCommand => ToolName is "Codex command" or "Bash" or "PowerShell" or "Terminal";
    public bool IsInspection => ToolName is "Read" or "Glob" or "Grep" or "LS" or "WebSearch" or "WebFetch"
        || (string.Equals(ServerName, "beutl", StringComparison.OrdinalIgnoreCase)
            && (ToolName.StartsWith("get_", StringComparison.Ordinal) || ToolName.StartsWith("list_", StringComparison.Ordinal)
                || ToolName.StartsWith("inspect_", StringComparison.Ordinal)));
    public Icon ToolIcon => IsSubagent ? Icon.People
        : IsCommand ? Icon.WindowConsole
        : IsInspection ? Icon.Search
        : _hasImageOutput ? Icon.Image
        : ToolName is "Edit" or "Write" or "File change" ? Icon.Edit
        : Icon.Wrench;

    public string ToolName
    {
        get;
        private set => Set(ref field, value);
    } = "";

    public string? ServerName
    {
        get;
        private set => Set(ref field, value);
    }

    public string? Input
    {
        get;
        private set => Set(ref field, value);
    }
    public string? Output
    {
        get;
        private set => Set(ref field, value);
    }

    public IReadOnlyList<AgentImagePreview> Images
    {
        get;
        private set => Set(ref field, value);
    } = [];

    public string Status
    {
        get;
        private set => Set(ref field, value);
    } = AgentStrings.Running;

    public bool IsRunning => _status == AgentToolStatus.Running;
    public bool IsInProgress => _status is AgentToolStatus.Running or AgentToolStatus.Pending;
    public bool IsSuccessful => _status == AgentToolStatus.Completed;
    public bool IsFailed => _status == AgentToolStatus.Failed;
    public bool HasDetails => !string.IsNullOrWhiteSpace(Input) || !string.IsNullOrWhiteSpace(Output);

    public string? ActivitySummary => Preview(Output);

    public async Task StopAsync()
    {
        if (!CanStop || StopAction is not { } stop)
            return;
        _stopping = true;
        NotifyStop();
        try
        {
            await stop(this);
        }
        finally
        {
            _stopping = false;
            NotifyStop();
        }
    }

    public void Describe(AgentToolCallStarted entry)
    {
        bool beutlTool = entry.ToolName.StartsWith(BeutlPrefix, StringComparison.Ordinal);
        ToolName = beutlTool ? entry.ToolName[BeutlPrefix.Length..] : entry.ToolName;
        ServerName = entry.McpServerName ?? (beutlTool ? "beutl" : null);
        if (entry.Input is { } input)
            Input = input.ValueKind == JsonValueKind.String ? input.GetString() : input.GetRawText();
        if (entry.Subagent is { } subagent)
            _subagent = new AgentSubagent(subagent.Role ?? _subagent?.Role, subagent.Description ?? _subagent?.Description);
        _isBackground |= entry.IsBackground;
        _backgroundTitle = entry.BackgroundTitle ?? _backgroundTitle;
        Notify(nameof(HasDetails));
        NotifyHierarchy();
    }

    private void NotifyHierarchy()
    {
        NotifyStop();
        Notify(nameof(IsSubagent));
        Notify(nameof(IsBackgroundWork));
        Notify(nameof(CanExpand));
        Notify(nameof(Title));
        Notify(nameof(WorkKindLabel));
        Notify(nameof(HasChildTools));
        Notify(nameof(ChildToolCount));
        Notify(nameof(ChildToolCountLabel));
    }

    public void UpdateScopedActivity(string text)
    {
        if (Preview(text) is { } activity && activity != _scopedActivity)
        {
            _scopedActivity = activity;
            Notify(nameof(WorkPreview));
        }
    }

    private static string? Preview(string? text)
    {
        var preview = text.AsSpan().TrimStart();
        int lineBreak = preview.IndexOfAny('\r', '\n');
        if (lineBreak >= 0)
            preview = preview[..lineBreak];
        return preview.IsEmpty ? null : preview[..Math.Min(preview.Length, 160)].ToString();
    }

    public void FinishIfRunning(AgentToolStatus status)
    {
        if (_status is AgentToolStatus.Pending or AgentToolStatus.Running)
            Update(status);
    }

    public void Update(AgentToolStatus status, string? message = null)
    {
        _status = status;
        Status = status switch
        {
            AgentToolStatus.Pending => AgentStrings.Pending,
            AgentToolStatus.Running => AgentStrings.Running,
            AgentToolStatus.Completed => AgentStrings.Completed,
            AgentToolStatus.Failed => AgentStrings.Failed,
            _ => AgentStrings.Stopped
        };
        if (!string.IsNullOrEmpty(message))
            Output = message;
        NotifyActivity();
    }

    public void Complete(AgentToolCallCompleted completed)
    {
        Update(completed.Status);
        if (completed.Outputs is not { Count: > 0 } outputs)
            return;

        var images = new List<AgentImagePreview>();
        var text = new List<string>();
        foreach (var output in outputs)
        {
            if (output.Type == "image")
            {
                _hasImageOutput = true;
                if (AgentImagePreview.FromToolOutput(output, Title) is { } image)
                    images.Add(image);
                else
                    text.Add(AgentStrings.ToolImageUnavailable);
            }
            else if ((output.Text ?? output.Uri) is { Length: > 0 } value)
            {
                text.Add(value);
            }
        }
        Dispose();
        Images = images;
        if (text.Count > 0)
        {
            Output = string.Join(Environment.NewLine, text);
            NotifyActivity();
        }
    }

    private void NotifyActivity()
    {
        NotifyStop();
        Notify(nameof(ActivitySummary));
        Notify(nameof(WorkPreview));
        Notify(nameof(HasDetails));
        Notify(nameof(CanExpand));
        Notify(nameof(IsRunning));
        Notify(nameof(IsInProgress));
        Notify(nameof(IsSuccessful));
        Notify(nameof(IsFailed));
        Notify(nameof(ToolIcon));
    }

    private void NotifyStop()
    {
        Notify(nameof(ShowStop));
        Notify(nameof(CanStop));
    }

    public void Dispose()
    {
        foreach (var image in Images)
            image.Dispose();
    }
}

internal sealed class AgentToolGroupItem(Guid? turnId) : AgentTimelineItemViewModel(turnId)
{
    public ObservableCollection<AgentToolItem> Tools { get; } = [];
    public bool HasFailure => Tools.Any(tool => tool.IsFailed);
    public string Label
    {
        get
        {
            int commands = Tools.Count(tool => tool.IsCommand);
            string format = commands == Tools.Count ? AgentStrings.ToolGroupCommands
                : commands > 0 ? AgentStrings.ToolGroupMixed
                : Tools.All(tool => tool.IsInspection) ? AgentStrings.ToolGroupChecks
                : AgentStrings.ToolGroupTools;
            return string.Format(CultureInfo.CurrentCulture, format, commands > 0 ? commands : Tools.Count, Tools.Count - commands);
        }
    }

    public void SetTools(IReadOnlyList<AgentToolItem> tools)
    {
        Tools.Clear();
        foreach (var tool in tools)
        {
            tool.Group = this;
            Tools.Add(tool);
        }
        RefreshSummary();
    }

    public void RefreshSummary()
    {
        Notify(nameof(Label));
        Notify(nameof(HasFailure));
    }
}

internal sealed class AgentTurnStatsItem(Guid? turnId, DateTimeOffset startedAt) : AgentTimelineItemViewModel(turnId)
{
    private AgentTurnCompleted? _completed;
    private long? _elapsedMs;

    public bool IsRunning => _elapsedMs is null;
    public string WorkTime => Duration is { } duration
        ? string.Format(CultureInfo.CurrentCulture, AgentStrings.TurnWorkedFor, duration)
        : string.Format(CultureInfo.CurrentCulture, AgentStrings.TurnWorkingFor,
            AgentTurnTime.Format((long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds));

    public void RefreshElapsed() => Notify(nameof(WorkTime));

    public void Finish(AgentTimelineEvent entry)
    {
        _completed = entry as AgentTurnCompleted;
        _elapsedMs = _completed?.DurationMs ?? (long)(entry.Timestamp - startedAt).TotalMilliseconds;
        Notify(null);
    }

    public string Detail => string.Join(Environment.NewLine, new[]
    {
        Duration,
        FormatCount(_completed?.InputTokens, AgentStrings.TurnInputTokens),
        FormatCount(_completed?.OutputTokens, AgentStrings.TurnOutputTokens),
        FormatCount(_completed?.CachedInputTokens, AgentStrings.TurnCachedInputTokens)
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private string? Duration => _elapsedMs is { } milliseconds
        ? AgentTurnTime.Format(milliseconds, fractionalSeconds: true) : null;

    private static string? FormatCount(long? count, string format) => count is { } value
        ? string.Format(CultureInfo.CurrentCulture, format, value.ToString("N0", CultureInfo.CurrentCulture)) : null;
}

internal static class AgentTurnTime
{
    public static string Format(long milliseconds, bool fractionalSeconds = false)
    {
        long elapsed = Math.Max(0, milliseconds);
        long seconds = elapsed / 1000;
        var culture = CultureInfo.CurrentCulture;
        if (seconds >= 3600)
            return string.Format(culture, AgentStrings.TurnDurationHours, seconds / 3600, seconds / 60 % 60, seconds % 60);
        if (seconds >= 60)
            return string.Format(culture, AgentStrings.TurnDurationMinutes, seconds / 60, seconds % 60);
        return string.Format(culture, AgentStrings.TurnDuration, fractionalSeconds ? elapsed / 1000d : seconds);
    }
}

internal sealed class AgentModelChangeItem(AgentThreadModelChanged entry) : AgentTimelineItemViewModel(entry.TurnId)
{
    public AgentThreadModelChanged Entry { get; } = entry;
}

internal sealed class AgentProviderChangeItem(AgentThreadProviderChanged entry) : AgentTimelineItemViewModel(entry.TurnId)
{
    public AgentThreadProviderChanged Entry { get; } = entry;
}

internal sealed class AgentTimelineDividerItem(Guid? turnId, string text, Icon icon) : AgentTimelineItemViewModel(turnId)
{
    public string Text { get; } = text;
    public Icon Icon { get; } = icon;
}

internal sealed class AgentTurnWorkItem(AgentTurnStatsItem header) : AgentTimelineItemViewModel(header.TurnId)
{
    public AgentTurnStatsItem Header { get; } = header;
    public ObservableCollection<AgentTimelineItemViewModel> Entries { get; } = [];
    public bool IsExpanded { get; set => Set(ref field, value); } = true;
}

internal sealed class AgentPlanItem(AgentPlanUpdated entry) : AgentTimelineItemViewModel(entry.TurnId)
{
    public IReadOnlyList<AgentPlanRow> Steps => Entry.Steps.Select(step => new AgentPlanRow(step.Text, step.Status)).ToArray();
    public string? Explanation => Entry.Explanation;
    public string Progress => $"{Entry.Steps.Count(step => step.Status == AgentPlanStepStatus.Completed)}/{Entry.Steps.Count}";

    public AgentPlanUpdated Entry
    {
        get;
        set
        {
            field = value;
            Notify(nameof(Steps));
            Notify(nameof(Explanation));
            Notify(nameof(Progress));
        }
    } = entry;
}

internal sealed record AgentPlanRow(string Text, AgentPlanStepStatus Status)
{
    public bool IsCompleted => Status == AgentPlanStepStatus.Completed;
    public bool IsInProgress => Status == AgentPlanStepStatus.InProgress;
    public Icon StatusIcon => Status switch
    {
        AgentPlanStepStatus.Completed => Icon.CheckmarkCircle,
        AgentPlanStepStatus.InProgress => Icon.CircleHalfFill,
        _ => Icon.Circle
    };
    public string StatusText => Status switch
    {
        AgentPlanStepStatus.Completed => AgentStrings.Completed,
        AgentPlanStepStatus.InProgress => AgentStrings.Running,
        _ => AgentStrings.Pending
    };
}

internal sealed class AgentNoticeItem(Guid? turnId, string message, FAInfoBarSeverity severity) : AgentTimelineItemViewModel(turnId)
{
    public string Message { get; } = message;
    public FAInfoBarSeverity Severity { get; } = severity;
}

internal sealed class AgentHistoryRecordItem(AgentTurnHistoryRecorded entry) : AgentTimelineItemViewModel(entry.TurnId)
{
    public AgentTurnHistoryRecorded Entry { get; } = entry;
}

internal sealed class AgentHistoryItem(AgentTurnHistoryRecorded entry, Func<AgentHistoryItem, Task> revert)
    : AgentTimelineItemViewModel(entry.TurnId)
{
    public int TransactionCount => entry.TransactionCount;
    public IReadOnlyList<string> Changes => entry.Changes ?? [];
    public bool HasChanges => Changes.Count > 0;
    public bool HasMoreChanges => HasChanges && TransactionCount > Changes.Count;
    public string MoreChanges => string.Format(CultureInfo.CurrentCulture, AgentStrings.MoreHistoryChanges,
        TransactionCount - Changes.Count);
    public string Summary => string.Format(CultureInfo.CurrentCulture, AgentStrings.TurnEditCount, TransactionCount);
    public string DisplayText => Result ?? Summary;
    public bool CanRevert
    {
        get;
        set => Set(ref field, value);
    }

    public string? Result
    {
        get;
        set
        {
            if (Set(ref field, value))
                Notify(nameof(DisplayText));
        }
    }

    public Task RevertAsync() => revert(this);
}
