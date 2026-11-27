using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Pages;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Services.PrimitiveImpls;

public sealed class ExtensionsToolWindowExtension : ToolWindowExtension
{
    public static readonly ExtensionsToolWindowExtension Instance = new();

    public override string Name => "ExtensionsToolWindow";

    public override string DisplayName => Strings.Extensions;

    public override ToolWindowMode Mode => ToolWindowMode.Dialog;

    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new ContextCommandDefinition<ExtensionsPageViewModel>("extensions.refresh", Strings.Refresh,
            keyGestures: [new("F5"), new("Ctrl+R"), new("Cmd+R")])
        {
            CanExecuteOn = (_, execution) => GetRefreshCommand(execution)?.CanExecute() == true,
            ExecuteOnAsync = (_, execution) => GetRefreshCommand(execution)?.ExecuteAsync() ?? Task.CompletedTask,
        },
    ];

    private static AsyncReactiveCommand? GetRefreshCommand(ContextCommandExecution execution)
    {
        if (execution.KeyEventArgs is not { } args)
            return null;

        // A custom typing key should stay in the search box. Function keys still refresh from there.
        if (execution.IsFromTextInput
            && (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == KeyModifiers.None
            && args.Key is not (>= Key.F1 and <= Key.F24))
            return null;

        return (TopLevel.GetTopLevel(args.Source as Visual) as ExtensionsPage)?.CurrentRefreshCommand;
    }

    public override FAIconSource? GetIcon()
        => new FluentIconSource() { Icon = Icon.PuzzlePiece };

    public override bool TryCreateContent([NotNullWhen(true)] out Window? window)
    {
        window = new ExtensionsPage();
        return true;
    }

    public override bool TryCreateContext([NotNullWhen(true)] out IToolWindowContext? context)
    {
        if (!MainViewModel.TryGetCurrent(out MainViewModel? mainViewModel))
        {
            context = null;
            return false;
        }

        context = new ExtensionsPageViewModel(mainViewModel._beutlClients, mainViewModel.EditorService, mainViewModel.ProjectService);
        return true;
    }
}
