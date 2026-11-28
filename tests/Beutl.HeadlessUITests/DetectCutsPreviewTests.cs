using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Audio;
using Beutl.Editor.Components.CutDetectionTab.ViewModels;
using Beutl.Editor.Components.CutDetectionTab.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DetectCutsPreviewTests
{
    [AvaloniaTest]
    public async Task RendersCandidatesOnTheClip()
    {
        var (_, scene, editor) = await TestShell.OpenNewSceneAsync("detect-cuts-preview");
        string path = Path.Combine(TestShell.ProjectRoot(editor), "shots.testcuts");
        File.WriteAllBytes(path, []);
        var decoder = new Moq.Mock<IDecoderInfo>();
        decoder.Setup(d => d.IsSupported(path)).Returns(true);
        decoder.Setup(d => d.VideoExtensions()).Returns([".testcuts"]);
        decoder.Setup(d => d.AudioExtensions()).Returns([]);
        decoder.Setup(d => d.Open(path, Moq.It.IsAny<MediaOptions>())).Returns(() => new ShotReader());
        TestShell.Decoders.Register(decoder.Object);
        try
        {
            var source = new VideoSource();
            source.ReadFrom(new Uri(path));
            var element = new Element
            {
                Start = { Value = MediaTime.Zero },
                Length = { Value = MediaTime.FromSeconds(3) },
                Uri = new Uri(Path.Combine(TestShell.ProjectRoot(editor), "shots.belm")),
                Name = { Value = "Interview_Take1" },
            };
            element.Objects.Add(new SourceVideo { Source = { Value = source } });
            scene.AddChild(element);

            using var tab = new CutDetectionTabViewModel(editor);
            tab.Show(element);
            CutDetectionViewModel model = tab.Analysis.Value!;
            await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsBusy.Value);
            Assert.That(model.Candidates, Has.Count.EqualTo(2), model.Status.Value);

            var view = new CutDetectionTabView { DataContext = tab };
            var window = new Window { Content = view, Width = 900, Height = 430 };
            window.Show();
            try
            {
                HeadlessTestHelpers.Render(30);
                Assert.That(view.FindControl<Button>("SplitButton")!.IsEffectivelyEnabled, Is.True);
                Assert.That(view.FindControl<Button>("MarkersButton")!.IsEffectivelyEnabled, Is.True);
                Assert.That(view.FindControl<Button>("CancelButton")!.IsVisible, Is.False);
                HeadlessCapture.SaveIfRequested(view, string.Empty, "detect-cuts");
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            TestShell.Decoders.Unregister(decoder.Object);
        }
    }

    private sealed class ShotReader : MediaReader
    {
        private static readonly (byte R, byte G, byte B)[] s_colors = [(210, 58, 44), (20, 132, 86), (44, 78, 180)];
        private readonly VideoStreamInfo _videoInfo = new("cut-test", 90, new PixelSize(64, 36), new Rational(30, 1));
        private readonly AudioStreamInfo _audioInfo = new("cut-test", Rational.Zero, 44100, 2);

        public override VideoStreamInfo VideoInfo => _videoInfo;
        public override AudioStreamInfo AudioInfo => _audioInfo;
        public override bool HasVideo => true;
        public override bool HasAudio => false;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            if (frame < 0 || frame >= _videoInfo.NumFrames)
            {
                image = null;
                return false;
            }

            (byte r, byte g, byte b) = s_colors[frame / 30];
            var bitmap = new Bitmap(64, 36);
            bitmap.GetPixelSpan<Bgra8888>().Fill(new Bgra8888(r, g, b, 255));
            image = Ref<Bitmap>.Create(bitmap);
            return true;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            sound = null;
            return false;
        }
    }
}
