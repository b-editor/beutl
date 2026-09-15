using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Controls;
using Beutl.Extensibility;
using Beutl.Pages;
using Beutl.Pages.SettingsPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.SettingsPages;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class SettingsGroupTests
{
    [AvaloniaTest]
    public void Dividers_follow_visible_rows_after_hiding_removing_and_inserting_items()
    {
        var panel = new OptionsGroupPanel { SeparatorBrush = Brushes.Red, VerticalAlignment = VerticalAlignment.Top };
        Border[] rows = Enumerable.Range(0, 3).Select(_ => new Border { Height = 40, Background = Brushes.White }).ToArray();
        panel.Children.AddRange(rows);
        var window = new Window { Content = panel, Background = Brushes.White, Width = 320, Height = 160 };
        try
        {
            window.Show();
            AssertLines(40, 81);
            rows[1].IsVisible = false;
            AssertLines(40);
            panel.Children.Remove(rows[0]);
            AssertLines();
            panel.Children.Insert(0, rows[0]);
            AssertLines(40);
            rows[1].IsVisible = true;
            AssertLines(40, 81);
            rows[0].Height = 60;
            AssertLines(60, 101);
            panel.SeparatorBrush = Brushes.Transparent;
            AssertLines();
        }
        finally { window.Close(); }

        void AssertLines(params int[] expected)
        {
            HeadlessTestHelpers.Render();
            using WriteableBitmap frame = window.CaptureRenderedFrame()!;
            using ILockedFramebuffer buffer = frame.Lock();
            Assert.That(buffer.Format, Is.EqualTo(PixelFormat.Rgba8888).Or.EqualTo(PixelFormat.Bgra8888));
            Assert.That(buffer.Size.Width, Is.GreaterThan(160));
            List<int> redRows = [];
            int redOffset = buffer.Format == PixelFormat.Bgra8888 ? 2 : 0;
            for (int y = 0; y < buffer.Size.Height; y++)
            {
                IntPtr pixel = buffer.Address + y * buffer.RowBytes + 160 * 4;
                if (Marshal.ReadByte(pixel, redOffset) > 200 && Marshal.ReadByte(pixel, 1) < 50)
                    redRows.Add(y);
            }
            Assert.That(redRows, Is.EqualTo(expected), "Only the gaps between visible items should have a divider.");
        }
    }

    [AvaloniaTest]
    [TestCase(480, false)]
    [TestCase(800, true)]
    public async Task Settings_screens_keep_grouped_rows_usable_when_expanded(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        using var model = TestShell.MainViewModel.CreateSettingsDialog();
        var dialog = new SettingsDialog
        {
            DataContext = model,
            Width = width,
            Height = 760,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            model.GoToBrowserSettingsPage();
            dialog.Show();
            HeadlessTestHelpers.Render(3);
            var nav = dialog.FindControl<FANavigationView>("nav")!;
            nav.IsPaneOpen = false;
            var frame = dialog.FindControl<FAFrame>("frame")!;
            foreach ((Type type, object context) in new (Type, object)[]
            {
                (typeof(ExtensionsSettingsPage), model.ExtensionsPage),
                (typeof(KeyMapSettingsPage), model.KeyMap),
                (typeof(AiAgentSettingsPage), model.AiAgent),
                (typeof(InformationPage), model.Information)
            })
            {
                frame.Navigate(type, context, new FASuppressNavigationTransitionInfo());
                HeadlessTestHelpers.Render(3);
                var page = (Control)frame.Content!;
                OptionsDisplayItem[] expandable = page.GetVisualDescendants().OfType<OptionsDisplayItem>()
                    .Where(x => x.Expands && x.IsEffectivelyVisible).ToArray();
                Assert.That(expandable, Is.Not.Empty, type.Name);
                foreach (OptionsDisplayItem row in type == typeof(KeyMapSettingsPage) ? expandable.Take(1) : expandable)
                {
                    row.ContentTransition = null;
                    row.IsExpanded = true;
                }
                HeadlessTestHelpers.Render(3);
                foreach (OptionsDisplayItem row in page.GetVisualDescendants().OfType<OptionsDisplayItem>())
                    Assert.That(row.BorderThickness, Is.EqualTo(default(Thickness)), $"Individual frame remains: {row.Header}");
                Capture(dialog, $"{type.Name}-{width}-{light}");

                if (type == typeof(KeyMapSettingsPage))
                {
                    OptionsDisplayItem row = page.GetVisualDescendants().OfType<OptionsDisplayItem>().First(x => x.ActionButton is Button);
                    var button = (Button)row.ActionButton;
                    button.Focus(NavigationMethod.Tab);
                    HeadlessTestHelpers.Render();
                    Point point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), dialog)!.Value;
                    dialog.MouseDown(point, MouseButton.Left);
                    dialog.MouseUp(point, MouseButton.Left);
                    HeadlessTestHelpers.Render();
                    Assert.That(dialog.GetVisualDescendants().OfType<FAPickerFlyoutPresenter>(), Is.Not.Empty);
                    dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    dialog.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                }
                if (type == typeof(AiAgentSettingsPage))
                {
                    foreach (OptionsDisplayItem row in page.GetVisualDescendants().OfType<OptionsDisplayItem>()
                                 .Where(x => x.IsEffectivelyVisible && x.ActionButton is not null))
                    {
                        Point point = row.ActionButton.TranslatePoint(default, row)!.Value;
                        Assert.That(point.X + row.ActionButton.Bounds.Width, Is.LessThanOrEqualTo(row.Bounds.Width + 0.001), row.Header.ToString());
                    }
                    OptionsDisplayItem advanced = page.GetVisualDescendants().OfType<OptionsDisplayItem>()
                        .First(x => Equals(x.Header, Beutl.Language.SettingsStrings.AiAgents_Advanced));
                    advanced.BringIntoView();
                    HeadlessTestHelpers.Render();
                    Capture(dialog, $"agent-details-{width}-{light}");
                }
            }
        }
        finally { dialog.Close(); }
    }

    [AvaloniaTest]
    public async Task Generated_extension_settings_share_frames_without_reordering_fields()
    {
        await TestReset.ResetShellAsync();
        var extension = new SampleExtension();
        var provider = new ExtensionProvider();
        provider.AddExtensions(901, TestShell.MainViewModel.ExtensionProvider.GetExtensions<PropertyEditorExtension>());
        provider.AddExtensions(902, [extension]);
        var model = new AnExtensionSettingsPageViewModel(extension, provider);
        using var listModel = new ExtensionsSettingsPageViewModel(provider);
        var page = new AnExtensionSettingsPage { DataContext = model };
        var window = new Window { Content = page, Width = 480, Height = 650 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(model.Properties.OfType<PropertyEditorGroupContext>().Count(), Is.EqualTo(2));
            OptionsDisplayItem[] rows = page.GetVisualDescendants().OfType<OptionsDisplayItem>().ToArray();
            Assert.That(rows, Has.Length.EqualTo(3));
            Assert.That(rows.Select(x => x.Header), Is.EqualTo(new[] { "General", "FirstPath", "SecondPath" }));
            foreach (OptionsDisplayItem row in rows)
            {
                Border root = row.GetVisualDescendants().OfType<Border>().First(x => x.Name == "LayoutRoot");
                Assert.That(root.BorderThickness, Is.EqualTo(default(Thickness)), row.Header.ToString());
            }
            TextBox input = rows[0].GetVisualDescendants().OfType<TextBox>().First();
            input.Focus();
            input.Text = "changed";
            rows[1].GetVisualDescendants().OfType<TextBox>().First().Focus();
            HeadlessTestHelpers.Render();
            Assert.That(extension.Settings.General, Is.EqualTo("changed"));
            Capture(window, "extension-properties");

            var list = new ExtensionsSettingsPage { DataContext = listModel };
            window.Content = list;
            HeadlessTestHelpers.Render(3);
            OptionsDisplayItem expanded = list.GetVisualDescendants().OfType<OptionsDisplayItem>().Single(x => x.Expands);
            expanded.ContentTransition = null;
            expanded.IsExpanded = true;
            HeadlessTestHelpers.Render();
            OptionsDisplayItem generated = list.GetVisualDescendants().OfType<OptionsDisplayItem>().Single(x => Equals(x.Header, extension.DisplayName));
            Assert.That(generated.BorderThickness, Is.EqualTo(default(Thickness)));
            Capture(window, "extension-list");
        }
        finally
        {
            page.DataContext = null;
            window.Close();
            foreach (var context in model.Properties) context?.Dispose();
        }
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_SETTINGS_GROUP_CAPTURE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        image?.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }

    private sealed class SampleExtension : Extension
    {
        public override string DisplayName => "Sample settings";
        public override SampleSettings Settings { get; } = new();
    }

    public sealed class SampleSettings : ExtensionSettings
    {
        public static readonly CoreProperty<string> GeneralProperty = ConfigureProperty<string, SampleSettings>(nameof(General)).DefaultValue("initial").Register();
        public static readonly CoreProperty<string> FirstPathProperty = ConfigureProperty<string, SampleSettings>(nameof(FirstPath)).DefaultValue("first").Register();
        public static readonly CoreProperty<string> SecondPathProperty = ConfigureProperty<string, SampleSettings>(nameof(SecondPath)).DefaultValue("second").Register();
        public string General { get => GetValue(GeneralProperty); set => SetValue(GeneralProperty, value); }
        [Display(GroupName = "Paths")]
        public string FirstPath { get => GetValue(FirstPathProperty); set => SetValue(FirstPathProperty, value); }
        [Display(GroupName = "Paths")]
        public string SecondPath { get => GetValue(SecondPathProperty); set => SetValue(SecondPathProperty, value); }
    }
}
