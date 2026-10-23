using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Media.Encoding;

public partial class VideoEncoderSettings : MediaEncoderSettings
{
    public VideoEncoderSettings()
        : this(5_000_000, 12)
    {
    }

    // A derived type passes its own defaults for the bit rate and the key frame rate.
    protected VideoEncoderSettings(int defaultBitrate, int defaultKeyframeRate)
    {
        Bitrate = Property.CreateValue(defaultBitrate);
        KeyframeRate = Property.CreateValue(defaultKeyframeRate);
        ScanProperties<VideoEncoderSettings>();
    }

    [Browsable(false)]
    public IProperty<PixelSize> SourceSize { get; } = Property.CreateValue<PixelSize>(default);

    [Display(Name = nameof(Strings.FrameSize), Description = nameof(Strings.FrameSize_Tip), ResourceType = typeof(Strings), Order = int.MinValue)]
    public IProperty<PixelSize> DestinationSize { get; } = Property.CreateValue<PixelSize>(default);

    [Display(Name = nameof(Strings.FrameRate), Description = nameof(Strings.FrameRate_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 1)]
    public IProperty<Rational> FrameRate { get; } = Property.CreateValue<Rational>(new(30, 1));

    [Display(Name = nameof(Strings.Bitrate), Description = nameof(Strings.Bitrate_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 2)]
    public IProperty<int> Bitrate { get; }

    [Display(Name = nameof(Strings.KeyframeRate), Description = nameof(Strings.KeyframeRate_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 3)]
    public IProperty<int> KeyframeRate { get; }
}

