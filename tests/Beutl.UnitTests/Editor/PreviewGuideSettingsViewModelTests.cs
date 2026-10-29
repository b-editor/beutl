using Avalonia.Media;

using Beutl.Configuration;
using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;

namespace Beutl.UnitTests.Editor;

// The preview settings tab and the editor settings page both edit the guides through this view model, which keeps
// EditorConfig the only copy of the settings.
[TestFixture]
public sealed class PreviewGuideSettingsViewModelTests
{
    [Test]
    public void A_check_box_sets_and_clears_its_flag_alone()
    {
        var config = new EditorConfig { PreviewGuideKinds = { Value = PreviewGuideKinds.TitleSafe } };
        using var vm = new PreviewGuideSettingsViewModel(config);
        PreviewGuideKindItem grid = vm.Kinds.Single(item => item.Kind == PreviewGuideKinds.CustomGrid);

        Assert.That(vm.IsGridEnabled.Value, Is.False);
        grid.IsChecked.Value = true;
        Assert.That(config.PreviewGuideKinds.Value, Is.EqualTo(PreviewGuideKinds.TitleSafe | PreviewGuideKinds.CustomGrid));
        Assert.That(vm.IsGridEnabled.Value, Is.True);

        grid.IsChecked.Value = false;
        Assert.That(config.PreviewGuideKinds.Value, Is.EqualTo(PreviewGuideKinds.TitleSafe));
    }

    [Test]
    public void Every_guide_has_a_check_box()
    {
        var config = new EditorConfig();
        using var vm = new PreviewGuideSettingsViewModel(config);

        PreviewGuideKinds all = vm.Kinds.Aggregate(PreviewGuideKinds.None, (kinds, item) => kinds | item.Kind);
        PreviewGuideKinds expected = Enum.GetValues<PreviewGuideKinds>().Aggregate(PreviewGuideKinds.None, (kinds, kind) => kinds | kind);
        Assert.That(all, Is.EqualTo(expected));
    }

    // The toggle key writes the config directly, and an open view follows it.
    [Test]
    public void A_view_follows_changes_made_elsewhere()
    {
        var config = new EditorConfig();
        using var vm = new PreviewGuideSettingsViewModel(config);

        config.IsPreviewGuideEnabled.Value = true;
        config.PreviewGuideKinds.Value = PreviewGuideKinds.Diagonals;
        config.PreviewGuideAspectRatios.Value = "1:1";

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsEnabled.Value, Is.True);
            Assert.That(vm.Kinds.Single(item => item.Kind == PreviewGuideKinds.Diagonals).IsChecked.Value, Is.True);
            Assert.That(vm.Kinds.Single(item => item.Kind == PreviewGuideKinds.TitleSafe).IsChecked.Value, Is.False);
            Assert.That(vm.AspectRatios.Value, Is.EqualTo("1:1"));
        });
    }

    // Color.ToString writes a known color's name, such as "Red", where the setting promises #AARRGGBB.
    [Test]
    public void A_color_is_saved_as_hex()
    {
        var config = new EditorConfig();
        using var vm = new PreviewGuideSettingsViewModel(config);

        vm.Color.Value = Colors.Red;

        Assert.That(config.PreviewGuideColor.Value, Is.EqualTo("#FFFF0000"));
    }

    [Test]
    public void A_color_that_does_not_parse_reads_as_white()
    {
        var config = new EditorConfig { PreviewGuideColor = { Value = "not a color" } };
        using var vm = new PreviewGuideSettingsViewModel(config);

        Assert.That(vm.Color.Value, Is.EqualTo(Colors.White));
        Assert.That(config.PreviewGuideColor.Value, Is.EqualTo("not a color"), "reading the setting does not rewrite it");
    }

    [Test]
    public void An_invalid_list_stays_in_the_text_box_with_an_error_and_is_not_saved()
    {
        var config = new EditorConfig { PreviewGuideAspectRatios = { Value = "9:16" } };
        using var vm = new PreviewGuideSettingsViewModel(config);

        vm.AspectRatios.Value = "9:16, wide";

        Assert.Multiple(() =>
        {
            Assert.That(vm.AspectRatios.HasErrors, Is.True);
            Assert.That(vm.AspectRatios.Value, Is.EqualTo("9:16, wide"));
            Assert.That(config.PreviewGuideAspectRatios.Value, Is.EqualTo("9:16"));
        });

        vm.AspectRatios.Value = "9:16, 2.39:1";

        Assert.That(vm.AspectRatios.HasErrors, Is.False);
        Assert.That(config.PreviewGuideAspectRatios.Value, Is.EqualTo("9:16, 2.39:1"));
    }

    [Test]
    public void A_preset_writes_its_ratios()
    {
        var config = new EditorConfig();
        using var vm = new PreviewGuideSettingsViewModel(config);
        PreviewGuideAspectRatioPreset feed = vm.Presets.Single(preset => preset.Name == "Instagram Feed");

        vm.SelectedPreset.Value = feed;

        Assert.That(config.PreviewGuideAspectRatios.Value, Is.EqualTo("4:5, 1:1"));
        Assert.That(vm.AspectRatios.Value, Is.EqualTo("4:5, 1:1"));
    }

    // TikTok, Reels and Shorts share 9:16; the list matching an earlier preset must not swap the one picked for it.
    [Test]
    public void The_picked_preset_stays_while_the_list_matches_it()
    {
        var config = new EditorConfig();
        using var vm = new PreviewGuideSettingsViewModel(config);
        PreviewGuideAspectRatioPreset tikTok = vm.Presets.Single(preset => preset.Name == "TikTok");

        vm.SelectedPreset.Value = tikTok;
        Assert.That(vm.SelectedPreset.Value, Is.SameAs(tikTok));

        vm.AspectRatios.Value = "9:16, 1:1";
        Assert.That(vm.SelectedPreset.Value, Is.Null, "no preset has these ratios");

        vm.AspectRatios.Value = "4:5,1:1";
        Assert.That(vm.SelectedPreset.Value?.Name, Is.EqualTo("Instagram Feed"), "spacing does not matter");
    }

    [Test]
    public void Every_preset_reads()
    {
        foreach (PreviewGuideAspectRatioPreset preset in PreviewGuideAspectRatioPreset.All)
        {
            Assert.That(PreviewGuideAspectRatio.TryParseList(preset.AspectRatios, out PreviewGuideAspectRatio[] ratios), Is.True, preset.Name);
            Assert.That(ratios, Is.Not.Empty, preset.Name);
        }
    }

    [Test]
    public void Opacity_round_trips_through_the_slider()
    {
        var config = new EditorConfig();
        using var vm = new PreviewGuideSettingsViewModel(config);

        vm.Opacity.Value = 0.35;
        Assert.That(config.PreviewGuideOpacity.Value, Is.EqualTo(0.35f));

        // The setting's range coerces the value, and the slider follows it.
        vm.Opacity.Value = 2;
        Assert.That(config.PreviewGuideOpacity.Value, Is.EqualTo(1f));
        Assert.That(vm.Opacity.Value, Is.EqualTo(1));
    }
}
