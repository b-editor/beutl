using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Collections;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.PropertyAdapters;
using Beutl.Services.Adapters;
using Beutl.Testing.Headless;
using Beutl.Views.Editors;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class PresenterListItemEditorTests
{
    [AvaloniaTest]
    public async Task A_presenter_item_of_an_object_list_is_reordered_by_its_presenter_editor()
    {
        var group = new DrawableGroup();
        group.Children.Add(new DrawablePresenter());
        using var history = new HistoryScope(group);
        using var list = CreateList(new EnginePropertyAdapter<ICoreList<Drawable>>(group.Children, group), history);
        var window = new Window { Content = new ListEditor { DataContext = list }, Width = 480, Height = 600 };
        try
        {
            window.Show();
            await Task.Delay(350);
            HeadlessTestHelpers.Render(3);

            CoreObjectListItemEditor item = window.GetVisualDescendants().OfType<CoreObjectListItemEditor>().Single();
            ReferenceEditor presenterEditor = item.GetVisualDescendants().OfType<ReferenceEditor>().Single();

            Assert.That(item.ReorderHandle, Is.Not.Null.And.SameAs(presenterEditor.ReorderHandle));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task A_second_target_request_while_the_picker_is_open_opens_no_second_picker()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new FilterEffectPresenter());
        using var history = new HistoryScope(group);
        using var list = CreateList(new EnginePropertyAdapter<ICoreList<FilterEffect>>(group.Children, group), history);
        var window = new Window { Content = new ListEditor { DataContext = list }, Width = 480, Height = 600 };
        try
        {
            window.Show();
            await Task.Delay(350);
            HeadlessTestHelpers.Render(3);

            ReferenceEditor presenterEditor = window.GetVisualDescendants().OfType<FilterEffectListItemEditor>().Single()
                .GetVisualDescendants().OfType<ReferenceEditor>().Single();
            presenterEditor.RaiseEvent(new RoutedEventArgs(ReferenceEditor.SelectTargetRequestedEvent));
            HeadlessTestHelpers.Render();
            presenterEditor.RaiseEvent(new RoutedEventArgs(ReferenceEditor.SelectTargetRequestedEvent));
            HeadlessTestHelpers.Render();

            Assert.That(window.GetVisualDescendants().OfType<LibraryItemPickerFlyoutPresenter>().Count(), Is.EqualTo(1));
        }
        finally
        {
            window.Close();
        }
    }

    private static ListEditorViewModel<T> CreateList<T>(IPropertyAdapter<ICoreList<T>> adapter, HistoryScope history)
    {
        var list = new ListEditorViewModel<T>(adapter);
        list.Accept(new ServiceVisitor(
            (typeof(HistoryManager), history.History),
            (typeof(IPropertyEditorFactory), new PropertyEditorFactoryAdapter(TestShell.Extensions)),
            (typeof(ExtensionProvider), TestShell.Extensions)));
        list.IsExpanded.Value = true;
        return list;
    }
}
