using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using BeUtl.Framework;
using BeUtl.ViewModels;
using BeUtl.Views;

namespace BeUtl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class TimelineTabExtension : ToolTabExtension
{
    public static readonly TimelineTabExtension Instance = new();

    public override string Name => "Timeline";

    public override string DisplayName => "Timeline";

    public override bool CanMultiple => false;

    public override ResourceReference<string>? Header => "S.Common.Timeline";

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out IControl? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new Timeline();
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
        if (editorContext is EditViewModel editViewModel)
        {
            context = new TimelineViewModel(editViewModel.Scene, editViewModel.Player);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
