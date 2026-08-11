using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using Beutl.Language;
using Beutl.Services.AI;
using Reactive.Bindings;

namespace Beutl.ViewModels;

internal sealed class AiPromptLibraryViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly IPromptLibrary _library;
    private readonly PromptTaskKind _taskKind;
    private readonly Func<string> _getPrompt;
    private readonly Action<string> _applyPrompt;
    private readonly ObservableCollection<AiPromptChoice> _choices = [];

    public AiPromptLibraryViewModel(
        PromptTaskKind taskKind,
        Func<string> getPrompt,
        Action<string> applyPrompt,
        IPromptLibrary? library = null)
    {
        _taskKind = taskKind;
        _getPrompt = getPrompt ?? throw new ArgumentNullException(nameof(getPrompt));
        _applyPrompt = applyPrompt ?? throw new ArgumentNullException(nameof(applyPrompt));
        _library = library ?? PromptLibraryProvider.Current;
        Choices = new ReadOnlyObservableCollection<AiPromptChoice>(_choices);

        CanUseSelection = SelectedChoice
            .Select(choice => choice is not null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        ApplySelected = new ReactiveCommand(
            CanUseSelection,
            ImmediateScheduler.Instance,
            CanUseSelection.Value);
        ApplySelected.Subscribe(ApplySelectedCore).DisposeWith(_disposables);
        TogglePinSelected = new ReactiveCommand(
            CanUseSelection,
            ImmediateScheduler.Instance,
            CanUseSelection.Value);
        TogglePinSelected.Subscribe(TogglePinSelectedCore).DisposeWith(_disposables);
        DeleteSelected = new ReactiveCommand(
            CanUseSelection,
            ImmediateScheduler.Instance,
            CanUseSelection.Value);
        DeleteSelected.Subscribe(DeleteSelectedCore).DisposeWith(_disposables);
        SaveTemplate = new ReactiveCommand();
        SaveTemplate.Subscribe(SaveTemplateCore).DisposeWith(_disposables);
        ClearRecent = new ReactiveCommand();
        ClearRecent.Subscribe(() =>
        {
            _library.ClearHistory();
            Refresh();
        }).DisposeWith(_disposables);

        Refresh();
    }

    public ReadOnlyObservableCollection<AiPromptChoice> Choices { get; }

    public ReactivePropertySlim<AiPromptChoice?> SelectedChoice { get; } = new();

    public ReactivePropertySlim<string> TemplateName { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanUseSelection { get; }

    public ReactivePropertySlim<bool> HasChoices { get; } = new();

    public ReactivePropertySlim<string?> Error { get; } = new();

    public ReactiveCommand ApplySelected { get; }

    public ReactiveCommand TogglePinSelected { get; }

    public ReactiveCommand DeleteSelected { get; }

    public ReactiveCommand SaveTemplate { get; }

    public ReactiveCommand ClearRecent { get; }

    public string PrivacyText => Strings.AiPromptLibraryPrivacy;

    public void Record(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return;

        _library.Record(_taskKind, prompt);
        Refresh();
    }

    public void Dispose()
    {
        SelectedChoice.Dispose();
        TemplateName.Dispose();
        HasChoices.Dispose();
        Error.Dispose();
        _disposables.Dispose();
    }

    private void ApplySelectedCore()
    {
        if (SelectedChoice.Value is { } choice)
        {
            _applyPrompt(choice.Prompt);
            Error.Value = null;
        }
    }

    private void TogglePinSelectedCore()
    {
        if (SelectedChoice.Value is not { } choice)
            return;

        bool changed = choice.IsTemplate
            ? _library.SetTemplatePinned(choice.Id, !choice.IsPinned)
            : _library.SetHistoryPinned(choice.Id, !choice.IsPinned);
        if (changed)
        {
            Refresh();
        }
    }

    private void DeleteSelectedCore()
    {
        if (SelectedChoice.Value is not { } choice)
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
            PromptTemplate template = _library.SaveTemplate(
                TemplateName.Value,
                _taskKind,
                _getPrompt());
            TemplateName.Value = string.Empty;
            Refresh(template.Id);
        }
        catch (ArgumentException)
        {
            Error.Value = Strings.AiPromptTemplateInvalid;
        }
    }

    private void Refresh(Guid? selectedId = null)
    {
        Guid? previousId = selectedId ?? SelectedChoice.Value?.Id;
        _choices.Clear();
        foreach (PromptTemplate template in _library.Templates
                     .Where(item => item.TaskKind == _taskKind)
                     .OrderByDescending(item => item.IsPinned)
                     .ThenByDescending(item => item.UpdatedAtUtc))
        {
            _choices.Add(new AiPromptChoice(
                template.Id,
                template.Name,
                template.Prompt,
                IsTemplate: true,
                template.IsPinned));
        }

        foreach (PromptHistoryEntry history in _library.History
                     .Where(item => item.TaskKind == _taskKind)
                     .OrderByDescending(item => item.IsPinned)
                     .ThenByDescending(item => item.LastUsedAtUtc))
        {
            string summary = history.Prompt.Split('\n', 2)[0];
            if (summary.Length > 72)
            {
                summary = summary[..69] + "…";
            }
            _choices.Add(new AiPromptChoice(
                history.Id,
                summary,
                history.Prompt,
                IsTemplate: false,
                history.IsPinned));
        }

        HasChoices.Value = _choices.Count > 0;
        SelectedChoice.Value = previousId is { } id
            ? _choices.FirstOrDefault(choice => choice.Id == id)
            : null;
    }
}

internal sealed record AiPromptChoice(
    Guid Id,
    string Name,
    string Prompt,
    bool IsTemplate,
    bool IsPinned)
{
    public string DisplayName => IsPinned ? $"★ {Name}" : Name;

    public override string ToString() => DisplayName;
}
