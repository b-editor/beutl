using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.TransitionTab.ViewModels;
using Beutl.Editor.Components.TransitionTab.Views;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.TransitionTab;

// Moved from the app; layouts and pins saved before name the former type.
[FormerPersistedName("[Beutl].Services.PrimitiveImpls:TransitionTabExtension")]
public sealed partial class TransitionTabExtension : ToolTabExtension
{
    public static readonly TransitionTabExtension Instance = new();

    public override string Name => "Transition";

    public override string DisplayName => GraphicsStrings.ClipTransition;

    // Opened from the timeline on a transition, not from the menu.
    public override string? Header => null;

    public override bool CanMultiple => true;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool OpenByDefault => false;

    public override int DefaultOrder => 90;

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.SlideTransition };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (SupportsTransitions(editorContext))
        {
            control = new TransitionTabView();
            return true;
        }

        control = null;
        return false;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (SupportsTransitions(editorContext))
        {
            context = new TransitionTabViewModel(editorContext);
            return true;
        }

        context = null;
        return false;
    }

    private static bool SupportsTransitions(IEditorContext editorContext)
        => editorContext.GetService(typeof(Scene)) is Scene
           && editorContext.GetService(typeof(IElementAttributeService)) is IElementAttributeService
           && editorContext.GetService(typeof(IPropertiesEditorFactory)) is IPropertiesEditorFactory;
}
