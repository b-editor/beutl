using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Logging;
using FluentIcons.Common;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentDiagnosticCard : UserControl
{
    private static readonly ILogger s_logger = Log.CreateLogger<AgentDiagnosticCard>();

    public AgentDiagnosticCard() => InitializeComponent();

    private async void OnCopyCommand(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AgentDiagnosticItem { Issue.Command: { } command } diagnostic
            || !await AgentClipboardHelper.TryCopyTextAsync(this, command, s_logger, "Could not copy an agent diagnostic command."))
            return;

        diagnostic.CopyIcon = Icon.Checkmark;
        await Task.Delay(TimeSpan.FromSeconds(1));
        diagnostic.CopyIcon = Icon.Copy;
    }

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        if (this.FindAncestorOfType<AgentTabView>()?.DataContext is AgentTabViewModel viewModel
            && TopLevel.GetTopLevel(this) is Window owner)
            await viewModel.OpenAiAgentSettingsAsync(owner);
    }
}
