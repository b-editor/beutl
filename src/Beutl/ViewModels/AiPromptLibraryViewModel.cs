using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Services.AI;
using Reactive.Bindings;

namespace Beutl.ViewModels;

/// <summary>
/// The saved prompts behind one task's prompt box. Templates and history are
/// two lists rather than one: a template is something the account named and
/// keeps, history is only what it happened to run, and the tab reaches them
/// from different places.
/// </summary>
internal sealed class AiPromptLibraryViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly IPromptLibrary _library;
    private readonly AiRequestRecoveryContext? _recoveryContext;
    private readonly PromptTaskKind _taskKind;
    private readonly Func<string> _getPrompt;
    private readonly Action<string> _applyPrompt;
    private readonly ObservableCollection<AiPromptChoice> _templates = [];
    private readonly ObservableCollection<AiPromptChoice> _history = [];

    public AiPromptLibraryViewModel(
        PromptTaskKind taskKind,
        Func<string> getPrompt,
        Action<string> applyPrompt,
        IPromptLibrary? library = null,
        AiRequestRecoveryContext? recoveryContext = null)
    {
        _taskKind = taskKind;
        _getPrompt = getPrompt ?? throw new ArgumentNullException(nameof(getPrompt));
        _applyPrompt = applyPrompt ?? throw new ArgumentNullException(nameof(applyPrompt));
        _recoveryContext = recoveryContext;
        _library = library ?? PromptLibraryProvider.For(
            recoveryContext ?? throw new ArgumentNullException(nameof(recoveryContext)));
        Templates = new ReadOnlyObservableCollection<AiPromptChoice>(_templates);
        History = new ReadOnlyObservableCollection<AiPromptChoice>(_history);

        Apply = new ReactiveCommand<AiPromptChoice>(ImmediateScheduler.Instance);
        Apply.Subscribe(ApplyCore).DisposeWith(_disposables);
        TogglePin = new ReactiveCommand<AiPromptChoice>(ImmediateScheduler.Instance);
        TogglePin.Subscribe(TogglePinCore).DisposeWith(_disposables);
        Delete = new ReactiveCommand<AiPromptChoice>(ImmediateScheduler.Instance);
        Delete.Subscribe(DeleteCore).DisposeWith(_disposables);
        SaveTemplate = new ReactiveCommand();
        SaveTemplate.Subscribe(SaveTemplateCore).DisposeWith(_disposables);
        ClearHistory = new ReactiveCommand();
        ClearHistory.Subscribe(() =>
        {
            _library.ClearHistory();
            Refresh();
        }).DisposeWith(_disposables);

        Refresh();
        if (_recoveryContext is not null)
            _recoveryContext.IdentityChanged += RefreshForIdentity;
    }

    /// <summary>Named prompts the account keeps, newest and pinned first.</summary>
    public ReadOnlyObservableCollection<AiPromptChoice> Templates { get; }

    /// <summary>Prompts this account has run, newest and pinned first.</summary>
    public ReadOnlyObservableCollection<AiPromptChoice> History { get; }

    /// <summary>
    /// Drives the history popup. Applying a prompt closes it, so the tab does
    /// not keep a panel open over the box the prompt just landed in.
    /// </summary>
    public ReactivePropertySlim<bool> IsHistoryOpen { get; } = new();

    public ReactivePropertySlim<string> TemplateName { get; } = new();

    public ReactivePropertySlim<bool> HasTemplates { get; } = new();

    public ReactivePropertySlim<bool> HasHistory { get; } = new();

    public ReactivePropertySlim<string?> Error { get; } = new();

    public ReactiveCommand<AiPromptChoice> Apply { get; }

    public ReactiveCommand<AiPromptChoice> TogglePin { get; }

    public ReactiveCommand<AiPromptChoice> Delete { get; }

    public ReactiveCommand SaveTemplate { get; }

    public ReactiveCommand ClearHistory { get; }

    public string PrivacyText => Strings.AiPromptLibraryPrivacy;

    public void Record(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return;

        try { _library.Record(_taskKind, prompt); }
        catch (AuthenticationRequiredException) { return; }
        Refresh();
    }

    public void Dispose()
    {
        if (_recoveryContext is not null)
            _recoveryContext.IdentityChanged -= RefreshForIdentity;
        IsHistoryOpen.Dispose();
        TemplateName.Dispose();
        HasTemplates.Dispose();
        HasHistory.Dispose();
        Error.Dispose();
        _disposables.Dispose();
    }

    private void ApplyCore(AiPromptChoice? choice)
    {
        if (choice is null)
            return;

        _applyPrompt(choice.Prompt);
        Error.Value = null;
        IsHistoryOpen.Value = false;
    }

    private void TogglePinCore(AiPromptChoice? choice)
    {
        if (choice is null)
            return;

        bool changed = choice.IsTemplate
            ? _library.SetTemplatePinned(choice.Id, !choice.IsPinned)
            : _library.SetHistoryPinned(choice.Id, !choice.IsPinned);
        if (changed)
        {
            Refresh();
        }
    }

    private void DeleteCore(AiPromptChoice? choice)
    {
        if (choice is null)
            return;

        bool deleted = choice.IsTemplate
            ? _library.DeleteTemplate(choice.Id)
            : _library.DeleteHistory(choice.Id);
        if (deleted)
        {
            Refresh();
        }
    }

    private void SaveTemplateCore()
    {
        Error.Value = null;
        try
        {
            _library.SaveTemplate(TemplateName.Value, _taskKind, _getPrompt());
            TemplateName.Value = string.Empty;
            Refresh();
        }
        catch (ArgumentException)
        {
            Error.Value = Strings.AiPromptTemplateInvalid;
        }
        catch (AuthenticationRequiredException)
        {
            Error.Value = Strings.AiAuthenticationRequired;
        }
    }

    private void Refresh()
    {
        _templates.Clear();
        foreach (PromptTemplate template in _library.Templates
                     .Where(item => item.TaskKind == _taskKind)
                     .OrderByDescending(item => item.IsPinned)
                     .ThenByDescending(item => item.UpdatedAtUtc))
        {
            _templates.Add(new AiPromptChoice(
                template.Id,
                template.Name,
                template.Prompt,
                IsTemplate: true,
                template.IsPinned));
        }

        _history.Clear();
        foreach (PromptHistoryEntry history in _library.History
                     .Where(item => item.TaskKind == _taskKind)
                     .OrderByDescending(item => item.IsPinned)
                     .ThenByDescending(item => item.LastUsedAtUtc))
        {
            _history.Add(new AiPromptChoice(
                history.Id,
                Summarize(history.Prompt),
                history.Prompt,
                IsTemplate: false,
                history.IsPinned));
        }

        HasTemplates.Value = _templates.Count > 0;
        HasHistory.Value = _history.Count > 0;
    }

    private void RefreshForIdentity()
    {
        // Clear the previous account before touching the new account's store.
        // A corrupt or unavailable destination must never leave the old
        // account's prompt text visible in the new session.
        _templates.Clear();
        _history.Clear();
        HasTemplates.Value = false;
        HasHistory.Value = false;
        IsHistoryOpen.Value = false;
        TemplateName.Value = string.Empty;
        Error.Value = null;
        try
        {
            Refresh();
        }
        catch (InvalidDataException ex)
        {
            // IdentityChanged is multicast and the dialog must still clear its
            // previous account's form even when this account's library is corrupt.
            System.Diagnostics.Trace.TraceWarning(
                "Failed to read prompt library during account switch: {0}",
                ex.Message);
        }
        catch (Exception ex) when (ex is AuthenticationRequiredException
            or IOException
            or UnauthorizedAccessException)
        {
            Error.Value = ex is AuthenticationRequiredException
                ? Strings.AiAuthenticationRequired
                : Strings.AiResultUnavailable;
        }
    }

    // A history entry has no name, so its first line stands in for one.
    private static string Summarize(string prompt)
    {
        string summary = prompt.Split('\n', 2)[0];
        return summary.Length > 72 ? summary[..69] + "…" : summary;
    }
}

internal sealed record AiPromptChoice(
    Guid Id,
    string Name,
    string Prompt,
    bool IsTemplate,
    bool IsPinned)
{
    public override string ToString() => Name;
}
