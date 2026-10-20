using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.HistoryTab.ViewModels;
using Beutl.Editor.Components.HistoryTab.Views;
using Beutl.Editor.Services;
using Beutl.Serialization;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.HistoryTab;

// Moved from the app; layouts and pins saved before name the former type.
[FormerPersistedName("[Beutl].Services.PrimitiveImpls:HistoryTabExtension")]
public sealed partial class HistoryTabExtension : ToolTabExtension
{
    public static readonly HistoryTabExtension Instance = new();

    public override string Name => "History";

    public override string DisplayName => Strings.History;

    public override string? Header => Strings.History;

    public override bool CanMultiple => false;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool OpenByDefault => false;

    public override int DefaultOrder => 100;

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.History };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (SupportsHistory(editorContext))
        {
            control = new HistoryView();
            return true;
        }

        control = null;
        return false;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (SupportsHistory(editorContext))
        {
            context = new HistoryViewModel(editorContext);
            return true;
        }

        context = null;
        return false;
    }

    private static bool SupportsHistory(IEditorContext editorContext)
        => editorContext.GetService(typeof(HistoryManager)) is HistoryManager
           && editorContext.GetService(typeof(IHistoryNavigator)) is IHistoryNavigator
           && editorContext.GetService(typeof(IUndoRedoEditorContext)) is IUndoRedoEditorContext;
}
