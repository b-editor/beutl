using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

// Property editors drop cached preview frames through IFrameCacheInvalidator, not through EditViewModel.
[TestFixture]
public sealed class FrameCacheInvalidatorTests
{
    [AvaloniaTest]
    public void A_property_editor_invalidates_the_range_of_its_element()
    {
        var shape = new RectShape();
        var element = new Element { Start = TimeSpan.FromSeconds(1), Length = TimeSpan.FromSeconds(2) };
        element.AddObject(shape);
        var invalidator = new RecordingInvalidator();
        using var editor = new NumberEditorViewModel<float>(
            new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Width, shape));
        editor.Accept(new Services(element, invalidator));

        editor.InvalidateFrameCache();

        Assert.That(invalidator.Ranges, Is.EqualTo(new[] { element.Range }));
    }

    [AvaloniaTest]
    public async Task The_editor_drops_the_cached_frames_of_the_range_only()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("frame-cache-invalidator");
        FrameCacheManager cache = editor.FrameCacheManager.Value;
        bool enabled = cache.IsEnabled;
        int rate = editor.Player.GetFrameRate();
        cache.IsEnabled = true;
        try
        {
            Add(cache, 5 * rate);
            Add(cache, 10 * rate);

            editor.GetRequiredService<IFrameCacheInvalidator>()
                .Invalidate(new TimeRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)));

            await HeadlessTestHelpers.WaitUntilAsync(() => !Contains(cache, 5 * rate), TimeSpan.FromMinutes(1));
            Assert.That(Contains(cache, 10 * rate), Is.True);
        }
        finally
        {
            cache.Clear();
            cache.IsEnabled = enabled;
        }
    }

    private static void Add(FrameCacheManager cache, int frame)
    {
        using Ref<Bitmap> bitmap = Ref<Bitmap>.Create(new Bitmap(16, 16));
        cache.Add(frame, bitmap);
    }

    private static bool Contains(FrameCacheManager cache, int frame)
    {
        if (!cache.TryGet(frame, out Ref<Bitmap>? cached))
        {
            return false;
        }

        cached.Dispose();
        return true;
    }

    private sealed class RecordingInvalidator : IFrameCacheInvalidator
    {
        public List<TimeRange> Ranges { get; } = [];

        public void Invalidate(TimeRange range) => Ranges.Add(range);
    }

    private sealed class Services(Element element, IFrameCacheInvalidator invalidator)
        : IPropertyEditorContextVisitor, IServiceProvider
    {
        public void Visit(IPropertyEditorContext context)
        {
        }

        public object? GetService(Type serviceType)
            => serviceType == typeof(Element) ? element
                : serviceType == typeof(IFrameCacheInvalidator) ? invalidator
                : null;
    }
}
