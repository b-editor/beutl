using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class OutputProjectRateTests
{
    [AvaloniaTest]
    public async Task Output_view_shows_project_rates()
    {
        EditViewModel editor = await OpenEditor();
        const int packageId = 2858;
        var encoder = new TestEncoder();
        editor.ExtensionProvider.AddExtensions(packageId, [encoder]);
        using var output = CreateOutput(editor, encoder);
        var view = new OutputView { DataContext = output };
        var window = new Window { Content = view, Width = 650, Height = 720 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(view.GetVisualDescendants().OfType<RationalEditor>().Single().Value,
                    Is.EqualTo(new Rational(60, 1)));
                Assert.That(view.GetVisualDescendants().OfType<NumberEditor<int>>().Select(control => control.Value),
                    Does.Contain(48000));
            });

            if (Environment.GetEnvironmentVariable("BEUTL_OUTPUT_RATES_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                frame!.Save(Path.Combine(directory, "project-rates.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            editor.ExtensionProvider.RemoveExtensions(packageId);
        }
    }

    [AvaloniaTest]
    public async Task New_output_and_preview_use_project_rates()
    {
        EditViewModel editor = await OpenEditor();
        using var output = CreateOutput(editor, new TestEncoder());

        Assert.Multiple(() =>
        {
            Assert.That(output.Controller.Value!.VideoSettings.FrameRate, Is.EqualTo(new Rational(60, 1)));
            Assert.That(output.Controller.Value.AudioSettings.SampleRate, Is.EqualTo(48000));
            Assert.That(editor.Composer.Value.SampleRate, Is.EqualTo(48000));
        });
    }

    [AvaloniaTest]
    public async Task Switching_encoders_initializes_project_rates()
    {
        EditViewModel editor = await OpenEditor();
        using var output = CreateOutput(editor, new TestEncoder());
        SetCustomRates(output);

        output.SelectedEncoder.Value = new OtherTestEncoder();

        Assert.Multiple(() =>
        {
            Assert.That(output.Controller.Value!.VideoSettings.FrameRate, Is.EqualTo(new Rational(60, 1)));
            Assert.That(output.Controller.Value.AudioSettings.SampleRate, Is.EqualTo(48000));
        });
    }

    [AvaloniaTest]
    public async Task Changing_destination_preserves_custom_rates()
    {
        EditViewModel editor = await OpenEditor();
        using var output = CreateOutput(editor, new TestEncoder());
        SetCustomRates(output);

        output.DestinationFile.Value = Path.Combine(BeutlHomeIsolation.CurrentHome!, "renamed.mp4");

        AssertCustomRates(output);
    }

    [AvaloniaTest]
    public async Task Restoring_profile_preserves_saved_rates()
    {
        EditViewModel editor = await OpenEditor();
        const int packageId = 2858;
        var encoder = new TestEncoder();
        editor.ExtensionProvider.AddExtensions(packageId, [encoder]);
        try
        {
            using var original = CreateOutput(editor, encoder);
            SetCustomRates(original);
            var json = new JsonObject();
            original.WriteToJson(json);

            using var restored = new OutputViewModel(editor);
            restored.ReadFromJson(json);

            AssertCustomRates(restored);
        }
        finally
        {
            editor.ExtensionProvider.RemoveExtensions(packageId);
        }
    }

    private static async Task<EditViewModel> OpenEditor()
    {
        await TestReset.ResetShellAsync();
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(320, 180, 60, 48000, "rates", directory))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static OutputViewModel CreateOutput(EditViewModel editor, TestEncoder encoder)
    {
        var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(BeutlHomeIsolation.CurrentHome!, "rates.mp4");
        output.SelectedEncoder.Value = encoder;
        return output;
    }

    private static void SetCustomRates(OutputViewModel output)
    {
        output.Controller.Value!.VideoSettings.FrameRate = new Rational(24000, 1001);
        output.Controller.Value.AudioSettings.SampleRate = 96000;
    }

    private static void AssertCustomRates(OutputViewModel output)
    {
        Assert.That(output.Controller.Value, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(output.Controller.Value!.VideoSettings.FrameRate, Is.EqualTo(new Rational(24000, 1001)));
            Assert.That(output.Controller.Value.AudioSettings.SampleRate, Is.EqualTo(96000));
        });
    }

    private class TestEncoder : ControllableEncodingExtension
    {
        public override IEnumerable<string> SupportExtensions() => [".mp4"];
        public override EncodingController CreateController(string file) => new TestController(file);
    }

    private sealed class OtherTestEncoder : TestEncoder;

    private sealed class TestController(string file) : EncodingController(file)
    {
        public override VideoEncoderSettings VideoSettings { get; } = new();
        public override AudioEncoderSettings AudioSettings { get; } = new();

        public override ValueTask Encode(IFrameProvider frames, ISampleProvider samples, CancellationToken token)
            => throw new NotSupportedException();
    }
}
