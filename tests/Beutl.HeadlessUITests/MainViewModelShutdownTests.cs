using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.ViewModels;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class MainViewModelShutdownTests
{
    [AvaloniaTest]
    public async Task ClosingRealShell_PreservesPackageQueueUntilExitThenDisposesClientsIdempotently()
    {
        await TestReset.ResetShellAsync();
        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        PackageChangesQueue? handedOffQueue = null;
        var viewModel = new MainViewModel(clients =>
            handedOffQueue = clients.GetResource<PackageChangesQueue>());
        var window = new MainWindow { DataContext = viewModel };
        // The minimal headless TestApp does not install the production MainView's app-only
        // chrome services. Keep the real MainWindow closing path while avoiding that unrelated
        // visual-tree startup callback.
        window.Content = null;
        viewModel.RegisterExitHandler(lifetime);

        try
        {
            window.Show();
            window.Close();

            Assert.DoesNotThrow(() =>
                viewModel._beutlClients.GetResource<PackageChangesQueue>());

            viewModel.CompleteShutdown();

            Assert.That(handedOffQueue, Is.Not.Null);
            Assert.Throws<ObjectDisposedException>(() =>
                viewModel._beutlClients.GetResource<PackageChangesQueue>());
            Assert.DoesNotThrow(viewModel.Dispose);
            Assert.DoesNotThrow(viewModel.CompleteShutdown);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
            }
            viewModel.Dispose();
            viewModel.CompleteShutdown();
        }
    }

}
