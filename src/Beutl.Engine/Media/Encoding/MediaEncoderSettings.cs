using System.ComponentModel;
using System.Text.Json.Nodes;

namespace Beutl.Media.Encoding;

public abstract partial class MediaEncoderSettings : CoreObject
{
    protected MediaEncoderSettings()
    {
        ScanProperties<MediaEncoderSettings>();
    }

    [Obsolete]
    public IProperty<JsonNode?> CodecOptions { get; } = Property.CreateValue<JsonNode?>(null);

    // A setting raises PropertyChanged under its name.
    internal override void OnPropertyValueChanged(IProperty property)
    {
        if (!IsOwnField(property))
            OnPropertyChanged(new PropertyChangedEventArgs(property.Name));
    }
}
