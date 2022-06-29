using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using BeUtl.Framework;
using BeUtl.ViewModels;
using BeUtl.Views;

namespace BeUtl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class AnimationTimelineTabExtension : ToolTabExtension
{
    public static readonly AnimationTimelineTabExtension Instance = new();

    public override string Name => "Animation Timeline";

    public override string DisplayName => "Animation Timeline";

    public override bool CanMultiple => true;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out IControl? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new AnimationTimeline();
            return true;
        }
        else
        {
            control = null;
            return false;
        }
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        // EditorBadge.axaml.cs以外からは許可しない
        context = null;
        return false;
    }
}
