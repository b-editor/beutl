using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;

namespace Beutl.Pages.SettingsPages;

public sealed partial class DecoderPriorityPage : UserControl
{
    public DecoderPriorityPage()
    {
        InitializeComponent();

        listBox.ContainerPrepared += OnListBoxContainerPrepared;
        listBox.ContainerClearing += OnListBoxContainerClearing;
    }

    private void OnListBoxContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        Interaction.GetBehaviors(e.Container).Clear();
    }

    private void OnListBoxContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        Interaction.GetBehaviors(e.Container).Add(new DecoderPriorityListBoxItemBehavior());
    }
}
