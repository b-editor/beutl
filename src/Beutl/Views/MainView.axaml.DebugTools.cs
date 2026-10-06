using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Services;
using Beutl.Services.WindowCapture;
using Beutl.Utilities;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Views;

public sealed partial class MainView
{
    [Conditional("DEBUG")]
    private void GC_Collect_Click(object? sender, RoutedEventArgs e) => RunGcCollect();

    internal void RunGcCollect()
    {
        DateTime dateTime = DateTime.UtcNow;
        long totalBytes = GC.GetTotalMemory(false);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        TimeSpan elapsed = DateTime.UtcNow - dateTime;

        long deltaBytes = GC.GetTotalMemory(false) - totalBytes;
        string str = StringFormats.ToHumanReadableSize(Math.Abs(deltaBytes));
        str = (deltaBytes >= 0 ? "+" : "-") + str;

        NotificationService.ShowInformation(
            Strings.Result,
            $"{Strings.ElapsedTime}: {elapsed.TotalMilliseconds}ms\n{Strings.Difference}: {str}");
    }

    [Conditional("DEBUG")]
    private void MonitorKeyModifier_Click(object? sender, RoutedEventArgs e) => OpenKeyModifierMonitor();

    internal void OpenKeyModifierMonitor()
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            new KeyModifierMonitor().Show(owner);
        }
    }

    [Conditional("DEBUG")]
    private void ThrowUnhandledException_Click(object? sender, RoutedEventArgs e) => ThrowDebugException();

    internal void ThrowDebugException()
    {
        throw new Exception("An unhandled exception occurred.");
    }

    [Conditional("DEBUG")]
    private async void StartWindowCapture_Click(object? sender, RoutedEventArgs e) => await StartWindowCaptureAsync();

    internal async Task StartWindowCaptureAsync()
    {
        if (_captureSession is not null)
        {
            NotificationService.ShowWarning(
                Strings.WindowCapture,
                MessageStrings.WindowCapture_AlreadyRunning);
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window window)
            return;

        // ffmpeg -version probes run synchronously inside Find(); push the work off
        // the UI thread so a slow probe doesn't make the menu click appear to hang.
        string? ffmpegPath = await Task.Run(FFmpegBinaryLocator.Find);
        if (ffmpegPath is null)
        {
            NotificationService.ShowError(
                Strings.WindowCapture,
                MessageStrings.WindowCapture_FfmpegNotFound);
            return;
        }

        var dialogVm = new WindowCaptureDialogViewModel();
        var dialog = new WindowCaptureDialog { DataContext = dialogVm };
        FAContentDialogResult result = await dialog.ShowAsync();
        if (result != FAContentDialogResult.Primary || !dialogVm.CanStart.Value)
            return;

        WindowCaptureSession? session = null;
        try
        {
            session = new WindowCaptureSession(
                window,
                dialogVm.Scale.Value,
                dialogVm.FrameRate.Value,
                dialogVm.OutputPath.Value!,
                ffmpegPath);
            session.Start();
            _captureSession = session;

            NotificationService.ShowInformation(
                Strings.WindowCapture,
                string.Format(MessageStrings.WindowCapture_RecordingStarted, session.Width, session.Height, session.FrameRate));
        }
        catch (Exception ex)
        {
            // The throw can occur after _captureSession was published, which would wedge every later Start.
            _captureSession = null;
            _logger.LogError(ex, "Failed to start window capture.");
            if (session is not null)
            {
                try { await session.DisposeAsync(); }
                catch (Exception disposeEx) { _logger.LogWarning(disposeEx, "Failed to dispose capture session after start failure."); }
            }
            NotificationService.ShowError(Strings.WindowCapture, ex.Message);
        }
    }

    [Conditional("DEBUG")]
    private async void StopWindowCapture_Click(object? sender, RoutedEventArgs e) => await StopWindowCaptureAsync();

    internal async Task StopWindowCaptureAsync()
    {
        // Coalesce a re-entrant Stop click so it can't emit a duplicate "Saved" toast.
        await _captureStop.TryRunAsync(async () =>
        {
            WindowCaptureSession? session = _captureSession;
            if (session is null)
            {
                NotificationService.ShowWarning(Strings.WindowCapture, MessageStrings.WindowCapture_NoActiveSession);
                return;
            }

            try
            {
                await session.StopAsync();
                _captureSession = null;
                NotificationService.ShowSuccess(
                    Strings.WindowCapture,
                    string.Format(MessageStrings.WindowCapture_Saved, session.OutputPath, session.CapturedFrameCount, session.DroppedFrameCount));
            }
            catch (Exception ex)
            {
                _captureSession = null;
                _logger.LogError(ex, "Failed to stop window capture.");
                NotificationService.ShowError(Strings.WindowCapture, ex.Message);
            }
        });
    }

    internal bool HasActiveCapture => _captureSession is not null;

    internal async Task EnsureCaptureStoppedAsync()
    {
        // Join an in-flight user stop so close waits for it; otherwise stop here.
        await _captureStop.RunOrJoinAsync(async () =>
        {
            WindowCaptureSession? session = _captureSession;
            if (session is null) return;
            try { await session.StopAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to stop capture during shutdown."); }
            finally { _captureSession = null; }
        });
    }
}
