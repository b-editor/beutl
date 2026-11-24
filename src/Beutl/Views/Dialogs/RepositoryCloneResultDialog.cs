using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

internal sealed class RepositoryCloneResultDialog : FAContentDialog
{
    private static readonly FuncDataTemplate<string> s_actionLabel = new((label, _) => new TextBlock
    {
        Text = label,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.None,
    });

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        // The FluentAvalonia footer gives each action the same width. Let labels
        // wrap within that width so the footer can grow vertically on narrow windows.
        e.NameScope.Get<Button>("PrimaryButton").ContentTemplate = s_actionLabel;
        e.NameScope.Get<Button>("SecondaryButton").ContentTemplate = s_actionLabel;
        e.NameScope.Get<Button>("CloseButton").ContentTemplate = s_actionLabel;
    }
}
