using Avalonia;
using Avalonia.Controls;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.Views.Tools;

// The AI plan is managed on the website. Any control that can send the user
// there attaches this so the entitlements are re-read when the app is focused
// again; otherwise a change made in the browser — a customer portal
// cancellation in particular — stays invisible until the next manual reload.
internal static class AiPlanReturnRefresh
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(AiPlanReturnRefresh));

    public static IDisposable Attach(Control control, IAiPlanCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(coordinator);
        return new Subscription(control, coordinator);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Control _control;
        private readonly IAiPlanCoordinator _coordinator;
        private readonly CancellationTokenSource _cts = new();
        private WindowBase? _window;
        private bool _disposed;

        public Subscription(Control control, IAiPlanCoordinator coordinator)
        {
            _control = control;
            _coordinator = coordinator;
            _control.Loaded += OnLoaded;
            _control.Unloaded += OnUnloaded;
            if (_control.IsLoaded)
            {
                Subscribe();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _control.Loaded -= OnLoaded;
            _control.Unloaded -= OnUnloaded;
            Unsubscribe();
            _cts.Cancel();
            _cts.Dispose();
        }

        private void OnLoaded(object? sender, EventArgs e) => Subscribe();

        private void OnUnloaded(object? sender, EventArgs e) => Unsubscribe();

        private void Subscribe()
        {
            if (_disposed || _window is not null)
                return;

            if (TopLevel.GetTopLevel(_control) is WindowBase window)
            {
                _window = window;
                window.Activated += OnActivated;
            }
        }

        private void Unsubscribe()
        {
            if (_window is null)
                return;

            _window.Activated -= OnActivated;
            _window = null;
        }

        private async void OnActivated(object? sender, EventArgs e)
        {
            if (_disposed)
                return;

            try
            {
                await _coordinator.RefreshIfPendingAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                s_logger.LogError(ex, "Failed to reload AI entitlements after returning to the app.");
            }
        }
    }
}
