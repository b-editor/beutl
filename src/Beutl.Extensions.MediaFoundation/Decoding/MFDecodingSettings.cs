using Beutl.Extensibility;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public sealed class MFDecodingSettings : ExtensionSettings
{
    public static readonly CoreProperty<bool> UseDXVA2Property;

    static MFDecodingSettings()
    {
        UseDXVA2Property = ConfigureProperty<bool, MFDecodingSettings>(nameof(UseDXVA2))
            .DefaultValue(true)
            .Register();

        AffectsConfig<MFDecodingSettings>(UseDXVA2Property);
    }

    public bool UseDXVA2
    {
        get => GetValue(UseDXVA2Property);
        set => SetValue(UseDXVA2Property, value);
    }
}
