using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.ChannelMapProperties.ViewModels;

public sealed class ChannelMapEditorViewModel : ValueEditorViewModel<AudioChannelMap>
{
    private readonly SourceSound? _sound;
    private readonly ReactivePropertySlim<bool> _hasSource = new();
    private AudioChannelLayout? _sourceLayout;
    private AudioChannelLayout? _rowSourceLayout;
    private AudioChannelFormat _rowFormat;
    private bool _refreshing;
    private EnumItem[] _sourceChannels = [];

    public ChannelMapEditorViewModel(IPropertyAdapter<AudioChannelMap> property) : base(property)
    {
        _hasSource.AddTo(Disposables);
        CanEditMap = CanEdit.CombineLatest(_hasSource, (canEdit, hasSource) => canEdit && hasSource)
            .ToReadOnlyReactivePropertySlim().AddTo(Disposables);
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

    public ObservableCollection<EnumItem> Formats { get; } = [];

    public ReactivePropertySlim<bool> IsExpanded { get; } = new(true);

    public ReadOnlyReactivePropertySlim<bool> CanEditMap { get; }

    public ReactivePropertySlim<int> SelectedFormatIndex { get; } = new();

    public ObservableCollection<ChannelAssignmentViewModel> Channels { get; } = [];

    private void RefreshSource()
    {
        using var source = _sound?.Source.Value?.ToResource(CompositionContext.Default);
        _sourceLayout = source is { MediaReader: not null, SampleRate: > 0, NumChannels: > 0 }
            ? source.ChannelLayout : null;
        _hasSource.Value = _sourceLayout != null;

        _refreshing = true;
        try
        {
            Formats.Clear();
            Formats.Add(new EnumItem(AudioStrings.ChannelMap_Auto, null, AudioChannelFormat.Auto));
            if (_sourceLayout is { } layout)
            {
                Formats.Add(new EnumItem(Strings.SpeakerLayout_Mono, null, AudioChannelFormat.Mono));
                Formats.Add(new EnumItem(Strings.SpeakerLayout_Stereo, null, AudioChannelFormat.Stereo));
                Formats.Add(new EnumItem(AudioStrings.ChannelMap_Surround51, null, AudioChannelFormat.Surround51));
                Formats.Add(new EnumItem(AudioStrings.ChannelMap_Surround71, null, AudioChannelFormat.Surround71));
                _sourceChannels = [new EnumItem(AudioStrings.ChannelMap_None, null, -1), .. Enumerable.Range(0, layout.ChannelCount)
                    .Select(channel => new EnumItem(AudioChannelMap.DescribeSourceChannel(layout, channel), null, channel))];
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

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        if (json[nameof(IsExpanded)] is JsonValue value && value.TryGetValue(out bool isExpanded))
            IsExpanded.Value = isExpanded;
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        json[nameof(IsExpanded)] = IsExpanded.Value;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        ClearChannels();
        IsExpanded.Dispose();
        SelectedFormatIndex.Dispose();
    }
}
