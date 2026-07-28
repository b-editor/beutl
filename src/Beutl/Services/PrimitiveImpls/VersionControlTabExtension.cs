using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using Beutl.Editor.Components.VersionControlTab.Views;
using Beutl.Editor.VersionControl;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class VersionControlTabExtension : ToolTabExtension
{
    public static readonly VersionControlTabExtension Instance = new();

    public override string Name => "Version Control";

    public override string DisplayName => Strings.VersionControl;

    public override string? Header => Strings.VersionControl;

    public override bool CanMultiple => false;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override int DefaultOrder => 110;

    public override bool OpenByDefault => false;

    public override bool TryCreateContent(
        IEditorContext editorContext,
        [NotNullWhen(true)] out Control? control)
    {
        if (SupportsVersionControl(editorContext))
        {
            control = new VersionControlTabView();
            return true;
        }

        control = null;
        return false;
    }

    public override bool TryCreateContext(
        IEditorContext editorContext,
        [NotNullWhen(true)] out IToolContext? context)
    {
        if (SupportsVersionControl(editorContext))
        {
            context = new VersionControlTabViewModel(this, editorContext);
            return true;
        }

        context = null;
        return false;
    }

    private static bool SupportsVersionControl(IEditorContext editorContext)
        => editorContext.GetService(typeof(IProjectVersionControlService))
               is IProjectVersionControlService
           && editorContext.GetService(typeof(IProjectVersionControlCoordinator))
               is IProjectVersionControlCoordinator;
}
