using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// A new scene's timeline tab shown in a window at scale 1 and offset 0.
internal sealed class TimelineTestHost : IDisposable
{
    private TimelineTestHost(
        Scene scene,
        TimelineTabViewModel model,
        Func<PointerWheelEventArgs, bool>? scrollDetector,
        double width,
        ThemeVariant? theme)
    {
        Scene = scene;
        Model = model;
        View = scrollDetector is null ? new TimelineTabView() : new TimelineTabView(scrollDetector);
        View.DataContext = model;
        Window = new Window { Content = View, Width = width, Height = 420 };
        if (theme is not null)
        {
            Window.RequestedThemeVariant = theme;
        }

        Window.Show();
        HeadlessTestHelpers.Render();
        Content = View.FindControl<ScrollViewer>("ContentScroll")!;
        Timeline = View.FindControl<Panel>("TimelinePanel")!;
        Ruler = View.FindControl<TimelineScale>("Scale")!;
        RulerBar = View.FindControl<Border>("RulerBar")!;
    }

    public static RawInputModifiers CommandModifier { get; } =
        KeyGestureHelper.GetCommandModifier() == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;

    public Scene Scene { get; }

    public TimelineTabViewModel Model { get; }

    public TimelineTabView View { get; }

    public Window Window { get; }

    public ScrollViewer Content { get; }

    public Panel Timeline { get; }

    public TimelineScale Ruler { get; }

    public Border RulerBar { get; }

    public static async Task<TimelineTestHost> CreateAsync(
        string name,
        TimeSpan duration,
        IReadOnlyList<ElementDescription>? clips = null,
        Func<PointerWheelEventArgs, bool>? scrollDetector = null,
        double width = 960,
        ThemeVariant? theme = null)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync(name);
        scene.Duration = duration;
        if (clips is not null)
        {
            var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
            await adder.AddAsync(clips, CancellationToken.None);
        }

        TimelineTabViewModel model = editor.FindToolTab<TimelineTabViewModel>()!;
        model.Options.Value = model.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
        return new TimelineTestHost(scene, model, scrollDetector, width, theme);
    }

    public void Dispose()
    {
        View.DataContext = null;
        Window.Close();
    }
}
