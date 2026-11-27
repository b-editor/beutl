using Avalonia.Controls;
using Beutl.Editor.Services.Captions;
using Beutl.Language;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

internal sealed partial class ExportSubtitlesDialog : FAContentDialog
{
    private static readonly FormatChoice[] s_formats =
    [
        new("SRT", CaptionFormats.Srt, "srt"),
        new("ASS", CaptionFormats.Ass, "ass"),
        new("WebVTT", CaptionFormats.WebVtt, "vtt"),
    ];

    public ExportSubtitlesDialog()
    {
        InitializeComponent();
        FormatPicker.ItemsSource = s_formats;
        FormatPicker.SelectedIndex = 0;
        SelectionSource.IsCheckedChanged += (_, _) => UpdateSource();
        LayersSource.IsCheckedChanged += (_, _) => UpdateSource();
    }

    internal ExportSubtitlesDialog(Scene scene, bool hasSelection) : this()
    {
        ILookup<int, string> layerNames = scene.Layers.ToLookup(layer => layer.ZIndex.Value, layer => layer.Name.Value);
        foreach (int layer in scene.Children.Select(element => element.ZIndex.Value).Distinct().Order())
        {
            string? name = layerNames[layer].FirstOrDefault();
            var option = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(name) ? $"{layer}" : $"{layer} · {name}",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                Tag = layer,
            };
            option.IsCheckedChanged += (_, _) => UpdateSource();
            LayerOptions.Children.Add(option);
        }

        SelectionSource.IsEnabled = hasSelection;
        SelectionSource.IsChecked = hasSelection;
        LayersSource.IsChecked = !hasSelection;
        UpdateSource();
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    internal bool UsesSelection => SelectionSource.IsChecked == true;

    internal FormatChoice Format => (FormatChoice)FormatPicker.SelectedItem!;

    internal HashSet<int> GetSelectedLayers() => LayerOptions.Children.OfType<CheckBox>()
        .Where(option => option.IsChecked == true).Select(option => (int)option.Tag!).ToHashSet();

    private void UpdateSource()
    {
        LayerList.IsVisible = !UsesSelection && LayerOptions.Children.Count > 0;
        NoLayersMessage.IsVisible = !UsesSelection && LayerOptions.Children.Count == 0;
        IsPrimaryButtonEnabled = UsesSelection || LayerOptions.Children.OfType<CheckBox>().Any(option => option.IsChecked == true);
    }

    internal sealed record FormatChoice(string Name, CaptionFormatId Id, string Extension)
    {
        public override string ToString() => Name;
    }
}
