using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.Media.Encoding;

public partial class AudioEncoderSettings : MediaEncoderSettings
{
    public AudioEncoderSettings()
        : this(128_000)
    {
    }

    // A derived type passes its own default for the bit rate.
    protected AudioEncoderSettings(int defaultBitrate)
    {
        Bitrate = Property.CreateValue(defaultBitrate);
        ScanProperties<AudioEncoderSettings>();
    }

    [Display(Name = nameof(Strings.SampleRate), Description = nameof(Strings.SampleRate_Tip), ResourceType = typeof(Strings), Order = int.MinValue)]
    public IProperty<int> SampleRate { get; } = Property.CreateValue(44100);

    [Display(Name = nameof(Strings.Channels), Description = nameof(Strings.Channels_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 1)]
    public IProperty<int> Channels { get; } = Property.CreateValue(2);

    [Display(Name = nameof(Strings.Bitrate), Description = nameof(Strings.Bitrate_Tip), ResourceType = typeof(Strings), Order = int.MinValue + 2)]
    public IProperty<int> Bitrate { get; }
}

