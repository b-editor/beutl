using System.Diagnostics;
using System.Reactive.Disposables;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

/// <summary>
/// Whatever the preview shows has to be the picture of the frame the playhead is on. The frame cache
/// keys entries on the frame number alone, so a mistake anywhere in the store / lookup / present path
/// shows up as a picture belonging to some other frame.
/// </summary>
[TestFixture]
public class PreviewShowsRequestedFrameTests
{
    private const int Rate = 30;
    private const int SegmentFrames = 5;
    private const int SegmentCount = 6;
    private const int TotalFrames = SegmentFrames * SegmentCount;

    // Each segment lights a distinct combination of channels, so a sampled pixel identifies its
    // segment regardless of whether the frame reached the screen as linear F16 or 8-bit sRGB.
    private static readonly (bool R, bool G, bool B)[] s_segmentChannels =
    [
        (true, false, false),
        (false, true, false),
        (false, false, true),
        (true, true, false),
        (true, false, true),
        (false, true, true),
    ];

    private static int ExpectedSegment(int frame) => frame / SegmentFrames;

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static IDisposable SuspendAutoSave()
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        bool prior = config.IsAutoSaveEnabled;
        config.IsAutoSaveEnabled = false;
        return Disposable.Create(() => config.IsAutoSaveEnabled = prior);
    }

    private static LinearGradientBrush NewGradient(Color color)
    {
        var brush = new LinearGradientBrush();
        brush.GradientStops.Add(new GradientStop { Offset = { CurrentValue = 0f }, Color = { CurrentValue = color } });
        brush.GradientStops.Add(new GradientStop
        {
            Offset = { CurrentValue = 1f },
            Color = { CurrentValue = new Color(255, (byte)(color.R / 3), (byte)(color.G / 3), (byte)(color.B / 3)) }
        });
        return brush;
    }

    private static async Task<EditViewModel> NewSegmentedEditor(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            160, 120, Rate, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var adder = (IElementAdder)editor.GetRequiredService<IElementAdder>();

        for (int i = 0; i < SegmentCount; i++)
        {
            (bool r, bool g, bool b) = s_segmentChannels[i];
            var color = new Color(255, r ? (byte)255 : (byte)0, g ? (byte)255 : (byte)0, b ? (byte)255 : (byte)0);
            adder.AddElement(new ElementDescription(
                Start: FrameTime(i * SegmentFrames),
                Length: FrameTime(SegmentFrames),
                Layer: 0,
                EngineObjectFactory: () => new RectShape
                {
                    Width = { CurrentValue = 160 },
                    Height = { CurrentValue = 120 },
                    // A gradient, not a flat fill: a flat colour survives the cache's 8-bit
                    // conversion untouched, so it cannot show whether the entry and the render
                    // agree.
                    Fill = { CurrentValue = NewGradient(color) }
                }));
            HeadlessTestHelpers.Settle();
        }

        return editor;
    }

    // Ticks, not seconds: a frame boundary computed in double can land just off the grid, which
    // makes two segments overlap or leave a gap.
    private static TimeSpan FrameTime(int frame) =>
        TimeSpan.FromTicks(frame * TimeSpan.TicksPerSecond / Rate);

    private static void SeekTo(EditViewModel editor, int frame)
    {
        editor.Player.CurrentFrame.Value = FrameTime(frame);
    }

    // Playback advances on a timer, so pumping the dispatcher is not enough to make frames appear.
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed >= timeout)
            {
                Assert.Fail($"Condition was not met within {timeout}.");
            }

            HeadlessTestHelpers.Settle();
            await Task.Delay(10);
        }
    }

    private static void DrainRenders()
    {
        for (int i = 0; i < 3; i++)
        {
            HeadlessTestHelpers.Settle();
            RenderThread.Dispatcher.Invoke(static () => { });
        }

        HeadlessTestHelpers.Settle();
    }

    private static byte[] CurrentPixels(EditViewModel editor)
    {
        Ref<Bitmap>? preview = editor.Player.PreviewImage.Value;
        Assert.That(preview, Is.Not.Null, "no preview frame was published");

        using Bitmap bgra = preview!.Value.Convert(BitmapColorType.Bgra8888);
        return bgra.GetPixelSpan().ToArray();
    }

    private static int CurrentSegment(EditViewModel editor)
    {
        Ref<Bitmap>? preview = editor.Player.PreviewImage.Value;
        Assert.That(preview, Is.Not.Null, "no preview frame was published");

        using Bitmap bgra = preview!.Value.Convert(BitmapColorType.Bgra8888);
        ReadOnlySpan<Bgra8888> pixels = bgra.GetPixelSpan<Bgra8888>();
        Bgra8888 p = pixels[bgra.Height / 2 * bgra.Width + bgra.Width / 2];

        (bool R, bool G, bool B) lit = (p.R > 64, p.G > 64, p.B > 64);
        return Array.IndexOf(s_segmentChannels, lit);
    }

    [AvaloniaTest]
    public async Task Scrubbing_over_cached_frames_always_shows_the_frame_under_the_playhead()
    {
        await TestReset.ResetShellAsync();
        GpuTestGate.EnsureAvailable();
        using IDisposable autoSave = SuspendAutoSave();

        try
        {
            EditViewModel editor = await NewSegmentedEditor("previewrequestedframe");
            editor.FrameCacheManager.Value.IsEnabled = true;
            editor.FrameCacheManager.Value.Clear();

            var mismatches = new List<string>();

            void Scrub(string pass, IEnumerable<int> frames)
            {
                foreach (int frame in frames)
                {
                    SeekTo(editor, frame);
                    DrainRenders();
                    int actual = CurrentSegment(editor);
                    if (actual != ExpectedSegment(frame))
                    {
                        mismatches.Add($"{pass}: frame {frame} showed segment {actual}, expected {ExpectedSegment(frame)}");
                    }
                }
            }

            Scrub("cold", Enumerable.Range(0, TotalFrames));
            Scrub("cached-forward", Enumerable.Range(0, TotalFrames));
            Scrub("cached-backward", Enumerable.Range(0, TotalFrames).Reverse());
            // Jumping across segment boundaries is what a real scrub does.
            Scrub("cached-jumpy", Enumerable.Range(0, TotalFrames).Select(i => i * 7 % TotalFrames));

            Assert.That(mismatches, Is.Empty, string.Join(Environment.NewLine, mismatches));
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    /// <summary>
    /// A drag moves the playhead faster than a frame can be rendered, so requests pile up and only
    /// the last one matters. Once the playhead settles, the preview has to end up on that frame and
    /// not on whichever superseded render happened to publish last.
    /// </summary>
    [AvaloniaTest]
    public async Task A_burst_of_seeks_settles_on_the_last_requested_frame()
    {
        await TestReset.ResetShellAsync();
        GpuTestGate.EnsureAvailable();
        using IDisposable autoSave = SuspendAutoSave();

        try
        {
            EditViewModel editor = await NewSegmentedEditor("previewseekburst");
            editor.FrameCacheManager.Value.IsEnabled = true;
            editor.FrameCacheManager.Value.Clear();

            // Warm every frame so the burst below runs entirely out of the cache.
            for (int frame = 0; frame < TotalFrames; frame++)
            {
                SeekTo(editor, frame);
                DrainRenders();
            }

            var mismatches = new List<string>();

            void Burst(string pass, int[] frames, bool pumpUiBetweenSeeks)
            {
                foreach (int frame in frames)
                {
                    SeekTo(editor, frame);
                    if (pumpUiBetweenSeeks)
                    {
                        HeadlessTestHelpers.Settle();
                    }
                }

                DrainRenders();
                int settled = frames[^1];
                int actual = CurrentSegment(editor);
                if (actual != ExpectedSegment(settled))
                {
                    mismatches.Add(
                        $"{pass}: settled on frame {settled} but showed segment {actual}, expected {ExpectedSegment(settled)}");
                }
            }

            int[][] bursts =
            [
                [0, 6, 12, 18, 24, 29],
                [29, 22, 15, 8, 1],
                [3, 27, 5, 25, 7, 23],
                [10, 11, 12, 13, 14],
            ];

            foreach (int[] burst in bursts)
            {
                Burst("no-pump", burst, pumpUiBetweenSeeks: false);
                Burst("pump", burst, pumpUiBetweenSeeks: true);
            }

            Assert.That(mismatches, Is.Empty, string.Join(Environment.NewLine, mismatches));
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    /// <summary>
    /// A cache entry is a converted, possibly reduced copy of the render, so a frame that is served
    /// from the cache must be the same picture the preview showed when it was rendered. Otherwise
    /// scrubbing across the edge of a cached range alternates between two renditions of neighbouring
    /// frames.
    /// </summary>
    [AvaloniaTest]
    public async Task A_frame_looks_the_same_freshly_rendered_and_replayed_from_the_cache()
    {
        await TestReset.ResetShellAsync();
        GpuTestGate.EnsureAvailable();
        using IDisposable autoSave = SuspendAutoSave();

        try
        {
            EditViewModel editor = await NewSegmentedEditor("previewcacheparity");
            editor.FrameCacheManager.Value.IsEnabled = true;
            editor.FrameCacheManager.Value.Clear();

            var mismatches = new List<int>();
            for (int frame = 0; frame < TotalFrames; frame += 3)
            {
                // Always arrive from somewhere else: seeking to the frame the playhead already sits
                // on renders nothing and would sample a stale preview.
                int elsewhere = frame == 0 ? TotalFrames - 1 : 0;

                SeekTo(editor, elsewhere);
                DrainRenders();
                SeekTo(editor, frame);
                DrainRenders();
                byte[] rendered = CurrentPixels(editor);

                SeekTo(editor, elsewhere);
                DrainRenders();
                SeekTo(editor, frame);
                DrainRenders();
                byte[] cached = CurrentPixels(editor);

                if (!rendered.AsSpan().SequenceEqual(cached))
                {
                    mismatches.Add(frame);
                }
            }

            Assert.That(mismatches, Is.Empty,
                $"frames served from the cache differ from the same frames when rendered: {string.Join(", ", mismatches)}");
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    /// <summary>
    /// Playback renders through <c>BufferedPlayer</c>, a different producer from the scrub path, and
    /// it stores every frame it renders. What it hands to the preview has to be that stored entry —
    /// the snapshot it was made from is a different rendition (RgbaF16/linear at full size), so
    /// queueing it would make a frame change appearance the moment it came back from the cache.
    /// </summary>
    [AvaloniaTest]
    public async Task Playing_publishes_the_rendition_it_stores_in_the_cache()
    {
        await TestReset.ResetShellAsync();
        GpuTestGate.EnsureAvailable();
        using IDisposable autoSave = SuspendAutoSave();
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        bool cacheWasEnabled = config.IsFrameCacheEnabled;
        config.IsFrameCacheEnabled = true;
        try
        {
            EditViewModel editor = await NewSegmentedEditor(nameof(Playing_publishes_the_rendition_it_stores_in_the_cache));
            SeekTo(editor, 0);
            DrainRenders();

            var published = new List<BitmapColorType>();
            using (editor.Player.PreviewImage.Subscribe(frame =>
                   {
                       if (editor.Player.IsPlaying.Value && frame?.Value is { IsDisposed: false } bitmap)
                       {
                           published.Add(bitmap.ColorType);
                       }
                   }))
            {
                editor.Player.Play();
                await WaitUntilAsync(() => published.Count >= 3, TimeSpan.FromSeconds(10));
                await editor.Player.Pause();
            }

            DrainRenders();

            Assert.That(published, Is.Not.Empty, "playback published no frame");
            Assert.That(published, Is.All.EqualTo(BitmapColorType.Bgra8888),
                "playback published the raw snapshot instead of the entry it stored");
            Assert.That(editor.FrameCacheManager.Value.TryGet(0, out Ref<Bitmap>? stored), Is.True,
                "playback stored nothing for frame 0");
            stored!.Dispose();
        }
        finally
        {
            config.IsFrameCacheEnabled = cacheWasEnabled;
            await TestReset.ResetShellAsync();
        }
    }
}
