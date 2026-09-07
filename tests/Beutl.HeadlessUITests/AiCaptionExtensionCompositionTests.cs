using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Models;
using Beutl.Editor.Services.Captions;
using Beutl.Extensibility;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiCaptionExtensionCompositionTests
{
    private const int TestPackageId = -42_001;
    private static readonly CaptionFormatId s_testFormat = new("test.caption");

    [AvaloniaTest]
    public async Task OpenTool_UsesDynamicContributionsAndDropsThemAfterUnload()
    {
        await TestReset.ResetShellAsync();
        (WeakReference codecReference, WeakReference factoryReference) =
            RegisterTestExtensions();
        using var openTool =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(editViewModel: null);
        try
        {
            bool imported = openTool.ImportCaptionBytes(
                Encoding.UTF8.GetBytes("from plugin codec"),
                s_testFormat);
            CaptionTemplateDescriptor pluginTemplate = openTool.CaptionTemplates
                .Single(template => template.Name == "Plugin caption template");
            openTool.SelectedCaptionTemplate.Value = pluginTemplate;
            Assert.Multiple(() =>
            {
                Assert.That(imported, Is.True);
                Assert.That(openTool.Cues.Single().Text, Is.EqualTo("from plugin codec"));
                Assert.That(
                    openTool.CaptionTemplates.Select(template => template.Name),
                    Does.Contain("Plugin caption template"));
            });
        }
        finally
        {
            RemoveTestExtensions();
        }

        // Move collection to a later continuation so the JIT cannot keep the
        // removed extension array alive as a temporary in the removal frame.
        await Task.Yield();

        using var newlyOpenedTool =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(editViewModel: null);
        Assert.Multiple(() =>
        {
            Assert.That(
                openTool.CaptionTemplates.Select(template => template.Name),
                Does.Not.Contain("Plugin caption template"));
            Assert.That(
                openTool.SelectedCaptionTemplate.Value.Id,
                Is.EqualTo(CaptionTemplateIds.DefaultText));
            Assert.Throws<NotSupportedException>(() =>
                openTool.ImportCaptionBytes(
                    Encoding.UTF8.GetBytes("no longer registered"),
                    s_testFormat));
            Assert.That(
                newlyOpenedTool.CaptionTemplates.Select(template => template.Name),
                Does.Not.Contain("Plugin caption template"));
            Assert.That(Collect(codecReference), Is.False);
            Assert.That(Collect(factoryReference), Is.False);
        });
    }

    [AvaloniaTest]
    public async Task InvalidCodecContribution_DoesNotSuppressTemplateContribution()
    {
        await TestReset.ResetShellAsync();
        TestShell.Extensions.AddExtensions(
            TestPackageId,
            [
                new InvalidCaptionDecoderExtension(),
                .. CreateTemplateExtensions(new TestCaptionElementFactory()),
            ]);
        try
        {
            using var viewModel =
                TestShell.MainViewModel.CreateAiSubtitleToolViewModel(editViewModel: null);

            Assert.That(
                viewModel.CaptionTemplates.Select(template => template.Name),
                Does.Contain("Plugin caption template"));
        }
        finally
        {
            TestShell.Extensions.RemoveExtensions(TestPackageId);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Codec, WeakReference Factory) RegisterTestExtensions()
    {
        var codec = new TestCaptionCodec();
        var factory = new TestCaptionElementFactory();
        TestShell.Extensions.AddExtensions(
            TestPackageId,
            [.. CreateCodecExtensions(codec), .. CreateTemplateExtensions(factory)]);
        return (new WeakReference(codec), new WeakReference(factory));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RemoveTestExtensions()
    {
        _ = TestShell.Extensions.RemoveExtensions(TestPackageId);
    }

    private static bool Collect(WeakReference reference)
    {
        for (int i = 0; reference.IsAlive && i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return reference.IsAlive;
    }

    private static Extension[] CreateCodecExtensions(TestCaptionCodec codec)
        =>
        [
            new TestCaptionCodecDescriptorExtension([
                new CaptionCodecDescriptorRegistration(
                    new CaptionCodecDescriptor(s_testFormat, [".plugcap"])),
            ]),
            new TestCaptionDecoderExtension([
                new CaptionDecoderRegistration(s_testFormat, codec),
            ]),
            new TestCaptionEncoderExtension([
                new CaptionEncoderRegistration(s_testFormat, codec),
            ]),
        ];

    private sealed class TestCaptionCodecDescriptorExtension(
        IReadOnlyCollection<CaptionCodecDescriptorRegistration> registrations)
        : CaptionCodecDescriptorExtension
    {
        public override IReadOnlyCollection<CaptionCodecDescriptorRegistration> Registrations
            => registrations;
    }

    private sealed class TestCaptionDecoderExtension(
        IReadOnlyCollection<CaptionDecoderRegistration> registrations)
        : CaptionDecoderExtension
    {
        public override IReadOnlyCollection<CaptionDecoderRegistration> Registrations
            => registrations;
    }

    private sealed class TestCaptionEncoderExtension(
        IReadOnlyCollection<CaptionEncoderRegistration> registrations)
        : CaptionEncoderExtension
    {
        public override IReadOnlyCollection<CaptionEncoderRegistration> Registrations
            => registrations;
    }

    private sealed class InvalidCaptionDecoderExtension : CaptionDecoderExtension
    {
        public override IReadOnlyCollection<CaptionDecoderRegistration> Registrations => [null!];
    }

    private static Extension[] CreateTemplateExtensions(ICaptionElementFactory factory)
    {
        var id = new CaptionTemplateId("beutl.tests.plugin-caption");
        return
        [
            new TestCaptionTemplateDescriptorExtension([
                new CaptionTemplateDescriptorRegistration(new CaptionTemplateDescriptor(
                    id,
                    new CaptionTemplateProviderId("beutl.tests"),
                    "Plugin caption template")),
            ]),
            new TestCaptionElementFactoryExtension([
                new CaptionElementFactoryRegistration(id, factory),
            ]),
            new TestCaptionPlacementExtension([
                new CaptionPlacementPolicyRegistration(
                    id,
                    DefaultCaptionPlacementPolicy.Instance),
            ]),
        ];
    }

    private sealed class TestCaptionTemplateDescriptorExtension(
        IReadOnlyCollection<CaptionTemplateDescriptorRegistration> registrations)
        : CaptionTemplateDescriptorExtension
    {
        public override IReadOnlyCollection<CaptionTemplateDescriptorRegistration> Registrations
            => registrations;
    }

    private sealed class TestCaptionElementFactoryExtension(
        IReadOnlyCollection<CaptionElementFactoryRegistration> registrations)
        : CaptionElementFactoryExtension
    {
        public override IReadOnlyCollection<CaptionElementFactoryRegistration> Registrations
            => registrations;
    }

    private sealed class TestCaptionPlacementExtension(
        IReadOnlyCollection<CaptionPlacementPolicyRegistration> registrations)
        : CaptionPlacementPolicyExtension
    {
        public override IReadOnlyCollection<CaptionPlacementPolicyRegistration> Registrations
            => registrations;
    }

    private sealed class TestCaptionCodec : ICaptionDecoder, ICaptionEncoder
    {
        public CaptionImportResult Decode(string content)
            => CaptionImportResult.Imported(new CaptionDocument(
            [
                new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), content),
            ]));

        public string Encode(CaptionDocument document)
            => string.Join("\n", document.Cues.Select(cue => cue.Text));
    }

    private sealed class TestCaptionElementFactory : ICaptionElementFactory
    {
        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
            => throw new NotSupportedException();
    }
}
