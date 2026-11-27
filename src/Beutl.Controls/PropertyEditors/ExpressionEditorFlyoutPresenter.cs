using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

public partial class ExpressionEditorFlyoutPresenter : DraggablePickerFlyoutPresenter
{
    private readonly CompositeDisposable _disposables = [];
    private const string HasErrorPseudoClass = ":has-error";
    private const string HelpTabPseudoClass = ":help-tab";

    private RadioButton? _inputTabButton;
    private RadioButton? _helpTabButton;

    [GeneratedStyledProperty]
    public partial string? ExpressionText { get; set; }

    [GeneratedStyledProperty]
    public partial string? ErrorMessage { get; set; }

    [GeneratedStyledProperty]
    public partial string? ExpressionHint { get; set; }

    [GeneratedStyledProperty(DefaultValue = "Sin(Time * 2 * PI) * 100")]
    public partial string ExpressionPlaceholder { get; set; }

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
