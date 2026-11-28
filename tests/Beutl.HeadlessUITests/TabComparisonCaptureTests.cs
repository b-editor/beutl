using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Audio;
using Beutl.Editor.Components.SilenceSplitTab.ViewModels;
using Beutl.Editor.Components.SilenceSplitTab.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// Renders the silence split tab at DetectCutsPreviewTests' size, so the two tools can be compared when
// BEUTL_HEADLESS_CAPTURE_DIR is set. Without it nothing is written.
[TestFixture]
public class TabComparisonCaptureTests
{
    [AvaloniaTest]
    public async Task CaptureSilenceSplitTab()
    {
        var (_, scene, editor) = await TestShell.OpenNewSceneAsync("silence-split-preview");
        var element = new Element
        {
            Length = { Value = MediaTime.FromSeconds(3) },
            Uri = new Uri(Path.Combine(TestShell.ProjectRoot(editor), "silence.belm")),
            Name = { Value = "Interview_Take1" },
        };
        element.Objects.Add(new SourceSound { Source = { Value = new SoundSource() } });
        scene.AddChild(element);

        using var tab = new SilenceSplitTabViewModel(editor);
        tab.Show(element);
        SilenceSplitViewModel model = tab.Analysis.Value!;
        await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsBusy.Value);
        model.SetWaveform(Enumerable.Range(0, 150).Select(i =>
            i is >= 50 and < 100
                ? new WaveformChunk(i, 0, 0)
                : new WaveformChunk(i, -0.4f, 0.4f)).ToArray());

        var view = new SilenceSplitTabView { DataContext = tab };
        var window = new Window { Content = view, Width = 900, Height = 430 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render(30);
            HeadlessCapture.SaveIfRequested(view, string.Empty, "silence-split-tab");
        }
        finally
        {
            window.Close();
        }
    }
}
