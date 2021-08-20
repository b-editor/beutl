using System;
using System.Linq;

using BEditor.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels.Settings
{
    public sealed class AppearanceViewModel
    {
        public sealed record LayerBorderItem(string Text, LayerBorder Border);

        public AppearanceViewModel()
        {
            SelectedLayerBorder = new ReactivePropertySlim<LayerBorderItem>(LayerBorderItems.First(i => i.Border == BEditor.Settings.Default.LayerBorder));

            SelectedLayerBorder.Subscribe(i => BEditor.Settings.Default.LayerBorder = i.Border);
        }

        public LayerBorderItem[] LayerBorderItems { get; } =
        {
            new(Strings.None, LayerBorder.None),
            new(Strings.Strong, LayerBorder.Strong),
            new(Strings.Thin, LayerBorder.Thin),
        };

        public ReactivePropertySlim<LayerBorderItem> SelectedLayerBorder { get; }

        public ReactiveCollection<string> Langs { get; } = new()
        {
            "ja-JP",
            "en-US",
        };
    }
}