using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Extensibility;
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
            using var palette = CreatePalette(TimelineTabExtension.Instance);
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

                CommandPaletteItemViewModel match = palette.FilteredCommands.Single(item => item.Command.Id == commandId);
                ListBox results = view.FindControl<ListBox>("ResultsListBox")!;
                results.ScrollIntoView(match);
                await CommandPaletteTestHost.UntilAsync(() => results.ContainerFromItem(match) is not null);
                Control row = results.ContainerFromItem(match)!;
                Assert.That(row.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text), Does.Contain(japaneseName));
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

    [AvaloniaTest]
    public async Task English_and_Japanese_matches_rank_names_before_descriptions_and_categories()
    {
        string[] expectedOrder = [SceneEditorExtension.PreviewZoomFitCommandId, "editor.togglePreviewCheckerboard", "main.saveAll"];
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        try
        {
            await TestShell.OpenNewSceneAsync("command-palette-ranking");
            using var palette = CreatePalette(SceneEditorExtension.Instance);
            palette.Open();

            // Each match follows a letter in both languages, with the same word-boundary scoring.
            var english = await SearchAsync(palette, "iew");
            var japanese = await SearchAsync(palette, "表示");

            Assert.That(english.Select(match => match.Id), Is.EqualTo(expectedOrder));
            Assert.That(english, Is.EqualTo(japanese));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
            await TestReset.ResetShellAsync();
        }

        async Task<(string Id, int Relevance)[]> SearchAsync(CommandPaletteViewModel palette, string query)
        {
            // Start from an empty list so every result item is created with the query's score.
            palette.Query.Value = "no matching command";
            await CommandPaletteTestHost.UntilAsync(() => palette.HasNoResults.Value);
            palette.Query.Value = query;
            await CommandPaletteTestHost.UntilAsync(() => palette.FilteredCommands.Count > 0);

            return palette.FilteredCommands
                .Where(item => expectedOrder.Contains(item.Command.Id))
                .Select(item => (item.Command.Id, item.Relevance))
                .ToArray();
        }
    }

    private static CommandPaletteViewModel CreatePalette(ViewExtension extension)
    {
        MainViewModel main = TestShell.MainViewModel;
        var commands = new ContextCommandManager(new ContextCommandSettingsStore());
        commands.Register(MainViewExtension.Instance);
        commands.Register(extension);

        var service = new CommandPaletteService(commands, new CommandPaletteHandlerProvider(() => main, main.EditorService),
            () => main.MenuBar, main.EditorService, main.ExtensionProvider);
        return new CommandPaletteViewModel(service, main.EditorService);
    }
}
