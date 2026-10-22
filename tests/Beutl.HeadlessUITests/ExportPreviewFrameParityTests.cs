using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia.Headless.NUnit;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

// Export and the preview draw the same element on every frame, also when the times were read back from a file and fall
// up to a tick short of their frames (#2885).
[TestFixture]
public sealed class ExportPreviewFrameParityTests
{
    private static readonly string[] s_rates = ["30", "30000/1001"];
    private static readonly Color[] s_colors = [Colors.Red, Colors.Lime, Colors.Blue];

    [AvaloniaTest]
    public async Task Export_and_preview_draw_the_same_element_on_each_frame([ValueSource(nameof(s_rates))] string rateText)
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("frame-parity", 16, 16);
        Scene scene = editor.Scene;
        scene.FindHierarchicalParent<Project>()!.Variables[ProjectVariableKeys.FrameRate] = rateText;
        Rational rate = scene.FindHierarchicalParent<Project>().GetFrameRate();
        scene.Duration = ThroughFile(new Element { Length = MediaTime.FromFrames(9, rate) }).Length;
        var starts = new List<MediaTime>();
        for (int i = 0; i < s_colors.Length; i++)
        {
            Element saved = ThroughFile(new Element
            {
                Start = MediaTime.FromFrames(3 * i, rate),
                Length = MediaTime.FromFrames(3, rate),
            });
            Color color = s_colors[i];
            await TestShell.AddElementAsync(editor, () => new RectShape
            {
                Width = { Value = 16 },
                Height = { Value = 16 },
                Fill = { Value = new SolidColorBrush(color) },
            }, saved.Start, saved.Length);
            starts.Add(saved.Start);
        }

        var exported = new List<int>();
        Scene snapshot = SceneSnapshot.Create(scene);
        using (SceneRenderer renderer = ExportRendererFactory.Create(snapshot))
        using (var progress = new Subject<MediaTime>())
        using (var provider = new FrameProviderImpl(snapshot, rate, renderer, progress))
        {
            while (await provider.ReadNextFrame() is { } bitmap)
            {
                using (bitmap)
                    exported.Add(Center(bitmap));
            }
        }

        var previewed = new List<int>();
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        for (int frame = 0; frame < exported.Count; frame++)
        {
            // A frame where an element begins is reached on the element's start, as snapping to it does.
            clock.Seek(frame % 3 == 0 ? starts[frame / 3] : MediaTime.FromFrames(frame, rate));
            HeadlessTestHelpers.Settle();
            editor.FrameCacheManager.Value.Clear();
            var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (editor.Player.AfterRendered.Take(1).Subscribe(_ => rendered.TrySetResult()))
            {
                editor.Player.QueuePreviewRender();
                await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }

            HeadlessTestHelpers.Settle();
            previewed.Add(Center(editor.Player.PreviewImage.Value!.Value));
        }

        Assert.Multiple(() =>
        {
            Assert.That(exported, Is.EqualTo(Enumerable.Range(0, 9).Select(frame => frame / 3)));
            Assert.That(previewed, Is.EqualTo(exported));
        });
    }

    private static Element ThroughFile(Element element)
        => (Element)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(element), typeof(Element));

    // The element drawn at the centre, by its colour: 0 red, 1 green, 2 blue, or -1 for nothing.
    private static int Center(Bitmap bitmap)
    {
        using Bitmap bgra = bitmap.Convert(BitmapColorType.Bgra8888);
        Bgra8888 pixel = bgra.GetPixelSpan<Bgra8888>()[bgra.Width * (bgra.Height / 2) + bgra.Width / 2];
        if (pixel.A < 128) return -1;
        return pixel.R > 128 ? 0 : pixel.G > 128 ? 1 : pixel.B > 128 ? 2 : -1;
    }
}
