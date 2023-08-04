using System.Collections.ObjectModel;

using Avalonia.Platform.Storage;

using Beutl.Api.Services;
using Beutl.Services;

using DynamicData;
using DynamicData.Binding;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using ReactiveUI;

namespace Beutl.ViewModels.Dialogs;

public sealed class AddOutputQueueViewModel : IDisposable
{
    private readonly OutputService _outputService;
    private readonly EditorService _editorService;
    private readonly ExtensionProvider _extensionProvider;
    private readonly OutputExtension[] _extensions;
    private readonly ReadOnlyObservableCollection<string> _suggestion;
    private readonly IDisposable _disposable1;
    private readonly IDisposable _disposable2;
    private readonly ReadOnlyObservableCollection<OutputExtension> _availableExtensions;
    private FilePickerFileType[]? _cachedFileTypes;

    public AddOutputQueueViewModel()
    {
        _outputService = ServiceLocator.Current.GetRequiredService<OutputService>();
        _editorService = ServiceLocator.Current.GetRequiredService<EditorService>();
        _extensionProvider = ServiceLocator.Current.GetRequiredService<ExtensionProvider>();
        _extensions = _extensionProvider.GetExtensions<OutputExtension>();

        _disposable1 = _editorService.TabItems
            .ToObservableChangeSet<ICoreList<EditorTabItem>, EditorTabItem>()
            .Filter(x => !_outputService.Items.Any(y => y.Context.TargetFile == x.FilePath.Value))
            .Transform(x => x.FilePath.Value)
            .Bind(out _suggestion)
            .Subscribe();

        _disposable2 = _extensions.AsObservableChangeSet()
            .Filter(SelectedFile.Select<string?, Func<OutputExtension, bool>>(
                f => f == null
                    ? _ => false
                    : ext => ext.IsSupported(f)))
            .Bind(out _availableExtensions)
            .Subscribe();

        CanAdd = SelectedFile.Select(File.Exists)
            .CombineLatest(SelectedExtension.Select(x => x != null))
            .Select(x => x.First && x.Second)
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactiveProperty<string?> SelectedFile { get; } = new();

    public ReadOnlyObservableCollection<string> Suggestion => _suggestion;

    public ReadOnlyReactivePropertySlim<bool> CanAdd { get; }

    public ReactiveProperty<OutputExtension?> SelectedExtension { get; } = new();

    public ReadOnlyObservableCollection<OutputExtension> AvailableExtensions => _availableExtensions;

    public void Add()
    {
        if (SelectedFile.Value != null
            && SelectedExtension.Value != null)
        {
            _outputService.AddItem(SelectedFile.Value, SelectedExtension.Value);
        }
    }

    public FilePickerFileType[] GetFilePickerFileTypes()
    {
        return _cachedFileTypes ??= _extensions
            .Select(x => x.GetFilePickerFileType())
            .Where(x => x != null)
            .ToArray();
    }

    public void Dispose()
    {
        _disposable1.Dispose();
        _disposable2.Dispose();
    }
}
