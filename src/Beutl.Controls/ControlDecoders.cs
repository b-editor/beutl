using Avalonia;
using Beutl.Media.Decoding;

namespace Beutl.Controls;

public sealed class ControlDecoders
{
    public static readonly AttachedProperty<DecoderRegistry?> DecodersProperty =
        AvaloniaProperty.RegisterAttached<ControlDecoders, StyledElement, DecoderRegistry?>("Decoders", inherits: true);

    public static DecoderRegistry? GetDecoders(AvaloniaObject element) => element.GetValue(DecodersProperty);

    public static void SetDecoders(AvaloniaObject element, DecoderRegistry? decoders) => element.SetValue(DecodersProperty, decoders);
}
