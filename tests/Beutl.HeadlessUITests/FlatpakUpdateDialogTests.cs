using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class FlatpakUpdateDialogTests
{
    [AvaloniaTest]
    public void FlatpakEnvironment_SelectsTheFlatpakUpdaterInThePublicConstructor()
    {
        string? previous = Environment.GetEnvironmentVariable("FLATPAK_ID");
        try
        {
            Environment.SetEnvironmentVariable("FLATPAK_ID", "net.beditor.Beutl");
            var vm = new UpdateDialogViewModel(FlatpakUpdateServiceTests.Update);
            Assert.That(vm.IsFlatpak, Is.True);
            Assert.That(vm.PrimaryButtonText.Value, Is.Empty);
        }
        finally { Environment.SetEnvironmentVariable("FLATPAK_ID", previous); }
    }

    [AvaloniaTest]
    public async Task FlatpakUpdateRunsOnce_AndDoesNotEnableTheStandaloneInstaller()
    {
        int calls = 0;
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new UpdateDialogViewModel(FlatpakUpdateServiceTests.Update, true, async (progress, token) =>
        {
            calls++;
            progress.Report(new(ExtensionsStrings.Installing));
            await finish.Task.WaitAsync(token);
        });
        Task operation = vm.StartFlatpakAsync();
        vm.Start();
        await vm.HandlePrimaryButtonClick();
        finish.SetResult();
        await operation;
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(vm.ProgressText.Value, Is.EqualTo(MessageStrings.FlatpakUpdateCompleted));
            Assert.That(vm.ProgressValue.Value, Is.EqualTo(1));
            Assert.That(vm.IsIndeterminate.Value, Is.False);
            Assert.That(vm.IsPrimaryButtonEnabled.Value, Is.False);
            Assert.That(vm.PrimaryButtonText.Value, Is.Empty);
            Assert.That(vm.CloseButtonText.Value, Is.EqualTo(Strings.Close));
        });
    }

    [AvaloniaTest]
    public async Task ClosingDialogCancelsAnActiveFlatpakOperation()
    {
        var vm = new UpdateDialogViewModel(FlatpakUpdateServiceTests.Update, true,
            (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        var window = new Window { Width = 640, Height = 480 };
        var dialog = new UpdateDialog { DataContext = vm };
        window.Show();
        Task closed = dialog.ShowAsync(window);
        Task operation = vm.StartFlatpakAsync();
        try
        {
            HeadlessTestHelpers.Render();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await closed.WaitAsync(TimeSpan.FromSeconds(5));
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(vm.ProgressText.Value, Is.EqualTo(MessageStrings.Canceled));
            Assert.That(vm.IsPrimaryButtonEnabled.Value, Is.False);
        }
        finally
        {
            vm.Cancel();
            dialog.Hide();
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task FailedFlatpakUpdate_LeavesTheAppOpenAndShowsTheFailure()
    {
        var window = new Window { Width = 640, Height = 480 };
        var vm = new UpdateDialogViewModel(FlatpakUpdateServiceTests.Update, true,
            (_, _) => Task.FromException(new IOException("Permission denied")));
        window.Show();
        try
        {
            await vm.StartFlatpakAsync();
            Assert.Multiple(() =>
            {
                Assert.That(window.IsVisible, Is.True);
                Assert.That(vm.ProgressText.Value, Does.Contain("Permission denied"));
                Assert.That(vm.ProgressText.Value, Does.Not.Contain(MessageStrings.FlatpakUpdateCompleted));
                Assert.That(vm.IsIndeterminate.Value, Is.False);
                Assert.That(vm.IsPrimaryButtonEnabled.Value, Is.False);
                Assert.That(vm.CloseButtonText.Value, Is.EqualTo(Strings.Close));
                Assert.That(vm.ShowReleasePage.Value, Is.True);
            });
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task CancelBeforeStarting_DoesNotStartTheUpdate()
    {
        bool started = false;
        var vm = new UpdateDialogViewModel(FlatpakUpdateServiceTests.Update, true, (_, _) =>
        {
            started = true;
            return Task.CompletedTask;
        });
        vm.Cancel();
        await vm.StartFlatpakAsync();
        Assert.That(started, Is.False);
        Assert.That(vm.ProgressText.Value, Is.EqualTo(MessageStrings.Canceled));
    }

    [AvaloniaTest]
    public void StandaloneUpdate_KeepsItsExistingButtons()
    {
        var vm = new UpdateDialogViewModel(FlatpakUpdateServiceTests.Update, false);
        Assert.That(vm.PrimaryButtonText.Value, Is.EqualTo(Strings.Next));
        Assert.That(vm.CloseButtonText.Value, Is.EqualTo(Strings.Cancel));
    }

    [AvaloniaTest]
    [TestCase(400, false, "ja-JP", "download")]
    [TestCase(400, true, "en-US", "download")]
    [TestCase(800, true, "ja-JP", "install")]
    [TestCase(800, false, "en-US", "install")]
    [TestCase(400, true, "ja-JP", "complete")]
    [TestCase(800, false, "en-US", "complete")]
    [TestCase(400, false, "ja-JP", "failure")]
    [TestCase(800, true, "en-US", "failure")]
    [TestCase(400, false, "ja-JP", "invalid-bundle")]
    [TestCase(400, true, "en-US", "too-large")]
    public async Task DialogShowsProgressAndResultAtDifferentSizes(int width, bool light, string language, string state)
    {
        CultureInfo oldCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new UpdateDialogViewModel(FlatpakUpdateServiceTests.Update, true, async (progress, token) =>
        {
            progress.Report(state == "download"
                ? new(MessageStrings.Downloading, 0.42) : new(ExtensionsStrings.Installing));
            await finish.Task.WaitAsync(token);
            if (state == "failure") throw new IOException(MessageStrings.FlatpakUpdateUnavailable);
            if (state == "invalid-bundle") throw new InvalidDataException(MessageStrings.FlatpakInvalidBundle);
            if (state == "too-large") throw new InvalidDataException(MessageStrings.FlatpakUpdateTooLarge);
        });
        var window = new Window { Width = width, Height = 480, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        var dialog = new UpdateDialog { DataContext = vm };
        Task? closed = null;
        Task? operation = null;
        try
        {
            window.Show();
            closed = dialog.ShowAsync(window);
            operation = vm.StartFlatpakAsync();
            if (state is "complete" or "failure" or "invalid-bundle" or "too-large")
            {
                finish.SetResult();
                await operation;
            }
            HeadlessTestHelpers.Render(3);
            var text = dialog.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == vm.ProgressText.Value);
            Assert.Multiple(() =>
            {
                Assert.That(text.Bounds.Width, Is.LessThan(width));
                Assert.That(text.Bounds.Height, Is.GreaterThan(0));
                Assert.That(dialog.PrimaryButtonText, Is.Empty);
                Assert.That(dialog.GetVisualDescendants().OfType<HyperlinkButton>().Single().IsEffectivelyVisible,
                    Is.EqualTo(state is "failure" or "invalid-bundle" or "too-large"));
                Assert.That(dialog.GetVisualDescendants().OfType<HyperlinkButton>().Single().NavigateUri?.AbsoluteUri,
                    Is.EqualTo(vm.Update.Url));
                Assert.That(window.IsVisible, Is.True);
            });
            if (Environment.GetEnvironmentVariable("BEUTL_FLATPAK_CAPTURE_DIR") is { Length: > 0 } directory)
            {
                // Only manual captures wait for the dialog's entrance animation.
                await Task.Delay(400);
                HeadlessTestHelpers.Render(3);
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                frame!.Save(Path.Combine(directory, $"{state}-{language}-{width}-{(light ? "light" : "dark")}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            vm.Cancel();
            finish.TrySetResult();
            if (operation != null) await operation;
            dialog.Hide();
            if (closed != null) await closed.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
            CultureInfo.CurrentUICulture = oldCulture;
        }
    }
}
