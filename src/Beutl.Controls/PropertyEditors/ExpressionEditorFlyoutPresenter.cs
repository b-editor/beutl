#nullable enable

using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

public class ExpressionEditorFlyoutPresenter : DraggablePickerFlyoutPresenter
{
    public static readonly StyledProperty<string?> ExpressionTextProperty =
        AvaloniaProperty.Register<ExpressionEditorFlyoutPresenter, string?>(nameof(ExpressionText));

    public static readonly StyledProperty<string?> ErrorMessageProperty =
        AvaloniaProperty.Register<ExpressionEditorFlyoutPresenter, string?>(nameof(ErrorMessage));

    private readonly CompositeDisposable _disposables = [];
    private const string HasErrorPseudoClass = ":has-error";
    private const string HelpTabPseudoClass = ":help-tab";

    private RadioButton? _inputTabButton;
    private RadioButton? _helpTabButton;

    public string? ExpressionText
    {
        get => GetValue(ExpressionTextProperty);
        set => SetValue(ExpressionTextProperty, value);
    }

    public string? ErrorMessage
    {
        get => GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);

        _inputTabButton = e.NameScope.Find<RadioButton>("InputTabButton");
        _helpTabButton = e.NameScope.Find<RadioButton>("HelpTabButton");

        _inputTabButton?.AddDisposableHandler(ToggleButton.IsCheckedChangedEvent, OnTabButtonIsCheckedChanged)
            .DisposeWith(_disposables);

        _helpTabButton?.AddDisposableHandler(ToggleButton.IsCheckedChangedEvent, OnTabButtonIsCheckedChanged)
            .DisposeWith(_disposables);

        if (_inputTabButton != null)
        {
            _inputTabButton.IsChecked = true;
        }
    }

    private void OnTabButtonIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_helpTabButton != null)
        {
            PseudoClasses.Set(HelpTabPseudoClass, _helpTabButton.IsChecked == true);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ErrorMessageProperty)
        {
            PseudoClasses.Set(HasErrorPseudoClass, !string.IsNullOrWhiteSpace(ErrorMessage));
        }
    }
}
