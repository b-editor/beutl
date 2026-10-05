using System.Collections.ObjectModel;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed record CommandPaletteChoiceViewModel(string Label, string? Description, object Item);

public sealed class CommandPalettePromptViewModel : IDisposable
{
    private readonly Func<string, string?>? _validate;
    private readonly IReadOnlyList<CommandPaletteChoiceViewModel> _choices;
    private readonly TaskCompletionSource<object?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IDisposable _selectionSubscription;

    internal CommandPalettePromptViewModel(
        string title, string? message, string? placeholder, bool isInput,
        Func<string, string?>? validate = null,
        IReadOnlyList<CommandPaletteChoiceViewModel>? choices = null)
    {
        Title = title;
        Message = message;
        Placeholder = placeholder ?? (isInput
            ? Strings.CommandPalette_InputPlaceholder
            : Strings.CommandPalette_ChoicePlaceholder);
        IsInput = isInput;
        _validate = validate;
        _choices = choices ?? [];
        _selectionSubscription = SelectedChoice.Subscribe(_ => UpdateCanConfirm());
    }

    public string Title { get; }

    public string? Message { get; }

    public string Placeholder { get; }

    public bool IsInput { get; }

    public bool IsPick => !IsInput;

    public ObservableCollection<CommandPaletteChoiceViewModel> FilteredChoices { get; } = [];

    public ReactivePropertySlim<CommandPaletteChoiceViewModel?> SelectedChoice { get; } = new();

    public ReactivePropertySlim<string?> ValidationError { get; } = new();

    public ReactivePropertySlim<bool> HasNoResults { get; } = new(false);

    public ReactivePropertySlim<bool> CanConfirm { get; } = new(false);

    internal Task<object?> Completion => _completion.Task;

    internal bool IsCompleted => _completion.Task.IsCompleted;

    internal void Update(string value)
    {
        if (IsCompleted) return;

        if (IsInput)
        {
            ValidationError.Value = _validate?.Invoke(value);
        }
        else
        {
            string query = value.Trim();
            CommandPaletteChoiceViewModel? previous = SelectedChoice.Value;
            FilteredChoices.Clear();
            foreach (CommandPaletteChoiceViewModel choice in _choices)
            {
                if (query.Length == 0
                    || choice.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || choice.Description?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true)
                {
                    FilteredChoices.Add(choice);
                }
            }

            SelectedChoice.Value = previous is not null && FilteredChoices.Contains(previous)
                ? previous
                : FilteredChoices.FirstOrDefault();
            HasNoResults.Value = FilteredChoices.Count == 0;
        }

        UpdateCanConfirm();
    }

    internal void MoveSelection(int delta)
    {
        if (FilteredChoices.Count == 0) return;
        int index = SelectedChoice.Value is { } selected ? FilteredChoices.IndexOf(selected) : -1;
        SelectedChoice.Value = FilteredChoices[Math.Clamp(index + delta, 0, FilteredChoices.Count - 1)];
    }

    internal void Submit(string value)
    {
        if (IsCompleted) return;
        Update(value);
        if (!CanConfirm.Value) return;

        CanConfirm.Value = false;
        _completion.TrySetResult(IsInput ? value : SelectedChoice.Value!.Item);
    }

    internal void Cancel() => _completion.TrySetResult(null);

    private void UpdateCanConfirm()
    {
        CanConfirm.Value = !IsCompleted && (IsInput
            ? string.IsNullOrEmpty(ValidationError.Value)
            : SelectedChoice.Value is not null);
    }

    public void Dispose()
    {
        Cancel();
        _selectionSubscription.Dispose();
        SelectedChoice.Dispose();
        ValidationError.Dispose();
        HasNoResults.Dispose();
        CanConfirm.Dispose();
    }
}
