using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Beutl.Views.Editors;

// The visibility toggle and the edit or add button in the header of
// AudioEffectEditor, FilterEffectEditor and TransformEditor.
public partial class EffectEditorHeaderButtons : UserControl
{
    public static readonly RoutedEvent<RoutedEventArgs> ClickEvent =
        RoutedEvent.Register<EffectEditorHeaderButtons, RoutedEventArgs>(nameof(Click), RoutingStrategies.Bubble);

    public EffectEditorHeaderButtons()
    {
        InitializeComponent();
    }

    public event EventHandler<RoutedEventArgs>? Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
    }
}
