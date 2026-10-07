using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.Services;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class TimelineTabView
{
    private void PopulateAddElementSubMenu()
    {
        foreach (LibraryItem item in LibraryService.Current.Items)
        {
            Control? menuItem = CreateMenuItemForLibraryItem(item);
            if (menuItem != null)
            {
                AddElementSubMenu.Items.Add(menuItem);
            }
        }
    }

    private Control? CreateMenuItemForLibraryItem(LibraryItem item)
    {
        switch (item)
        {
            case SingleTypeLibraryItem single when single.Format == KnownLibraryItemFormats.EngineObject:
                {
                    var menuItem = new FAMenuFlyoutItem { Text = single.DisplayName, Tag = single.ImplementationType };
                    menuItem.Click += AddElementWithTypeClick;
                    return menuItem;
                }

            case MultipleTypeLibraryItem multiple when multiple.Types.TryGetValue(KnownLibraryItemFormats.EngineObject, out Type? type):
                {
                    var menuItem = new FAMenuFlyoutItem { Text = multiple.DisplayName, Tag = type };
                    menuItem.Click += AddElementWithTypeClick;
                    return menuItem;
                }

            case GroupLibraryItem group:
                {
                    var subItems = new List<Control>();
                    foreach (LibraryItem child in group.Items)
                    {
                        Control? childItem = CreateMenuItemForLibraryItem(child);
                        if (childItem != null)
                        {
                            subItems.Add(childItem);
                        }
                    }

                    if (subItems.Count == 0)
                        return null;

                    var subMenu = new FAMenuFlyoutSubItem { Text = group.DisplayName };
                    foreach (Control subItem in subItems)
                    {
                        subMenu.Items.Add(subItem);
                    }
                    return subMenu;
                }

            default:
                return null;
        }
    }

    private void AddElementWithTypeClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        if (sender is not FAMenuFlyoutItem { Tag: Type operatorType }) return;

        AddEngineObjectAtClick(ViewModel, operatorType);
    }

    private static void AddEngineObjectAtClick(TimelineTabViewModel viewModel, Type type)
    {
        viewModel.AddElement.Execute(new ElementDescription(
            viewModel.ClickedFrame,
            s_defaultElementLength,
            viewModel.CalculateClickedLayer(),
            new ElementSource.EngineObject(() => (EngineObject)Activator.CreateInstance(type)!)));
    }

    private void AddAdjustmentLayerClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        ViewModel.AddElement.Execute(new ElementDescription(
            ViewModel.ClickedFrame,
            s_defaultElementLength,
            ViewModel.CalculateClickedLayer(),
            new ElementSource.EngineObject(
                () => new Beutl.Graphics.SourceBackdrop { Clear = { CurrentValue = true } }),
            Name: Strings.AdjustmentLayer));
    }

    private void PopulateAddFromTemplateSubMenu()
    {
        AddFromTemplateSubMenu.Items.Clear();
        foreach (ObjectTemplateItem template in ObjectTemplateService.Instance
            .FindByBaseType(typeof(Element)))
        {
            var menuItem = new FAMenuFlyoutItem { Text = template.Name.Value, Tag = template };
            menuItem.Click += AddElementFromTemplateClick;
            AddFromTemplateSubMenu.Items.Add(menuItem);
        }

        AddFromTemplateSubMenu.IsEnabled = AddFromTemplateSubMenu.Items.Count > 0;
    }

    private void AddElementFromTemplateClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        if (sender is not FAMenuFlyoutItem { Tag: ObjectTemplateItem template }) return;

        ViewModel.AddElement.Execute(ElementTemplateResolver.CreateDescription(
            template,
            ViewModel.ClickedFrame,
            ViewModel.CalculateClickedLayer()));
    }

    private void ShowSceneSettings(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        IEditorContext editorContext = ViewModel.EditorContext;
        SceneSettingsTabViewModel? tab = editorContext.FindToolTab<SceneSettingsTabViewModel>();
        if (tab != null)
        {
            tab.IsSelected.Value = true;
        }
        else
        {
            editorContext.OpenToolTab(new SceneSettingsTabViewModel(editorContext));
        }
    }

    private void ShowBpmGridFlyout(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var bpmGrid = ViewModel.Options.Value.BpmGrid;
        var flyout = new Editor.Components.Views.BpmGridFlyout
        {
            IsEnabledChecked = bpmGrid.IsEnabled,
            Bpm = (decimal)bpmGrid.Bpm,
            Subdivisions = bpmGrid.Subdivisions,
            OffsetSeconds = (decimal)bpmGrid.Offset.TotalSeconds,
        };
        flyout.OptionsChanged += (_, options) =>
        {
            ViewModel.Options.Value = ViewModel.Options.Value with { BpmGrid = options };
        };

        flyout.ShowAt(TimelinePanel, true);
    }
}
