using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PathEditorTab.Views;
using Beutl.Editor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.PathEditorTab;

[PrimitiveImpl]
public sealed class PathEditorTabExtension : ToolTabExtension
{
    public static readonly PathEditorTabExtension Instance = new();

    public override string Name => "PathEditor";

    public override string DisplayName => Strings.PathEditor;

    public override bool CanMultiple => false;

    public override string? Header => Strings.PathEditor;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        // IEditorContextがITimelineOptionsProviderサービスを提供しているかチェック
        if (editorContext.GetService<ITimelineOptionsProvider>() != null)
        {
            control = new PathEditorTabView();
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
        if (editorContext.GetService<ITimelineOptionsProvider>() != null)
        {
            context = new PathEditorTabViewModel(editorContext);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
