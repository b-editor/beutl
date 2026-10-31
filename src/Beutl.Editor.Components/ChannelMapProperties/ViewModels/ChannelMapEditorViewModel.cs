using System.Collections.ObjectModel;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.ChannelMapProperties.ViewModels;

public sealed class ChannelMapEditorViewModel : ValueEditorViewModel<AudioChannelMap>
{
    private readonly SourceSound? _sound;
    private AudioChannelLayout? _sourceLayout;
    private AudioChannelLayout? _rowSourceLayout;
    private AudioChannelFormat _rowFormat;
    private bool _refreshing;
    private string[] _sourceChannels = [];

    public ChannelMapEditorViewModel(IPropertyAdapter<AudioChannelMap> property) : base(property)
    {
        _sound = property.GetEngineProperty()?.GetOwnerObject() as SourceSound;
        if (_sound != null)
        {
            Observable.FromEventPattern(h => _sound.Source.Edited += h, h => _sound.Source.Edited -= h)
                .Subscribe(_ => RefreshSource()).AddTo(Disposables);
        }

        RefreshSource();
        Value.Subscribe(_ => RefreshMap()).AddTo(Disposables);
        SelectedFormatIndex.Skip(1).Subscribe(index =>
        {
            if (_refreshing || !CanEdit.Value || index < 0 || index >= Formats.Count) return;
            AudioChannelMap map = index == 0 || _sourceLayout is not { } layout
                ? AudioChannelMap.Auto : AudioChannelMap.CreateDefault((AudioChannelFormat)index, layout);
            SetValue(Value.Value, map);
        }).AddTo(Disposables);
    }

    public ObservableCollection<string> Formats { get; } = [];

    public ReactivePropertySlim<int> SelectedFormatIndex { get; } = new();

    public ObservableCollection<ChannelAssignmentViewModel> Channels { get; } = [];

    public ReactivePropertySlim<string?> Message { get; } = new();

    private void RefreshSource()
    {
        using var source = _sound?.Source.Value?.ToResource(CompositionContext.Default);
        _sourceLayout = source is { MediaReader: not null, SampleRate: > 0, NumChannels: > 0 }
            ? source.ChannelLayout : null;

        _refreshing = true;
        try
        {
            Formats.Clear();
            Formats.Add(AudioStrings.ChannelMap_Auto);
            if (_sourceLayout is { } layout)
            {
                Formats.Add(Strings.SpeakerLayout_Mono);
                Formats.Add(Strings.SpeakerLayout_Stereo);
                Formats.Add(AudioStrings.ChannelMap_Surround51);
                Formats.Add(AudioStrings.ChannelMap_Surround71);
                _sourceChannels = [AudioStrings.ChannelMap_None, .. Enumerable.Range(0, layout.ChannelCount)
                    .Select(channel => AudioChannelMap.DescribeSourceChannel(layout, channel))];
            }
            else
            {
                _sourceChannels = [];
            }
        }
        finally
        {
            _refreshing = false;
        }
        RefreshMap();
    }

    private void RefreshMap()
    {
        _refreshing = true;
        try
        {
            AudioChannelMap map = _sourceLayout is { } layout ? Value.Value.Resolve(layout) : AudioChannelMap.Auto;
            SelectedFormatIndex.Value = (int)map.Format;
            Message.Value = _sourceLayout == null ? AudioStrings.ChannelMap_SourceUnavailable
                : !map.Equals(Value.Value) ? AudioStrings.ChannelMap_SourceChanged : null;
            if (_rowFormat == map.Format && _rowSourceLayout == _sourceLayout && Channels.Count == map.Sources.Length)
            {
                for (int channel = 0; channel < Channels.Count; channel++)
                    Channels[channel].SelectedIndex.Value = map.Sources[channel] + 1;
                return;
            }

            ClearChannels();
            _rowFormat = map.Format;
            _rowSourceLayout = _sourceLayout;
            if (map.Format == AudioChannelFormat.Auto) return;

            AudioChannelLayout clipLayout = map.ResolveClipLayout(_sourceLayout!.Value);
            for (int channel = 0; channel < map.Sources.Length; channel++)
            {
                int index = channel;
                Channels.Add(new ChannelAssignmentViewModel(
                    AudioChannelMap.GetChannelName(clipLayout, channel), _sourceChannels, map.Sources[channel] + 1,
                    selected => SetSource(index, selected - 1)));
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void SetSource(int channel, int source)
    {
        if (_refreshing || !CanEdit.Value || _sourceLayout is not { } layout) return;
        AudioChannelMap map = Value.Value.Resolve(layout);
        if (map.Format == AudioChannelFormat.Auto) return;
        SetValue(Value.Value, map.WithSources(map.Sources.SetItem(channel, source)));
    }

    private void ClearChannels()
    {
        foreach (var channel in Channels) channel.Dispose();
        Channels.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        ClearChannels();
        SelectedFormatIndex.Dispose();
        Message.Dispose();
    }
}
