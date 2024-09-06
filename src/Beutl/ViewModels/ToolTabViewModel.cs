using FluentAvalonia.UI.Controls;

namespace Beutl.ViewModels;

public sealed class ToolTabViewModel(IToolContext context, EditViewModel editViewModel) : IDisposable
{
    public IToolContext Context { get; private set; } = context;

    public IconSource Icon { get; } = context.Extension.GetIcon();

    public EditViewModel EditViewModel { get; } = editViewModel;

    public void Dispose()
    {
        Context.Dispose();
        Context = null!;
    }
}
