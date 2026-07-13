using System.Reactive.Linq;

using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;

using Beutl.Pages.ExtensionsPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels.ExtensionsPages;

using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class LibraryPageOperationStatusTests
{
    [AvaloniaTest]
    public void BusyCard_ShowsOperationStatusWhileActionButtonsAreHidden()
    {
        using var viewModel = new StubUserPackageViewModel();
        var page = new LibraryPage();
        var template = (IDataTemplate)page.Resources["UserPackageItemTemplate"]!;
        Control content = new ContentPresenter
        {
            Content = viewModel,
            ContentTemplate = template,
        };
        var window = new Window { Content = content, Width = 800, Height = 300 };

        try
        {
            viewModel.StatusText.Value = "Updating";
            viewModel.IsBusy.Value = true;
            window.Show();
            HeadlessTestHelpers.Render();

            List<Button> buttons = content.GetVisualDescendants().OfType<Button>().ToList();
            Assert.That(
                buttons.Any(x => x.Command is null && Equals(x.Content, "Updating")),
                Is.True,
                string.Join(Environment.NewLine, buttons.Select(x => $"Name={x.Name}; Content={x.Content}; Command={x.Command}; Classes={x.Classes}")));
            Button statusButton = buttons.Single(x => x.Command is null && Equals(x.Content, "Updating"));

            Assert.That(statusButton.Classes, Does.Not.Contain("transparent"));
            Assert.That(statusButton.Classes, Does.Contain("shimmer"));
            Assert.That(statusButton.Content, Is.EqualTo("Updating"));
            Assert.That(statusButton.IsHitTestVisible, Is.False);

            Assert.That(buttons.Single(x => x.Command == viewModel.Install).Classes, Does.Contain("transparent"));
            Assert.That(buttons.Single(x => x.Command == viewModel.Update).Classes, Does.Contain("transparent"));
            Assert.That(buttons.Single(x => x.Command == viewModel.Uninstall).Classes, Does.Contain("transparent"));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private sealed class StubUserPackageViewModel : IUserPackageViewModel
    {
        public StubUserPackageViewModel()
        {
            DisplayName = Observable.Return<string?>("Package").ToReadOnlyReactivePropertySlim();
            LogoUrl = Observable.Return<string?>(null).ToReadOnlyReactivePropertySlim();
            IsInstallButtonVisible = Observable.Return(false).ToReadOnlyReactivePropertySlim();
            IsUninstallButtonVisible = Observable.Return(false).ToReadOnlyReactivePropertySlim();
            IsUpdateButtonVisible = Observable.Return(false).ToReadOnlyReactivePropertySlim();
            CanCancel = Observable.Return(false).ToReadOnlyReactivePropertySlim();
        }

        public string Name => "Package";

        public IReadOnlyReactiveProperty<string?> DisplayName { get; }

        public IReadOnlyReactiveProperty<string?> LogoUrl { get; }

        public string Publisher => "Publisher";

        public bool IsRemote => false;

        public ReadOnlyReactivePropertySlim<bool> IsInstallButtonVisible { get; }

        public ReadOnlyReactivePropertySlim<bool> IsUninstallButtonVisible { get; }

        public IReadOnlyReactiveProperty<bool> IsUpdateButtonVisible { get; }

        public ReadOnlyReactivePropertySlim<bool> CanCancel { get; }

        public AsyncReactiveCommand Install { get; } = new();

        public AsyncReactiveCommand Update { get; } = new();

        public AsyncReactiveCommand Uninstall { get; } = new();

        public AsyncReactiveCommand Cancel { get; } = new();

        public ReactivePropertySlim<bool> IsBusy { get; } = new();

        public ReactivePropertySlim<string?> StatusText { get; } = new();

        public void Dispose()
        {
            DisplayName.Dispose();
            LogoUrl.Dispose();
            IsInstallButtonVisible.Dispose();
            IsUninstallButtonVisible.Dispose();
            IsUpdateButtonVisible.Dispose();
            CanCancel.Dispose();
            Install.Dispose();
            Update.Dispose();
            Uninstall.Dispose();
            Cancel.Dispose();
            IsBusy.Dispose();
            StatusText.Dispose();
        }
    }
}
