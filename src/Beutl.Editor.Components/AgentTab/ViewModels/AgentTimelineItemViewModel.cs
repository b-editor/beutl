using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Beutl.Agents;
using Beutl.Editor.Services.AI;
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

internal sealed class AgentUserItem(AgentUserMessage entry) : AgentTimelineItemViewModel(entry.TurnId)
{
    public string Text => entry.Text;
}

internal sealed class AgentAssistantItem(Guid? turnId) : AgentTimelineItemViewModel(turnId)
{
    public ObservableStringBuilder Markdown { get; } = new();
    public string Text => Markdown.ToString();
    public bool CanCopy { get; private set => Set(ref field, value); }

    public void Append(string text)
    {
        Markdown.Append(text);
        Notify(nameof(Text));
    }

    public void Complete(string text)
    {
        if (Text != text)
        {
            Markdown.Clear();
            Append(text);
        }
        CanCopy = !string.IsNullOrWhiteSpace(text);
    }

    public void Finish() => CanCopy = !string.IsNullOrWhiteSpace(Text);
}

internal sealed class AgentReasoningItem(Guid? turnId) : AgentTimelineItemViewModel(turnId)
{
    public string Text
    {
        get;
        set => Set(ref field, value);
    } = "";
}

internal sealed class AgentToolItem : AgentTimelineItemViewModel, IDisposable
{
    private const long MaxImageBytes = 16 * 1024 * 1024;
    private const string BeutlPrefix = "mcp__beutl__";

    private AgentToolStatus _status = AgentToolStatus.Running;
    private AgentSubagent? _subagent;
    private string? _backgroundTitle;
    private bool _isBackground;
    private string? _scopedActivity;

    public AgentToolItem(AgentToolCallStarted entry) : base(entry.TurnId)
    {
        Describe(entry);
        Children.CollectionChanged += (_, _) => NotifyHierarchy();
    }

    public ObservableCollection<AgentTimelineItemViewModel> Children { get; } = [];
    internal AgentToolItem? Parent { get; set; }
    public int ChildToolCount => Children.Count(item => item is AgentToolItem);
    public string ChildToolCountLabel => string.Format(CultureInfo.CurrentCulture, AgentStrings.SubagentToolCount, ChildToolCount);
    public bool HasChildTools => ChildToolCount > 0;
    public bool IsBackground => _isBackground;
    public bool IsSubagent => _subagent is not null || ToolName is "Agent" or "Task" || (!IsBackground && Children.Count > 0);
    public bool IsBackgroundWork => IsSubagent || IsBackground;
    public string Title => _subagent?.Description ?? _backgroundTitle ?? _subagent?.Role ?? (IsSubagent ? AgentStrings.Subagent : ToolName);
    public string WorkKindLabel => IsSubagent ? _subagent?.Role ?? AgentStrings.Subagent : AgentStrings.BackgroundCommand;
    public string WorkPreview => _scopedActivity ?? ActivitySummary;
    public bool CanExpand => HasDetails || IsBackgroundWork || Children.Count > 0;

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

    public IReadOnlyList<Bitmap> Images
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

    public Icon StatusIcon => _status switch
    {
        AgentToolStatus.Completed => Icon.CheckmarkCircle,
        AgentToolStatus.Failed => Icon.ErrorCircle,
        AgentToolStatus.Cancelled => Icon.DismissCircle,
        _ => Icon.Clock
    };

    public string ActivitySummary => Preview(Output) ?? Status;

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

        var images = new List<Bitmap>();
        var text = new List<string>();
        foreach (var output in outputs)
        {
            if (output.Type == "image")
            {
                if (DecodeImage(output) is { } image)
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
        Notify(nameof(ActivitySummary));
        Notify(nameof(WorkPreview));
        Notify(nameof(HasDetails));
        Notify(nameof(CanExpand));
        Notify(nameof(IsRunning));
        Notify(nameof(IsInProgress));
        Notify(nameof(IsSuccessful));
        Notify(nameof(IsFailed));
        Notify(nameof(StatusIcon));
    }

    private static Bitmap? DecodeImage(AgentToolOutput output)
    {
        try
        {
            using Stream? stream = OpenImage(output);
            if (stream is null)
                return null;
            AiImageDecodeValidator.ValidateEncoded(stream, MaxImageBytes);
            using var source = new Bitmap(stream);
            var size = source.PixelSize;
            double scale = Math.Min(1, Math.Min(320d / size.Width, 240d / size.Height));
            return source.CreateScaledBitmap(new Avalonia.PixelSize(
                Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale))));
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException
            or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static Stream? OpenImage(AgentToolOutput output)
    {
        if (output.Data is { Length: > 0 } data)
        {
            if (data.Length > (MaxImageBytes + 2) / 3 * 4)
                return null;
            return new MemoryStream(Convert.FromBase64String(data), writable: false);
        }
        return Uri.TryCreate(output.Uri, UriKind.Absolute, out var uri) && uri.IsFile
            && !uri.IsUnc && string.IsNullOrEmpty(uri.Host)
            ? File.OpenRead(uri.LocalPath) : null;
    }

    public void Dispose()
    {
        foreach (var image in Images)
            image.Dispose();
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
