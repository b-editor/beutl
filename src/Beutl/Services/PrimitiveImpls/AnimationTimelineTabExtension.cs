using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Framework;
using Beutl.ViewModels;
using Beutl.Views;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class AnimationTimelineTabExtension : ToolTabExtension
{
    public static readonly AnimationTimelineTabExtension Instance = new();

    public override string Name => "Animation Timeline";

    public override string DisplayName => "Animation Timeline";

    public override bool CanMultiple => true;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out IControl? control)
    {
        //if (editorContext is EditViewModel)
        //{
        //    control = new AnimationTimeline();
        //    return true;
        //}
        //else
        {
            control = null;
            return false;
        }
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = null;
        return false;
    }
}
