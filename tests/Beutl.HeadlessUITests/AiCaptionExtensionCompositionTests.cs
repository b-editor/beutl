using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Collections;
using Beutl.Editor.Models;
using Beutl.Editor.Services.Captions;
using Beutl.Extensibility;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Tools;
using DynamicData;
using DynamicData.Binding;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiCaptionExtensionCompositionTests
{
    private const int TestPackageId = -42_001;
    private const int OtherTestPackageId = -42_002;
    private static readonly CaptionFormatId s_testFormat = new("test.caption");
    private static readonly CaptionTemplateId s_pluginTemplateId = new("beutl.tests.plugin-caption");
    // Sorts ahead of the plugin template, so adding it moves the plugin template's index.
    private static readonly CaptionTemplateId s_otherTemplateId = new("beutl.tests.another-plugin-caption");

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
                openTool.SelectedCaptionTemplate.Value?.Id,
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
    public async Task ShownTool_KeepsTheTemplateChoiceWhilePluginTemplatesComeAndGo()
    {
        await TestReset.ResetShellAsync();
        using AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(editViewModel: null);
        viewModel.SelectedSubtitlePageIndex.Value = 1;
        var view = new AiSubtitleView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 460, Height = 640 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ComboBox picker = view.FindControl<ComboBox>("CaptionTemplateComboBox")!;
            CaptionTemplateDescriptor defaultText = viewModel.CaptionTemplates
                .Single(template => template.Id == CaptionTemplateIds.DefaultText);
            Assert.That(picker.IsEffectivelyVisible, Is.True);
            AssertChoice("when the tool is shown", defaultText);
            var selections = new List<CaptionTemplateDescriptor?>();
            using IDisposable recording = viewModel.SelectedCaptionTemplate
                .Skip(1)
                .Subscribe(selections.Add);
            int previewRefreshes = 0;
            viewModel.BeforeTemplatePreviewAdmission = () => previewRefreshes++;

            Swap(
                "after a plugin template was added",
                () => TestShell.Extensions.AddExtensions(
                    TestPackageId,
                    CreateTemplateExtensions(new TestCaptionElementFactory())),
                defaultText,
                keepsChoice: true);

            CaptionTemplateDescriptor pluginTemplate = viewModel.CaptionTemplates
                .Single(template => template.Id == s_pluginTemplateId);
            picker.SelectedItem = pluginTemplate;
            HeadlessTestHelpers.Render();
            AssertChoice("after the plugin template was picked", pluginTemplate);

            Swap(
                "after another plugin template was added",
                () => TestShell.Extensions.AddExtensions(
                    OtherTestPackageId,
                    CreateTemplateExtensions(
                        new TestCaptionElementFactory(),
                        s_otherTemplateId,
                        "Another plugin caption template")),
                pluginTemplate,
                keepsChoice: true);
            Swap(
                "after the picked plugin template was removed",
                () => TestShell.Extensions.RemoveExtensions(TestPackageId),
                defaultText,
                keepsChoice: false);

            void Swap(string step, Action swap, CaptionTemplateDescriptor expected, bool keepsChoice)
            {
                selections.Clear();
                previewRefreshes = 0;
                swap();
                HeadlessTestHelpers.Render();
                AssertChoice(step, expected);
                if (keepsChoice)
                {
                    // Dropping a template that stays and choosing it again would restart its
                    // preview and briefly disable Add to scene.
                    Assert.Multiple(() =>
                    {
                        Assert.That(selections, Is.Empty, step);
                        Assert.That(previewRefreshes, Is.Zero, step);
                    });
                }
            }

            void AssertChoice(string step, CaptionTemplateDescriptor expected)
            {
                CaptionTemplateDescriptor[] templates = [.. viewModel.CaptionTemplates];
                Assert.Multiple(() =>
                {
                    Assert.That(viewModel.SelectedCaptionTemplate.Value, Is.EqualTo(expected), step);
                    Assert.That(picker.SelectedItem, Is.EqualTo(expected), step);
                    Assert.That(picker.SelectedIndex, Is.EqualTo(Array.IndexOf(templates, expected)), step);
                    Assert.That(picker.SelectionBoxItem, Is.EqualTo(expected), step);
                });
            }
        }
        finally
        {
            _ = TestShell.Extensions.RemoveExtensions(TestPackageId);
            _ = TestShell.Extensions.RemoveExtensions(OtherTestPackageId);
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // Packages can be loaded and unloaded on a worker thread. The picker's list must still change only
    // on the UI thread, and a swap that reaches the UI thread late must not undo a later one.
    [AvaloniaTest]
    public async Task ShownTool_TakesTemplateSwapsFromWorkerThreadsInOrder()
    {
        await TestReset.ResetShellAsync();
        using AiSubtitleDialogViewModel viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(editViewModel: null);
        viewModel.SelectedSubtitlePageIndex.Value = 1;
        var view = new AiSubtitleView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 460, Height = 640 };
        int changesOffUiThread = 0;
        void OnChoicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                Interlocked.Increment(ref changesOffUiThread);
        }

        viewModel.CaptionTemplates.CollectionChanged += OnChoicesChanged;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ComboBox picker = view.FindControl<ComboBox>("CaptionTemplateComboBox")!;
            CaptionTemplateDescriptor defaultText = viewModel.CaptionTemplates
                .Single(template => template.Id == CaptionTemplateIds.DefaultText);

            await Task.Run(() => TestShell.Extensions.AddExtensions(
                TestPackageId,
                CreateTemplateExtensions(new TestCaptionElementFactory())));
            HeadlessTestHelpers.Render();
            AssertChoice("after a worker thread added a plugin template", defaultText);
            picker.SelectedItem = viewModel.CaptionTemplates
                .Single(template => template.Id == s_pluginTemplateId);
            HeadlessTestHelpers.Render();

            // The UI thread stays busy while a worker removes the plugin template, then adds another
            // template itself before it gets to the removal.
            Task removal = Task.Run(() => TestShell.Extensions.RemoveExtensions(TestPackageId));
            Assert.That(removal.Wait(TimeSpan.FromSeconds(10)), Is.True);
            TestShell.Extensions.AddExtensions(
                OtherTestPackageId,
                CreateTemplateExtensions(
                    new TestCaptionElementFactory(),
                    s_otherTemplateId,
                    "Another plugin caption template"));
            HeadlessTestHelpers.Render();
            AssertChoice("after both swaps reached the UI thread", defaultText);
            Assert.That(
                viewModel.CaptionTemplates.Select(template => template.Id),
                Does.Contain(s_otherTemplateId).And.Not.Contain(s_pluginTemplateId));

            void AssertChoice(string step, CaptionTemplateDescriptor expected)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(picker.ItemCount, Is.EqualTo(viewModel.CaptionTemplates.Count), step);
                    Assert.That(viewModel.SelectedCaptionTemplate.Value, Is.EqualTo(expected), step);
                    Assert.That(picker.SelectedItem, Is.EqualTo(expected), step);
                    Assert.That(changesOffUiThread, Is.Zero, step);
                });
            }
        }
        finally
        {
            viewModel.CaptionTemplates.CollectionChanged -= OnChoicesChanged;
            _ = TestShell.Extensions.RemoveExtensions(TestPackageId);
            _ = TestShell.Extensions.RemoveExtensions(OtherTestPackageId);
            window.Close();
            HeadlessTestHelpers.Settle();
        }
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

    // A template contribution that changes while the subtitle tool is shown changes the tool's
    // template list. The change must reach subscribers added after the template picker as one they
    // can apply: DynamicData, for one, cannot apply a Replace whose item counts differ.
    [AvaloniaTest]
    public async Task ShownTool_KeepsLaterTemplateSubscribersInSync()
    {
        await TestReset.ResetShellAsync();
        using var viewModel =
            TestShell.MainViewModel.CreateAiSubtitleToolViewModel(editViewModel: null);
        ICoreReadOnlyList<CaptionTemplateDescriptor> templates = viewModel.CaptionTemplates;
        // The edit page hosts the template picker.
        viewModel.SelectedSubtitlePageIndex.Value = 1;
        var view = new AiSubtitleView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 460, Height = 900 };
        IDisposable? subscription = null;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            subscription = templates
                .ToObservableChangeSet<ICoreReadOnlyList<CaptionTemplateDescriptor>, CaptionTemplateDescriptor>()
                .Bind(out ReadOnlyObservableCollection<CaptionTemplateDescriptor> mirror)
                .Subscribe();

            TestShell.Extensions.AddExtensions(
                TestPackageId,
                CreateTemplateExtensions(new TestCaptionElementFactory()));
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(
                    templates.Select(template => template.Name),
                    Does.Contain("Plugin caption template"));
                Assert.That(mirror, Is.EqualTo(templates));
            });

            TestShell.Extensions.RemoveExtensions(TestPackageId);
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(
                    templates.Select(template => template.Name),
                    Does.Not.Contain("Plugin caption template"));
                Assert.That(mirror, Is.EqualTo(templates));
            });
        }
        finally
        {
            subscription?.Dispose();
            TestShell.Extensions.RemoveExtensions(TestPackageId);
            window.Close();
            HeadlessTestHelpers.Settle();
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
        => CreateTemplateExtensions(factory, s_pluginTemplateId, "Plugin caption template");

    private static Extension[] CreateTemplateExtensions(
        ICaptionElementFactory factory,
        CaptionTemplateId id,
        string name)
    {
        return
        [
            new TestCaptionTemplateDescriptorExtension([
                new CaptionTemplateDescriptorRegistration(new CaptionTemplateDescriptor(
                    id,
                    new CaptionTemplateProviderId("beutl.tests"),
                    name)),
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
