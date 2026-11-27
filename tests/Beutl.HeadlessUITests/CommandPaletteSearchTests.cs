using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class CommandPaletteSearchTests
{
    [AvaloniaTest]
    [TestCase("SAVE ALL", "main.saveAll", "すべて保存")]
    [TestCase("all files", "main.saveAll", "すべて保存")]
    [TestCase("すべて保存", "main.saveAll", "すべて保存")]
    [TestCase("Split by Current Frame", "timeline.split", "現在のフレームで分割")]
    [TestCase("timeline", "timeline.split", "現在のフレームで分割")]
    [TestCase("export project", "MenuBar.ExportProject", "プロジェクトをエクスポート…")]
    public async Task Search_finds_Japanese_commands_by_localized_and_English_text(
        string search, string commandId, string japaneseName)
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        try
        {
            MainViewModel main = TestShell.MainViewModel;
            var commands = new ContextCommandManager(new ContextCommandSettingsStore());
            commands.Register(MainViewExtension.Instance);
            commands.Register(TimelineTabExtension.Instance);
            var service = new CommandPaletteService(commands, new CommandPaletteHandlerProvider(() => main, main.EditorService),
                () => main.MenuBar, main.EditorService, main.ExtensionProvider);
            using var palette = new CommandPaletteViewModel(service, main.EditorService);
            var view = new CommandPaletteView { DataContext = palette };
            var window = new Window { Content = view, Width = 420, Height = 360 };
            try
            {
                window.Show();
                palette.Open();
                HeadlessTestHelpers.Render();
                Assert.That(CultureInfo.CurrentUICulture.Name, Is.EqualTo("ja-JP"));
                int unfilteredCount = palette.FilteredCommands.Count;

                window.KeyTextInput(search);
                await CommandPaletteTestHost.UntilAsync(() => palette.FilteredCommands.Count < unfilteredCount);

                Assert.That(palette.FilteredCommands.Select(item => item.Command.Id), Does.Contain(commandId));
                ListBox results = view.FindControl<ListBox>("ResultsListBox")!;
                Assert.That(results.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text), Does.Contain(japaneseName));
                HeadlessCapture.SaveIfRequested(window, nameof(CommandPaletteSearchTests), search);
            }
            finally
            {
                palette.Close();
                window.Close();
                HeadlessTestHelpers.Settle();
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
