using System.Reactive.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;
using Beutl.Editor.Components.ColorScopesTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Source;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ColorScopesTabViewTests
{
    [AvaloniaTest]
    public void Only_the_attached_view_model_keeps_a_refresh_subscription()
    {
        using ColorScopesTabViewModel first = CreateViewModel();
        using ColorScopesTabViewModel second = CreateViewModel();
        var view = new ColorScopesTabView { DataContext = first };
        var window = new Window { Content = view };
        try
        {
            window.Show();
            Assert.That(RefreshSubscribers(first), Is.EqualTo(1));

            view.DataContext = second;
            Assert.That(RefreshSubscribers(first), Is.Zero, "The previous view model still reaches the view.");
            Assert.That(RefreshSubscribers(second), Is.EqualTo(1));

            view.DataContext = null;
            view.DataContext = second;
            Assert.That(RefreshSubscribers(second), Is.EqualTo(1), "Setting the same view model again doubles the refreshes.");

            window.Content = null;
            Assert.That(RefreshSubscribers(second), Is.Zero, "A detached view stays subscribed.");

            window.Content = view;
            Assert.That(RefreshSubscribers(second), Is.EqualTo(1), "A re-attached view stops refreshing.");
        }
        finally
        {
            window.Close();
        }
    }

    private static ColorScopesTabViewModel CreateViewModel()
    {
        var player = new Mock<IPreviewPlayer>();
        player.SetupGet(p => p.PreviewImage).Returns(new ReactivePropertySlim<Ref<Bitmap>?>());
        player.SetupGet(p => p.AfterRendered).Returns(Observable.Never<System.Reactive.Unit>());
        var editor = new Mock<IEditorContext>();
        editor.Setup(e => e.GetService(typeof(IPreviewPlayer))).Returns(player.Object);
        return new ColorScopesTabViewModel(editor.Object);
    }

    private static int RefreshSubscribers(ColorScopesTabViewModel viewModel)
    {
        FieldInfo field = typeof(ColorScopesTabViewModel).GetField(
            nameof(ColorScopesTabViewModel.RefreshRequested), BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (field.GetValue(viewModel) as Delegate)?.GetInvocationList().Length ?? 0;
    }
}
