using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentQuestionCard : UserControl
{
    public AgentQuestionCard()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnQuestionKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        FocusPage();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (IsLoaded && (change.Property == DataContextProperty || change.Property == IsVisibleProperty))
            Dispatcher.UIThread.Post(FocusPage, DispatcherPriority.Input);
    }

    private void FocusPage()
    {
        if (!IsLoaded || !IsEffectivelyVisible || DataContext is not AgentQuestionItem { IsPending: true, CurrentQuestion: { } question })
            return;

        if (question.ShowAnswerBox)
            AnswerBox.Focus();
        else if (question.HasSingleOptions)
            SingleOptions.Focus();
        else
            MultiOptions.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault()?.Focus();
    }

    private void OnBack(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AgentQuestionItem item)
        {
            item.Back();
            Dispatcher.UIThread.Post(FocusPage, DispatcherPriority.Input);
        }
    }

    private void OnSkip(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AgentQuestionItem item)
        {
            var previous = item.CurrentQuestion;
            item.Skip();
            if (!ReferenceEquals(previous, item.CurrentQuestion))
                Dispatcher.UIThread.Post(FocusPage, DispatcherPriority.Input);
        }
    }

    private void OnAdvance(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AgentQuestionItem item)
            Advance(item);
    }

    private void Advance(AgentQuestionItem item)
    {
        var previous = item.CurrentQuestion;
        item.Advance();
        if (!ReferenceEquals(previous, item.CurrentQuestion))
            Dispatcher.UIThread.Post(FocusPage, DispatcherPriority.Input);
        else if (item.IsPending && previous?.ShowAnswerBox == true && !previous.HasAnswer)
            AnswerBox.Focus();
    }

    private void OnQuestionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || DataContext is not AgentQuestionItem { IsPending: true, CurrentQuestion: { } question } item
            || QuestionBack.IsKeyboardFocusWithin || QuestionSkip.IsKeyboardFocusWithin || QuestionAdvance.IsKeyboardFocusWithin)
            return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Advance(item);
            return;
        }
        if (!question.HasSingleOptions || AnswerBox.IsKeyboardFocusWithin)
            return;

        int index = e.Key switch
        {
            Key.Up => Math.Max(0, question.SelectedIndex - 1),
            Key.Down => Math.Min(question.SingleOptions.Count - 1, question.SelectedIndex + 1),
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
            _ => -1
        };
        if (index >= 0 && index < question.SingleOptions.Count)
        {
            e.Handled = true;
            question.SelectedIndex = index;
            SingleOptions.ScrollIntoView(question.SingleOptions[index]);
        }
    }
}
