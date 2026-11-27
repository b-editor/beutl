using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentPermissionModePicker : UserControl
{
    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<AgentPermissionModePicker, bool>(nameof(IsCompact));

    private AgentTabViewModel? _viewModel;
    private bool _attached;
    private bool _syncingSelection;

    public AgentPermissionModePicker()
    {
        InitializeComponent();
        PermissionComboBox.TemplateApplied += OnComboBoxTemplateApplied;
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private static void OnComboBoxTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (e.NameScope.Find<Control>("DropDownGlyph") is not { } glyph)
            return;
        glyph.Margin = new Thickness(4, 1, 5, 0);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.MinWidth = 0;
        glyph.MinHeight = 0;
        for (StyledElement? parent = glyph.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is Grid grid && grid.ColumnDefinitions.Count >= 2)
            {
                grid.ColumnDefinitions[1].Width = GridLength.Auto;
                break;
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCompactProperty)
            PermissionComboBox.Classes.Set("compact", IsCompact);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_attached)
            AttachViewModel(DataContext as AgentTabViewModel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        AttachViewModel(DataContext as AgentTabViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        AttachViewModel(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void AttachViewModel(AgentTabViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
            return;
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = viewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncSelection();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AgentTabViewModel.SelectedPermissionMode) or nameof(AgentTabViewModel.PermissionModes) or null or "")
            SyncSelection();
    }

    private void SyncSelection()
    {
        _syncingSelection = true;
        try
        {
            PermissionComboBox.SelectedItem = _viewModel?.SelectedPermissionMode;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || DataContext is not AgentTabViewModel viewModel)
            return;
        if (PermissionComboBox.SelectedItem is AgentPermissionChoiceItem choice && choice != viewModel.SelectedPermissionMode)
        {
            await viewModel.SetPermissionModeAsync(choice);
            SyncSelection();
        }
    }
}
