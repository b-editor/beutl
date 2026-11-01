using Reactive.Bindings;

namespace Beutl.Editor.Components.ChannelMapProperties.ViewModels;

public sealed class ChannelAssignmentViewModel : IDisposable
{
    private readonly IDisposable _subscription;

    public ChannelAssignmentViewModel(string name, IReadOnlyList<string> sourceChannels, int selectedIndex, Action<int> changed)
    {
        Name = name;
        SourceChannels = sourceChannels;
        SelectedIndex = new(selectedIndex);
        _subscription = SelectedIndex.Skip(1).Where(index => index >= 0 && index < sourceChannels.Count).Subscribe(changed);
    }

    public string Name { get; }

    public IReadOnlyList<string> SourceChannels { get; }

    public ReactivePropertySlim<int> SelectedIndex { get; }

    public void Dispose()
    {
        _subscription.Dispose();
        SelectedIndex.Dispose();
    }
}
