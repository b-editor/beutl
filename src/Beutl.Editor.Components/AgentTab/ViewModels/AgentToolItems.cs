using System.Collections.ObjectModel;
using System.Text.Json;
using Beutl.Agents;
using FluentIcons.Common;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

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
    private bool HasDetails => !string.IsNullOrWhiteSpace(Input) || !string.IsNullOrWhiteSpace(Output);

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
        NotifyHierarchy();
    }

    private void NotifyHierarchy()
    {
        NotifyStop();
        Notify(
            nameof(IsSubagent),
            nameof(IsBackgroundWork),
            nameof(CanExpand),
            nameof(Title),
            nameof(WorkKindLabel),
            nameof(HasChildTools),
            nameof(ChildToolCount),
            nameof(ChildToolCountLabel),
            nameof(ToolIcon));
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
        else if (_hasImageOutput)
        {
            Notify(nameof(ToolIcon));
        }
    }

    private void NotifyActivity()
    {
        NotifyStop();
        Notify(
            nameof(ActivitySummary),
            nameof(WorkPreview),
            nameof(CanExpand),
            nameof(IsRunning),
            nameof(IsInProgress),
            nameof(IsSuccessful),
            nameof(IsFailed),
            nameof(ToolIcon));
    }

    private void NotifyStop() => Notify(nameof(ShowStop), nameof(CanStop));

    public void Dispose()
    {
        var images = Images;
        Images = [];
        foreach (var image in images)
            image.Dispose();
    }
}

internal sealed class AgentToolGroupItem(Guid? turnId) : AgentTimelineItemViewModel(turnId)
{
    public ObservableCollection<AgentTimelineItemViewModel> Items { get; } = [];
    public ObservableCollection<AgentToolItem> Tools { get; } = [];
    public bool IsExpanded
    {
        get;
        set
        {
            if (Set(ref field, value))
                Notify(nameof(VisibleItems), nameof(VisibleTools));
        }
    }

    public ObservableCollection<AgentTimelineItemViewModel>? VisibleItems => IsExpanded ? Items : null;
    public ObservableCollection<AgentToolItem>? VisibleTools => IsExpanded ? Tools : null;
    public bool HasFailure => Tools.Any(tool => tool.IsFailed);
    public int ThoughtCount => Items.Count(item => item is AgentReasoningItem);
    public string Label
    {
        get
        {
            int commands = Tools.Count(tool => tool.IsCommand);
            string format = commands == Tools.Count ? AgentStrings.ToolGroupCommands
                : commands > 0 ? AgentStrings.ToolGroupMixed
                : Tools.All(tool => tool.IsInspection) ? AgentStrings.ToolGroupChecks
                : AgentStrings.ToolGroupTools;
            string summary = string.Format(CultureInfo.CurrentCulture, format,
                commands > 0 ? commands : Tools.Count, Tools.Count - commands);
            int thoughts = ThoughtCount;
            if (thoughts == 0)
                return summary;
            return string.Format(CultureInfo.CurrentCulture,
                thoughts == 1 ? AgentStrings.ToolGroupWithThought : AgentStrings.ToolGroupWithThoughts,
                summary, thoughts);
        }
    }

    public void SetItems(IReadOnlyList<AgentTimelineItemViewModel> items)
    {
        ClearItems();
        foreach (var item in items)
        {
            SetGroup(item, this);
            Items.Add(item);
            if (item is AgentToolItem tool)
                Tools.Add(tool);
        }
        RefreshSummary();
    }

    public void ClearItems()
    {
        foreach (var item in Items)
            SetGroup(item, null);
        Items.Clear();
        Tools.Clear();
    }

    public void RefreshSummary() => Notify(nameof(Label), nameof(HasFailure), nameof(ThoughtCount));

    private static void SetGroup(AgentTimelineItemViewModel item, AgentToolGroupItem? group)
    {
        if (item is AgentToolItem tool)
            tool.Group = group;
        else if (item is AgentReasoningItem reasoning)
            reasoning.Group = group;
    }
}
