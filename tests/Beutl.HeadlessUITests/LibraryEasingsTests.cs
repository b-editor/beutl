using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Animation.Easings;
using Beutl.Controls;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views;
using Beutl.Editor.Components.LibraryTab.Views.LibraryViews;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LibraryEasingsTests
{
    private static readonly Type[] s_builtInOrder =
    [
        typeof(BackEaseIn),
        typeof(BackEaseInOut),
        typeof(BackEaseOut),
        typeof(BounceEaseIn),
        typeof(BounceEaseInOut),
        typeof(BounceEaseOut),
        typeof(CircularEaseIn),
        typeof(CircularEaseInOut),
        typeof(CircularEaseOut),
        typeof(CubicEaseIn),
        typeof(CubicEaseInOut),
        typeof(CubicEaseOut),
        typeof(ElasticEaseIn),
        typeof(ElasticEaseInOut),
        typeof(ElasticEaseOut),
        typeof(ExponentialEaseIn),
        typeof(ExponentialEaseInOut),
        typeof(ExponentialEaseOut),
        typeof(QuadraticEaseIn),
        typeof(QuadraticEaseInOut),
        typeof(QuadraticEaseOut),
        typeof(QuarticEaseIn),
        typeof(QuarticEaseInOut),
        typeof(QuarticEaseOut),
        typeof(QuinticEaseIn),
        typeof(QuinticEaseInOut),
        typeof(QuinticEaseOut),
        typeof(SineEaseIn),
        typeof(SineEaseInOut),
        typeof(SineEaseOut),
        typeof(LinearEasing),
        typeof(HoldEasing),
    ];

    [AvaloniaTest]
    public async Task Easings_show_extension_registrations_until_their_types_unload()
    {
        using var library = new LibraryService();
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, library, TestShell.Configuration.EditorConfig)
        {
            SelectedTab = 1
        };
        var view = new LibraryTabView { DataContext = viewModel };
        using var host = new ControlHost(view, width: 980, height: 990);
        var easingsView = view.GetVisualDescendants().OfType<EasingsView>().Single();
        Assert.That(easingsView.FindControl<Border>("splineEasing"), Is.Not.Null);
        Assert.That(viewModel.LibraryItems, Is.Empty);
        AssertGraphs(s_builtInOrder);

        try
        {
            await Task.Run(() => library.RegisterGroup("Extension easings",
                group => group.AddEasing<WaveExtensionEasing>("Wave")));
            AssertGraphs([.. s_builtInOrder, typeof(WaveExtensionEasing)]);

            await Task.Run(() => library.AddMultiple("Steps", item => item.BindEasing<SteppedExtensionEasing>()));
            AssertGraphs([.. s_builtInOrder, typeof(SteppedExtensionEasing), typeof(WaveExtensionEasing)]);
            HeadlessCapture.SaveIfRequested(easingsView, nameof(LibraryEasingsTests), "extension-easings");
        }
        finally
        {
            await Task.Run(() => TypeUnloadNotifier.NotifyUnloading([typeof(SteppedExtensionEasing), typeof(WaveExtensionEasing)]));
        }

        AssertGraphs(s_builtInOrder);

        void AssertGraphs(Type[] expected)
        {
            HeadlessTestHelpers.Render(2);
            Assert.That(easingsView.GetVisualDescendants().OfType<EasingGraph>().Select(graph => graph.Easing?.GetType()),
                Is.EqualTo(expected));
        }
    }

    private sealed class SteppedExtensionEasing : Easing
    {
        public override float Ease(float progress) => MathF.Floor(progress * 4) / 4;
    }

    private sealed class WaveExtensionEasing : Easing
    {
        public override float Ease(float progress) => progress + 0.2f * MathF.Sin(progress * MathF.Tau);
    }
}
