using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.PropertyAdapters;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class VideoSourceEditorViewModel : LocalFileSourceEditorViewModel<VideoSource>
{
    private readonly EnumEditorViewModel<VideoTransferInterpretation>? _transferEditor;
    private readonly EnumEditorViewModel<DeinterlaceMode>? _deinterlaceMode;
    private readonly EnumEditorViewModel<DeinterlaceFieldOrder>? _deinterlaceFieldOrder;
    private readonly EnumEditorViewModel<DeinterlaceMethod>? _deinterlaceMethod;
    private readonly EnumEditorViewModel<DeinterlaceFrameRateMode>? _deinterlaceFrameRate;
    private readonly SourceVideo? _video;

    public VideoSourceEditorViewModel(IPropertyAdapter<VideoSource?> property) : base(property)
    {
        if (SourceVideo.FindSourceOwner(property.GetEngineProperty()) is { } video)
        {
            _video = video;
            _transferEditor = CreateEnumEditor(video.TransferInterpretation, video);
            _deinterlaceMode = CreateEnumEditor(video.DeinterlaceMode, video);
            _deinterlaceFieldOrder = CreateEnumEditor(video.DeinterlaceFieldOrder, video);
            _deinterlaceMethod = CreateEnumEditor(video.DeinterlaceMethod, video);
            _deinterlaceFrameRate = CreateEnumEditor(video.DeinterlaceFrameRateMode, video);
        }
    }

    internal bool HasTransferInterpretation => _transferEditor is not null;

    internal VideoTransferInterpretation? SelectedTransferInterpretation
        => GetSelectedValue(_transferEditor);

    internal bool HasDeinterlace => _deinterlaceMode != null;

    internal DeinterlaceMode? SelectedDeinterlaceMode => GetSelectedValue(_deinterlaceMode);
    internal DeinterlaceFieldOrder? SelectedDeinterlaceFieldOrder => GetSelectedValue(_deinterlaceFieldOrder);
    internal DeinterlaceMethod? SelectedDeinterlaceMethod => GetSelectedValue(_deinterlaceMethod);
    internal DeinterlaceFrameRateMode? SelectedDeinterlaceFrameRate => GetSelectedValue(_deinterlaceFrameRate);

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is IServiceProvider)
        {
            _transferEditor?.Accept(visitor);
            _deinterlaceMode?.Accept(visitor);
            _deinterlaceFieldOrder?.Accept(visitor);
            _deinterlaceMethod?.Accept(visitor);
            _deinterlaceFrameRate?.Accept(visitor);
        }
    }

    internal void SetTransferInterpretation(VideoTransferInterpretation value)
    {
        SetEnumValue(_transferEditor, value);
    }

    internal void SetDeinterlaceOption(object? option)
    {
        if (option is DeinterlaceMode mode)
        {
            SetEnumValue(_deinterlaceMode, mode);
        }
        else if (SelectedDeinterlaceMode != DeinterlaceMode.Off)
        {
            switch (option)
            {
                case DeinterlaceFieldOrder fieldOrder:
                    SetEnumValue(_deinterlaceFieldOrder, fieldOrder);
                    break;
                case DeinterlaceMethod method:
                    SetEnumValue(_deinterlaceMethod, method);
                    break;
                case DeinterlaceFrameRateMode frameRate:
                    SetEnumValue(_deinterlaceFrameRate, frameRate);
                    break;
            }
        }
    }

    internal async Task<VideoStreamInfo?> ProbeDeinterlaceStatusAsync(DecoderRegistry decoders, bool preferProxy, ProxyPreset preset)
    {
        if (IsDisposed || _video == null || ValueDifference.Value != null || SelectedDeinterlaceMode is not { } mode
            || (mode != DeinterlaceMode.Off && (SelectedDeinterlaceFieldOrder == null
                || SelectedDeinterlaceMethod == null || SelectedDeinterlaceFrameRate == null))
            || Value.Value is not { HasUri: true, Uri: { IsFile: true } uri })
        {
            return null;
        }

        var options = new MediaOptions(MediaMode.Video)
        {
            Deinterlace = _video.GetDeinterlaceOptions(),
            PreferProxy = preferProxy,
            PreferredProxyPreset = preset,
            IsBackground = true,
        };
        var info = await Task.Run(() =>
        {
            try
            {
                using var reader = decoders.OpenMediaFile(uri.LocalPath, options);
                return reader is { HasVideo: true } ? reader.VideoInfo : null;
            }
            catch (Exception)
            {
                // An unreadable source has no status. The settings themselves are still editable.
                return null;
            }
        });
        return !IsDisposed && Value.Value is { HasUri: true } source && source.Uri == uri
            && _video.GetDeinterlaceOptions() == options.Deinterlace ? info : null;
    }

    private EnumEditorViewModel<T> CreateEnumEditor<T>(IProperty<T> property, SourceVideo owner) where T : struct, Enum
    {
        var adapter = (IPropertyAdapter<T>)PropertyAdapterFactory.CreateAdapter(property, owner);
        var editor = new EnumEditorViewModel<T>(adapter);
        Own(editor);
        return editor;
    }

    private static T? GetSelectedValue<T>(EnumEditorViewModel<T>? editor) where T : struct, Enum
        => editor is { ValueDifference.Value: null } ? editor.PropertyAdapter.GetValue() : null;

    private void SetEnumValue<T>(EnumEditorViewModel<T>? editor, T value) where T : struct, Enum
    {
        if (!IsDisposed && CanEditProperty.Value && editor != null)
            editor.SetValue(editor.PropertyAdapter.GetValue(), value);
    }
}
