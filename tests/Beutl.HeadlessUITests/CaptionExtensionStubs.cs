using Beutl.Editor.Services.Captions;

namespace Beutl.HeadlessUITests;

// Caption extensions that only hand out the registrations they are given.
internal sealed class TestCaptionTemplateDescriptorExtension(
    IReadOnlyCollection<CaptionTemplateDescriptorRegistration> registrations)
    : CaptionTemplateDescriptorExtension
{
    public override IReadOnlyCollection<CaptionTemplateDescriptorRegistration> Registrations
        => registrations;
}

internal sealed class TestCaptionElementFactoryExtension(
    IReadOnlyCollection<CaptionElementFactoryRegistration> registrations)
    : CaptionElementFactoryExtension
{
    public override IReadOnlyCollection<CaptionElementFactoryRegistration> Registrations
        => registrations;
}

internal sealed class TestCaptionPlacementExtension(
    IReadOnlyCollection<CaptionPlacementPolicyRegistration> registrations)
    : CaptionPlacementPolicyExtension
{
    public override IReadOnlyCollection<CaptionPlacementPolicyRegistration> Registrations
        => registrations;
}

internal sealed class TestCaptionCodecDescriptorExtension(
    IReadOnlyCollection<CaptionCodecDescriptorRegistration> registrations)
    : CaptionCodecDescriptorExtension
{
    public override IReadOnlyCollection<CaptionCodecDescriptorRegistration> Registrations
        => registrations;
}

internal sealed class TestCaptionDecoderExtension(
    IReadOnlyCollection<CaptionDecoderRegistration> registrations)
    : CaptionDecoderExtension
{
    public override IReadOnlyCollection<CaptionDecoderRegistration> Registrations
        => registrations;
}

internal sealed class TestCaptionEncoderExtension(
    IReadOnlyCollection<CaptionEncoderRegistration> registrations)
    : CaptionEncoderExtension
{
    public override IReadOnlyCollection<CaptionEncoderRegistration> Registrations
        => registrations;
}
