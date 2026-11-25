using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.UndisplayableDataTab.ViewModels;
using Beutl.Editor.Components.UndisplayableDataTab.Views;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.UndisplayableDataTab;

internal interface IUndisplayableDataNavigator
{
    void Locate(UndisplayableDataEntry entry);
}

public sealed class UndisplayableDataTabExtension : ToolTabExtension
{
    public static readonly UndisplayableDataTabExtension Instance = new();

    public override string Name => "Undisplayable Data";
    public override string DisplayName => UndisplayableDataStrings.Title;
    public override string Header => DisplayName;
    public override bool CanMultiple => false;
    public override DockAnchor DefaultAnchor => DockAnchor.Right;
    public override FAIconSource? GetIcon() => new FluentIconSource { Icon = Icon.DocumentError };

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = editorContext.Object is Scene ? new UndisplayableDataView() : null;
        return control is not null;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = editorContext.Object is Scene scene
            && editorContext.GetService<IUndisplayableDataNavigator>() is { } navigator
            ? new UndisplayableDataViewModel(editorContext, scene, navigator)
            : null;
        return context is not null;
    }
}
