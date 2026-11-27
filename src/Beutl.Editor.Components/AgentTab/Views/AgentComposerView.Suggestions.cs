using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Agents;
using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentComposerView
{
    private bool _suggestionsRefreshQueued;
    private AgentComposerToken? _suggestionToken;
    private AgentComposerToken? _dismissedToken;

    internal bool HasOpenSuggestions => SuggestionsPopup.IsOpen;
    internal void DismissSuggestions() => HandleSuggestionKey(Key.Escape);

    private void OnPromptBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
            _dismissedToken = null;
        if (e.Property == TextBox.TextProperty || e.Property == TextBox.CaretIndexProperty
            || e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty)
            QueueSuggestionsRefresh();
    }

    private void QueueSuggestionsRefresh()
    {
        if (_suggestionsRefreshQueued)
            return;

        _suggestionsRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _suggestionsRefreshQueued = false;
            RefreshSuggestions();
        }, DispatcherPriority.Input);
    }

    private void RefreshSuggestions()
    {
        if (!_attached || !IsEffectivelyVisible || !PromptBox.IsFocused || _viewModel is not { CanManage: true } model
            || HasActiveComposition() || !TryGetToken(out var token))
        {
            _dismissedToken = null;
            EndSuggestions();
            return;
        }
        if (_dismissedToken == token)
            return;

        model.DiscoverComposerSuggestions(token.Trigger);
        var suggestions = model.GetComposerSuggestions(token);
        if (suggestions.Length == 0 || ComposerCard.Bounds.Width <= 0)
        {
            CloseSuggestions();
            return;
        }

        var selected = _suggestionToken == token ? SuggestionsList.SelectedItem as AgentComposerSuggestionRow : null;
        if (SuggestionsList.ItemsSource is not IEnumerable<AgentComposerSuggestionRow> previous || !previous.SequenceEqual(suggestions))
        {
            SuggestionsList.ItemsSource = suggestions;
            SuggestionsList.SelectedIndex = Math.Max(0, Array.IndexOf(suggestions, selected));
        }
        else if (_suggestionToken != token)
        {
            SuggestionsList.SelectedIndex = 0;
        }
        _suggestionToken = token;
        SuggestionsCard.Width = ComposerCard.Bounds.Width;
        if (TopLevel.GetTopLevel(this) is { } top && ComposerCard.TranslatePoint(default, top) is { } anchor)
            SuggestionsList.MaxHeight = Math.Clamp(anchor.Y - 24, 70, 230);
        SuggestionsPopup.IsOpen = true;
    }

    private bool TryGetToken(out AgentComposerToken token)
    {
        token = default;
        return PromptBox.SelectionStart == PromptBox.SelectionEnd
            && AgentComposerToken.TryFind(PromptBox.Text ?? "", PromptBox.CaretIndex, out token);
    }

    private bool HandleSuggestionKey(Key key)
    {
        switch (key)
        {
            case Key.Up:
            case Key.Down:
                int delta = key == Key.Up ? -1 : 1;
                int index = Math.Clamp(SuggestionsList.SelectedIndex + delta, 0, SuggestionsList.ItemCount - 1);
                SuggestionsList.SelectedIndex = index;
                SuggestionsList.ScrollIntoView(SuggestionsList.SelectedItem!);
                return true;
            case Key.Tab:
            case Key.Enter:
                if (SuggestionsList.SelectedItem is AgentComposerSuggestionRow selection)
                    AcceptSuggestion(selection);
                return true;
            case Key.Escape:
                _dismissedToken = _suggestionToken;
                EndSuggestions();
                return true;
            default:
                return false;
        }
    }

    private void OnSuggestionPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Visual visual
            && visual.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is AgentComposerSuggestionRow selection)
        {
            e.Handled = true;
            AcceptSuggestion(selection);
        }
    }

    private void AcceptSuggestion(AgentComposerSuggestionRow suggestion)
    {
        if (HasActiveComposition() || !TryGetToken(out var token) || _suggestionToken != token
            || PromptBox.Text is not { } text || _viewModel is not { } model)
            return;

        string replacement = model.AcceptComposerSuggestion(suggestion);
        string suffix = text[token.End..];
        string separator = suffix.StartsWith(' ') ? "" : " ";
        PromptBox.SetCurrentValue(TextBox.TextProperty, text[..token.Start] + replacement + separator + suffix);
        int caret = token.Start + replacement.Length + 1;
        PromptBox.Focus();
        PromptBox.SelectionStart = caret;
        PromptBox.SelectionEnd = caret;
        PromptBox.CaretIndex = caret;
        EndSuggestions();
    }

    private void EndSuggestions()
    {
        CloseSuggestions();
        _viewModel?.ResetMentionCandidates();
    }

    private void CloseSuggestions()
    {
        SuggestionsPopup.IsOpen = false;
        _suggestionToken = null;
    }
}
