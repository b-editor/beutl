using System.Collections.ObjectModel;

using Avalonia;

using Beutl.Api.Services;
using Beutl.Controls.Navigation;

using DynamicData;
using DynamicData.Binding;

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

        NavigateToExtensionSettings = new AsyncReactiveCommand<Extension>()
            .WithSubscribe(async e =>
            {
                INavigationProvider nav = await GetNavigation();
                await nav.NavigateAsync(
                    x => x?.Extension == e,
                    () => new AnExtensionSettingsPageViewModel(e));
            });

        ICoreReadOnlyList<Extension> allExtension = ExtensionProvider.Current.AllExtensions;
        var comparer = SortExpressionComparer<Extension>.Ascending(i => i.Name);
        allExtension.ToObservableChangeSet<ICoreReadOnlyList<Extension>, Extension>()
            .Filter(v => v.Settings != null)
            .Sort(comparer)
            .Bind(out ReadOnlyObservableCollection<Extension>? extensions)
            .Subscribe();

        Extensions = extensions;
    }

    public EditorExtensionPriorityPageViewModel EditorPriority => _editorPriority ??= new();

    public DecoderPriorityPageViewModel DecoderPriority => _decoderPriority ??= new();

    public AsyncReactiveCommand NavigateToEditorPriority { get; }

    public AsyncReactiveCommand NavigateToDecoderPriority { get; }

    public AsyncReactiveCommand<Extension> NavigateToExtensionSettings { get; }

    public ReadOnlyObservableCollection<Extension> Extensions { get; }
}
