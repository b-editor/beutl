using System.ComponentModel;
using System.Text.Json.Nodes;

namespace Beutl.Media.Encoding;

public abstract class MediaEncoderSettings : CoreObject
{
    protected MediaEncoderSettings()
    {
        ScanProperties<MediaEncoderSettings>();
    }

    [Obsolete]
    public IProperty<JsonNode?> CodecOptionsProperty { get; } = new ValueOnlyProperty<JsonNode?>(null);

    [Obsolete()]
    public JsonNode? CodecOptions
    {
        get => CodecOptionsProperty.Value;
        set => CodecOptionsProperty.Value = value;
    }

    // A setting raises PropertyChanged under its name, as its CoreProperty field did.
    internal override void OnPropertyValueChanged(IProperty property)
    {
        if (!IsOwnField(property))
            OnPropertyChanged(new PropertyChangedEventArgs(property.Name));
    }
}
