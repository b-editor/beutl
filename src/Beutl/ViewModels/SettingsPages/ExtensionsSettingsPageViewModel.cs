using Beutl.Controls.Navigation;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class ExtensionsSettingsPageViewModel : PageContext
{
    private EditorExtensionPriorityPageViewModel? _editorPriority;
    private DecoderPriorityPageViewModel? _decoderPriority;

    public ExtensionsSettingsPageViewModel()
    {
        NavigateToEditorPriority = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                INavigationProvider nav = await GetNavigation();
                await nav.NavigateAsync(
                    x => x is not null,
                    () => EditorPriority);
            });

        NavigateToDecoderPriority = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                INavigationProvider nav = await GetNavigation();
                await nav.NavigateAsync(
                    x => x is not null,
                    () => DecoderPriority);
            });
    }

    public EditorExtensionPriorityPageViewModel EditorPriority => _editorPriority ??= new();

    public DecoderPriorityPageViewModel DecoderPriority => _decoderPriority ??= new();

    public AsyncReactiveCommand NavigateToEditorPriority { get; }

    public AsyncReactiveCommand NavigateToDecoderPriority { get; }
}
