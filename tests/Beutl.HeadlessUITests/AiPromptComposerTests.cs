using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiPromptComposerTests
{
    [Test]
    public void Compose_ProducesStableLabeledSectionsAndNormalizesWhitespace()
    {
        string result = AiPromptComposer.Compose(new AiPromptParts(
            "  A fox   in a forest ",
            Style: " cinematic  watercolor ",
            Composition: "wide shot",
            Motion: "slow push in",
            Exclusions: " text, logos "));

        Assert.That(result, Is.EqualTo(
            "A fox in a forest\n" +
            "Style: cinematic watercolor\n" +
            "Composition: wide shot\n" +
            "Motion: slow push in\n" +
            "Avoid: text, logos"));
    }

    [Test]
    public void Compose_OmitsEmptyOptionalSections()
    {
        string result = AiPromptComposer.Compose(new AiPromptParts(
            "Product photo",
            Style: " ",
            Exclusions: null));

        Assert.That(result, Is.EqualTo("Product photo"));
    }

    [Test]
    public void Compose_RejectsFinalPromptWhenOptionalSectionsCrossServerLimit()
    {
        var parts = new AiPromptParts(
            "subject",
            Style: new string('s', 2_000),
            Exclusions: new string('x', 2_000));

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            AiPromptComposer.Compose(parts))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Does.Contain("4000"));
            Assert.That(AiPromptComposer.GetValidationError(parts), Does.Contain("4000"));
        }
    }
}
