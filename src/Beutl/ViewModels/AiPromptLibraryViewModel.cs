using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using Avalonia.Threading;
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
    private readonly object _refreshGate = new();
    private readonly Action<Action> _dispatchToUi;
    private readonly IPromptLibrary _library;
    private readonly IDisposable? _libraryChangeSubscription;
    private readonly AiRequestRecoveryContext? _recoveryContext;
    private readonly PromptTaskKind _taskKind;
    private readonly Func<string> _getPrompt;
    private readonly Action<string> _applyPrompt;
    private readonly ObservableCollection<AiPromptChoice> _templates = [];
    private readonly ObservableCollection<AiPromptChoice> _history = [];
    private bool _disposed;
    private long _accountGeneration;

    public AiPromptLibraryViewModel(
        PromptTaskKind taskKind,
        Func<string> getPrompt,
        Action<string> applyPrompt,
        IPromptLibrary? library = null,
        AiRequestRecoveryContext? recoveryContext = null,
        Action<Action>? dispatchToUi = null)
    {
        _taskKind = taskKind;
        _getPrompt = getPrompt ?? throw new ArgumentNullException(nameof(getPrompt));
        _applyPrompt = applyPrompt ?? throw new ArgumentNullException(nameof(applyPrompt));
        _recoveryContext = recoveryContext;
        Dispatcher dispatcher = Dispatcher.UIThread;
        _dispatchToUi = dispatchToUi ?? (action =>
        {
            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.Post(action);
        });
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
            RefreshAfterLocalMutation();
        }).DisposeWith(_disposables);

        if (_recoveryContext is not null)
            _recoveryContext.IdentityChanged += RefreshForIdentity;
        IDisposable? changeSubscription = null;
        try
        {
            changeSubscription = (_library as IPromptLibraryChangeSource)?
                .SubscribeChanged(RefreshFromLibraryChange);
            _libraryChangeSubscription = changeSubscription;
            // Subscribe before the initial snapshot. A mutation that wins this race is then
            // either visible to the snapshot or followed by a notification; it cannot be lost
            // between reading the shared library and attaching the observer.
            string? account = CurrentAccount();
            long generation = Volatile.Read(ref _accountGeneration);
            Action initialRefresh = () =>
                ApplyQueuedRefresh(identityChanged: false, account, generation);
            _dispatchToUi(initialRefresh);
        }
        catch
        {
            changeSubscription?.Dispose();
            if (_recoveryContext is not null)
                _recoveryContext.IdentityChanged -= RefreshForIdentity;
            throw;
        }
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
        RefreshAfterLocalMutation();
    }

    public void Dispose()
    {
        lock (_refreshGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            Interlocked.Increment(ref _accountGeneration);
        }
        if (_recoveryContext is not null)
            _recoveryContext.IdentityChanged -= RefreshForIdentity;
        _libraryChangeSubscription?.Dispose();
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
            RefreshAfterLocalMutation();
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
            RefreshAfterLocalMutation();
        }
    }

    private void SaveTemplateCore()
    {
        Error.Value = null;
        try
        {
            _library.SaveTemplate(TemplateName.Value, _taskKind, _getPrompt());
            TemplateName.Value = string.Empty;
            RefreshAfterLocalMutation();
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

    private void RefreshFromLibraryChange()
    {
        string? account = CurrentAccount();
        long generation = Volatile.Read(ref _accountGeneration);
        QueueRefresh(identityChanged: false, account, generation);
    }

    private void QueueRefresh(bool identityChanged, string? account, long generation)
    {
        _dispatchToUi(() => ApplyQueuedRefresh(identityChanged, account, generation));
    }

    private void ApplyQueuedRefresh(bool identityChanged, string? account, long generation)
    {
        lock (_refreshGate)
        {
            if (_disposed)
                return;
            if (generation != Volatile.Read(ref _accountGeneration))
                return;
            string? currentAccount = CurrentAccount();
            if (generation != Volatile.Read(ref _accountGeneration)
                || !StringComparer.Ordinal.Equals(account, currentAccount))
            {
                return;
            }

            if (identityChanged)
            {
                RefreshForIdentityCore();
            }
            else
            {
                try
                {
                    Refresh();
                    if (Error.Value == Strings.AiResultUnavailable
                        || Error.Value == Strings.AiAuthenticationRequired)
                    {
                        Error.Value = null;
                    }
                }
                catch (InvalidDataException ex)
                {
                    ClearPromptChoices();
                    Error.Value = Strings.AiResultUnavailable;
                    System.Diagnostics.Trace.TraceWarning(
                        "Failed to read the prompt library: {0}",
                        ex.Message);
                }
                catch (Exception ex) when (ex is AuthenticationRequiredException
                    or IOException
                    or UnauthorizedAccessException)
                {
                    ClearPromptChoices();
                    Error.Value = ex is AuthenticationRequiredException
                        ? Strings.AiAuthenticationRequired
                        : Strings.AiResultUnavailable;
                }
            }
        }
    }

    private void RefreshAfterLocalMutation()
    {
        if (_libraryChangeSubscription is null)
            RefreshFromLibraryChange();
    }

    private void RefreshForIdentity()
    {
        long generation = Interlocked.Increment(ref _accountGeneration);
        QueueRefresh(identityChanged: true, CurrentAccount(), generation);
    }

    private string? CurrentAccount()
        => _recoveryContext?.TryGetIdentity()?.AccountId;

    private void RefreshForIdentityCore()
    {
        // Clear the previous account before touching the new account's store.
        // A corrupt or unavailable destination must never leave the old
        // account's prompt text visible in the new session.
        ClearPromptChoices();
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

    private void ClearPromptChoices()
    {
        _templates.Clear();
        _history.Clear();
        HasTemplates.Value = false;
        HasHistory.Value = false;
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
