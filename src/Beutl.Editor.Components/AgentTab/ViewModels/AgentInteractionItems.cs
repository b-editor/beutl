using Beutl.Agents;
using FluentIcons.Common;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal abstract class AgentInteractionItem(Guid turnId) : AgentTimelineItemViewModel(turnId)
{
    public bool IsPending
    {
        get;
        private set => Set(ref field, value);
    } = true;

    public string? ResolutionText { get; private set => Set(ref field, value); }
    public Icon ResolutionIcon { get; private set => Set(ref field, value); } = Icon.DismissCircle;

    protected void Resolve(string status, Icon icon, string? detail = null)
    {
        ResolutionIcon = icon;
        ResolutionText = string.IsNullOrWhiteSpace(detail) ? status : $"{status} · {detail}";
        IsPending = false;
    }

    internal virtual void Close()
    {
        if (IsPending)
            Resolve(AgentStrings.RequestClosed, Icon.DismissCircle);
    }
}

internal sealed class AgentPermissionItem : AgentInteractionItem
{
    private readonly TaskCompletionSource<AgentPermissionDecision> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AgentPermissionItem(AgentPermissionRequest request) : base(request.TurnId)
    {
        ToolName = request.ToolName;
        Description = request.Description;
        var supported = request.Options?.Select(option => option.Decision).ToHashSet();
        var options = new List<AgentPermissionChoice>();
        if (supported is null || supported.Contains(AgentPermissionDecision.AllowOnce))
            options.Add(new AgentPermissionChoice(options.Count + 1, AgentPermissionDecision.AllowOnce, AgentStrings.ApprovalAllowOnce));
        if (supported is null || supported.Contains(AgentPermissionDecision.AllowAlways))
            options.Add(new AgentPermissionChoice(options.Count + 1, AgentPermissionDecision.AllowAlways,
                AgentStrings.ApprovalAllowAlways, request.McpServerName));
        bool rejectAlwaysOnly = supported?.Contains(AgentPermissionDecision.RejectAlways) == true
            && !supported.Contains(AgentPermissionDecision.RejectOnce);
        options.Add(rejectAlwaysOnly
            ? new AgentPermissionChoice(options.Count + 1, AgentPermissionDecision.RejectAlways, AgentStrings.RejectAlways)
            : new AgentPermissionChoice(options.Count + 1, AgentPermissionDecision.RejectOnce, AgentStrings.ApprovalReject));
        Options = options;
    }

    public string ToolName { get; }
    public string? Description { get; }
    public string ApprovalQuestion => string.Format(AgentStrings.AllowToolQuestion, ToolName);
    public IReadOnlyList<AgentPermissionChoice> Options { get; }
    public int SelectedIndex { get; set => Set(ref field, value); }
    internal Task<AgentPermissionDecision> Answer => _answer.Task;

    public void SubmitSelected()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Options.Count)
            Respond(Options[SelectedIndex].Decision);
    }

    internal void Respond(AgentPermissionDecision decision)
    {
        if (IsPending && _answer.TrySetResult(decision))
            Resolve(DecisionLabel(decision), decision is AgentPermissionDecision.RejectOnce or AgentPermissionDecision.RejectAlways
                ? Icon.DismissCircle : Icon.Checkmark, ToolName);
    }

    internal override void Close()
    {
        base.Close();
        _answer.TrySetCanceled();
    }

    private static string DecisionLabel(AgentPermissionDecision decision) => decision switch
    {
        AgentPermissionDecision.AllowOnce => AgentStrings.AllowOnce,
        AgentPermissionDecision.AllowAlways => AgentStrings.AllowAlways,
        AgentPermissionDecision.RejectOnce => AgentStrings.RejectOnce,
        _ => AgentStrings.RejectAlways
    };
}

internal sealed record AgentPermissionChoice(int Number, AgentPermissionDecision Decision, string Label, string? Detail = null);

internal sealed class AgentQuestionItem : AgentInteractionItem
{
    private TaskCompletionSource<IReadOnlyDictionary<string, string[]>>? _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _step;

    public AgentQuestionItem(AgentUserQuestionRequest request) : base(request.TurnId)
    {
        Questions = request.Questions.Select(question => new AgentQuestionAnswer(question)).ToArray();
        foreach (var question in Questions)
            question.PropertyChanged += (_, _) => Notify(nameof(CanAdvance));
    }

    public AgentQuestionItem(Guid turnId, bool answered) : base(turnId)
    {
        WasAnswered = answered;
        Questions = [];
        if (answered)
            Resolve(AgentStrings.Answered, Icon.Checkmark);
        Close();
    }

    public IReadOnlyList<AgentQuestionAnswer> Questions { get; }
    public AgentQuestionAnswer? CurrentQuestion => _step < Questions.Count ? Questions[_step] : null;
    public bool HasSteps => Questions.Count > 1;
    public string StepLabel => $"{_step + 1} / {Questions.Count}";
    public bool CanGoBack => _step > 0;
    public bool CanSkip => IsPending && CurrentQuestion is not null;
    public bool CanAdvance => IsPending && CurrentQuestion?.HasAnswer == true;
    public string AdvanceLabel => _step + 1 < Questions.Count ? AgentStrings.QuestionNext : AgentStrings.Submit;
    internal bool WasAnswered { get; private set; }
    internal Task<IReadOnlyDictionary<string, string[]>> Answer => _answer!.Task;

    public void Back()
    {
        if (IsPending && CanGoBack)
            GoTo(_step - 1);
    }

    public void Skip()
    {
        if (!CanSkip || CurrentQuestion is not { } current)
            return;
        current.IsSkipped = true;
        if (_step + 1 < Questions.Count)
            GoTo(_step + 1);
        else
            Submit();
    }

    public void Advance()
    {
        if (!CanAdvance || CurrentQuestion is not { } current)
            return;
        current.IsSkipped = false;
        if (_step + 1 < Questions.Count)
            GoTo(_step + 1);
        else
            Submit();
    }

    private void GoTo(int step)
    {
        _step = step;
        Notify(nameof(CurrentQuestion));
        Notify(nameof(StepLabel));
        Notify(nameof(CanGoBack));
        Notify(nameof(CanSkip));
        Notify(nameof(CanAdvance));
        Notify(nameof(AdvanceLabel));
    }

    private void Submit()
    {
        if (!Questions.All(question => question.HasAnswer || question.IsSkipped))
            return;
        var answers = new Dictionary<string, string[]>();
        foreach (var question in Questions)
        {
            if (!question.IsSkipped && question.HasAnswer)
                answers[question.Id] = question.GetAnswers();
        }
        if (_answer?.TrySetResult(answers) == true)
        {
            if (answers.Count == 0)
            {
                Resolve(AgentStrings.QuestionSkipped, Icon.DismissCircle);
            }
            else
            {
                WasAnswered = true;
                string summary = string.Join(", ", Questions.Where(question => !question.IsSecret && answers.ContainsKey(question.Id))
                    .SelectMany(question => answers[question.Id]).Select(answer => answer.Split(['\r', '\n'], 2)[0].Trim()));
                Resolve(AgentStrings.Answered, Icon.Checkmark, summary);
            }
            Notify(nameof(CanSkip));
            Notify(nameof(CanAdvance));
            ClearSecrets();
        }
    }

    internal override void Close()
    {
        base.Close();
        _answer?.TrySetCanceled();
        _answer = null;
        ClearSecrets();
    }

    private void ClearSecrets()
    {
        foreach (var question in Questions.Where(question => question.IsSecret))
            question.Text = null;
    }
}

internal sealed class AgentQuestionAnswer : AgentBindable
{
    private readonly AgentQuestion _question;

    public AgentQuestionAnswer(AgentQuestion question)
    {
        _question = question;
        Options = (question.Options ?? []).Select((option, index) => new AgentQuestionChoice(index + 1, option.Label, option.Description)).ToArray();
        SingleOptions = AllowOther ? [.. Options, new AgentQuestionChoice(Options.Count + 1, AgentStrings.QuestionOther, null)] : Options;
        foreach (var choice in Options)
            choice.PropertyChanged += (_, _) =>
            {
                IsSkipped = false;
                Notify(nameof(HasAnswer));
            };
    }

    public string Id => _question.Id;
    public string Question => _question.Text;
    public string? Header => string.IsNullOrWhiteSpace(_question.Header) ? null : _question.Header;
    public bool IsSecret => _question.IsSecret;
    public bool IsSkipped { get; set => Set(ref field, value); }
    public IReadOnlyList<AgentQuestionChoice> Options { get; }
    public IReadOnlyList<AgentQuestionChoice> SingleOptions { get; }
    public bool HasTextOnly => Options.Count == 0;
    public bool HasSingleOptions => !HasTextOnly && !_question.MultiSelect;
    public bool HasMultiOptions => !HasTextOnly && _question.MultiSelect;
    public bool AllowOther => !HasTextOnly && _question.IsOther;
    public bool ShowMultiOther => HasMultiOptions && AllowOther;
    public bool ShowAnswerBox => HasTextOnly || (AllowOther && (HasMultiOptions ? OtherChecked : SelectedIndex == Options.Count));
    public char PasswordChar => IsSecret ? '•' : '\0';
    public bool HasAnswer => HasMultiOptions
        ? (!ShowAnswerBox || !string.IsNullOrWhiteSpace(Text)) && (ShowAnswerBox || Options.Any(choice => choice.IsChecked))
        : ShowAnswerBox ? !string.IsNullOrWhiteSpace(Text) : SelectedIndex >= 0 && SelectedIndex < Options.Count;

    public int SelectedIndex
    {
        get;
        set
        {
            if (Set(ref field, value))
                AnswerChanged();
        }
    }

    public bool OtherChecked
    {
        get;
        set
        {
            if (Set(ref field, value))
                AnswerChanged();
        }
    }

    public string? Text
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                IsSkipped = false;
                Notify(nameof(HasAnswer));
            }
        }
    }

    public string[] GetAnswers()
    {
        if (!HasMultiOptions)
            return [ShowAnswerBox ? Text! : Options[SelectedIndex].Label];

        var answers = Options.Where(choice => choice.IsChecked).Select(choice => choice.Label).ToList();
        if (ShowAnswerBox)
            answers.Add(Text!);
        return answers.ToArray();
    }

    private void AnswerChanged()
    {
        IsSkipped = false;
        Notify(nameof(ShowAnswerBox));
        Notify(nameof(HasAnswer));
    }
}

internal sealed class AgentQuestionChoice(int number, string label, string? description) : AgentBindable
{
    public int Number { get; } = number;
    public string Label { get; } = label;
    public string? Description { get; } = description;
    public bool IsChecked { get; set => Set(ref field, value); }
}
