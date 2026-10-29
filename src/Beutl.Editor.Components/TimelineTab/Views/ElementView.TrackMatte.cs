using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Graphics;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class ElementView
{
    private void PopulateTrackMatteMenu(ElementViewModel viewModel)
    {
        trackMatteMenu.Items.Clear();
        var sourceMenu = new FAMenuFlyoutSubItem { Text = Strings.MatteSource };
        MatteSourceKind kind = viewModel.Model.MatteSourceKind.Value;
        sourceMenu.Items.Add(CreateMatteItem(Strings.MatteSourceAbove, kind == MatteSourceKind.Above,
            model => model.SetMatteSource(MatteSourceKind.Above)));
        sourceMenu.Items.Add(CreateMatteItem(Strings.MatteSourceBelow, kind == MatteSourceKind.Below,
            model => model.SetMatteSource(MatteSourceKind.Below)));
        sourceMenu.Items.Add(new FAMenuFlyoutSeparator());
        sourceMenu.Items.Add(CreateMatteItem(StatusStrings.None,
            kind == MatteSourceKind.Layer && viewModel.Model.MatteSourceZIndex.Value == null,
            model => model.SetMatteSource(MatteSourceKind.Layer)));

        IEnumerable<int> layers = viewModel.Scene.Layers.Select(layer => layer.ZIndex.Value)
            .Concat(viewModel.Scene.Children.Select(element => element.ZIndex.Value))
            .Where(z => z != viewModel.Model.ZIndex.Value);
        // Keep a saved reference selectable after a layer move, or while its layer is empty.
        if (viewModel.Model.MatteSourceZIndex.Value is { } selected) layers = layers.Append(selected);
        foreach (int zIndex in layers.Distinct().Order())
        {
            string? name = viewModel.Scene.Layers.FirstOrDefault(layer => layer.ZIndex.Value == zIndex)?.Name.Value;
            string label = string.IsNullOrWhiteSpace(name) ? zIndex.ToString(CultureInfo.CurrentCulture) : $"{zIndex}: {name}";
            sourceMenu.Items.Add(CreateMatteItem(label,
                kind == MatteSourceKind.Layer && viewModel.Model.MatteSourceZIndex.Value == zIndex,
                model => model.SetMatteSource(MatteSourceKind.Layer, zIndex)));
        }

        trackMatteMenu.Items.Add(sourceMenu);
        trackMatteMenu.Items.Add(new FAMenuFlyoutSeparator());
        foreach ((MatteMode mode, string label) in new[]
                 {
                     (MatteMode.None, StatusStrings.None),
                     (MatteMode.Alpha, Strings.MatteAlpha),
                     (MatteMode.Luma, Strings.MatteLuma),
                     (MatteMode.InvertedAlpha, Strings.MatteInvertedAlpha),
                     (MatteMode.InvertedLuma, Strings.MatteInvertedLuma),
                 })
        {
            trackMatteMenu.Items.Add(CreateMatteItem(label, viewModel.Model.MatteMode.Value == mode,
                model => model.SetMatteMode(mode)));
        }

        trackMatteMenu.Items.Add(new FAMenuFlyoutSeparator());
        trackMatteMenu.Items.Add(CreateMatteItem(Strings.IsVisibleInOutput, viewModel.Model.IsVisibleInOutput.Value,
            model => model.ToggleOutputVisibility()));
    }

    private FAToggleMenuFlyoutItem CreateMatteItem(string text, bool isChecked, Action<ElementViewModel> action)
    {
        var item = new FAToggleMenuFlyoutItem { Text = text, IsChecked = isChecked };
        item.Click += (_, _) =>
        {
            if (DataContext is ElementViewModel model) action(model);
        };
        return item;
    }
}
