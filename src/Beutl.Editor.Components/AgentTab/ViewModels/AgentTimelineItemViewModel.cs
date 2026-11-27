using System.Collections.ObjectModel;
using Beutl.Agents;
using FluentAvalonia.UI.Controls;
using FluentIcons.Common;
using LiveMarkdown.Avalonia;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

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
            attachment.RemoveUnavailablePreview();
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
        if (Preview is not { } preview)
            return;
        Preview = null;
        Notify(nameof(HasPreview));
        preview.Dispose();
    }
}

internal sealed class AgentAssistantItem(Guid? turnId, DateTimeOffset timestamp, string? projectDirectory)
    : AgentMessageItem(turnId, timestamp), IDisposable
{
    public ObservableStringBuilder Markdown { get; } = new();
    public override string Text => Markdown.ToString();
    public bool ReceivedCompletion { get; private set; }
    public bool IsFinalTurnResponse { get; private set => Set(ref field, value); }
    public bool CanCopy => IsFinalTurnResponse && !string.IsNullOrWhiteSpace(Text);
    public IReadOnlyList<AgentImagePreview> Images { get; private set => Set(ref field, value); } = [];

    public void Append(string text)
    {
        Markdown.Append(text);
        Notify(nameof(Text));
    }

    public void Complete(string text)
    {
        bool refreshImages = !ReceivedCompletion || Text != text;
        ReceivedCompletion = true;
        if (Text != text)
        {
            Markdown.Clear();
            Append(text);
        }
        if (refreshImages)
            UpdateImages(text);
    }

    public void MarkFinal()
    {
        if (!ReceivedCompletion)
            UpdateImages(Text);
        IsFinalTurnResponse = true;
        Notify(nameof(CanCopy));
    }

    private void UpdateImages(string text)
    {
        Dispose();
        Images = AgentMarkdownImages.GetPreviews(text, projectDirectory);
    }

    public void Dispose()
    {
        var images = Images;
        Images = [];
        foreach (var image in images)
            image.Dispose();
    }
}

internal sealed class AgentReasoningItem(Guid? turnId, DateTimeOffset startedAt) : AgentTimelineItemViewModel(turnId)
{
    public string Summary { get; private set => Set(ref field, value); } = AgentStrings.Thinking;
    public bool IsFinished { get; private set; }
    public bool IsExpanded
    {
        get;
        set
        {
            if (Set(ref field, value))
                Notify(nameof(ExpandedContent));
        }
    }

    public AgentReasoningItem? ExpandedContent => IsExpanded ? this : null;

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

internal sealed class AgentTurnStatsItem(Guid? turnId, DateTimeOffset startedAt, int segment = 0) : AgentTimelineItemViewModel(turnId)
{
    private AgentTurnCompleted? _completed;
    private long? _elapsedMs;

    public int Segment { get; } = segment;
    public bool IsRunning => _elapsedMs is null;
    public string WorkTime => Duration is { } duration
        ? string.Format(CultureInfo.CurrentCulture, AgentStrings.TurnWorkedFor, duration)
        : string.Format(CultureInfo.CurrentCulture, AgentStrings.TurnWorkingFor,
            AgentTurnTime.Format((long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds));

    public void RefreshElapsed() => Notify(nameof(WorkTime));

    public void Finish(AgentTimelineEvent entry)
    {
        _completed = entry as AgentTurnCompleted;
        _elapsedMs = (Segment == 0 ? _completed?.DurationMs : null) ?? (long)(entry.Timestamp - startedAt).TotalMilliseconds;
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

internal sealed class AgentTurnWorkItem(AgentTurnStatsItem header, bool expanded, Action<bool> setExpanded)
    : AgentTimelineItemViewModel(header.TurnId)
{
    private bool _wasRunning = header.IsRunning;

    public AgentTurnStatsItem Header { get; } = header;
    public ObservableCollection<AgentTimelineItemViewModel> Entries { get; } = [];
    public ObservableCollection<AgentTimelineItemViewModel>? VisibleEntries => IsExpanded ? Entries : null;
    public bool IsExpanded
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Notify(nameof(VisibleEntries));
                setExpanded(value);
            }
        }
    } = expanded;

    internal void SyncRunningState(bool manuallyExpanded)
    {
        if (Header.IsRunning)
        {
            _wasRunning = true;
            return;
        }
        if (!_wasRunning)
            return;
        _wasRunning = false;
        if (IsExpanded != manuallyExpanded)
            IsExpanded = manuallyExpanded;
    }
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
            Notify(nameof(Steps), nameof(Explanation), nameof(Progress));
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
    public string DisplayMessage => Severity == FAInfoBarSeverity.Error ? AgentStrings.TurnFailed : Message;
    public Icon StatusIcon => Severity is FAInfoBarSeverity.Error or FAInfoBarSeverity.Warning ? Icon.Warning : Icon.Info;
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
    public bool IsExpanded
    {
        get;
        set
        {
            if (Set(ref field, value))
                Notify(nameof(ExpandedContent));
        }
    }

    public AgentHistoryItem? ExpandedContent => IsExpanded ? this : null;
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
