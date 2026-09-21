using System.Collections.ObjectModel;
using Beutl.Language;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

internal sealed class AiVideoInputGroup : IDisposable
{
    private readonly Action _changed;
    public AiVideoInputGroup(string kind, string label, IReadOnlyReactiveProperty<bool> busy, Func<Task> pick, Action changed)
    {
        Kind = kind;
        Label = label;
        _changed = changed;
        Pick = new AsyncReactiveCommand(busy.CombineLatest(IsSupported, (active, supported) => !active && supported)).WithSubscribe(pick);
        Files.CollectionChanged += (_, _) => { UpdateHint(); _changed(); };
    }
    public string Kind { get; }
    public string Label { get; }
    public ObservableCollection<AiVideoInputFile> Files { get; } = [];
    public ReactivePropertySlim<bool> IsVisible { get; } = new();
    public ReactivePropertySlim<bool> IsSupported { get; } = new();
    public ReactivePropertySlim<string> Hint { get; } = new(string.Empty);
    public AsyncReactiveCommand Pick { get; }
    public int MaximumCount { get; private set; }
    public long MaximumBytes { get; private set; }
    public void SetLimits(bool enabled, int count, long bytes)
    {
        MaximumCount = count;
        MaximumBytes = bytes;
        IsSupported.Value = enabled && count > 0 && bytes > 0;
        IsVisible.Value = IsSupported.Value || Files.Count > 0;
        UpdateHint();
    }
    private void UpdateHint() => Hint.Value = string.Format(Strings.AiVideoReferenceLimit, Files.Count, MaximumCount, MaximumBytes / 1048576d);
    public void Add(string path, string? name = null)
    {
        if (Files.Any(file => file.Path == path)) return;
        var file = new AiVideoInputFile(path, name ?? Path.GetFileName(path));
        file.Remove.Subscribe(() => { Files.Remove(file); file.Dispose(); });
        Files.Add(file);
    }
    public void Dispose()
    {
        foreach (var file in Files) file.Dispose();
        Pick.Dispose(); IsVisible.Dispose(); IsSupported.Dispose(); Hint.Dispose();
    }
}
internal sealed class AiVideoInputFile(string path, string name) : IDisposable
{
    public string Path { get; } = path;
    public string Name { get; } = name;
    public string RemoveLabel => string.Format(Strings.AiVideoRemoveReference, Name);
    public ReactiveCommand Remove { get; } = new();
    public void Dispose() => Remove.Dispose();
}
