using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Collections;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.PropertyAdapters;
using Beutl.Services.Adapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ListEditorAddTests
{
    [AvaloniaTest]
    public async Task Closing_the_type_picker_adds_nothing_and_reports_nothing()
    {
        var group = new FilterEffectGroup();
        using var history = new HistoryScope(group);
        var adapter = new EnginePropertyAdapter<ICoreList<FilterEffect>>(group.Children, group);
        using var list = new ListEditorViewModel<FilterEffect>(adapter);
        list.Accept(new ServiceVisitor(
            (typeof(HistoryManager), history.History),
            (typeof(IPropertyEditorFactory), new PropertyEditorFactoryAdapter(TestShell.Extensions)),
            (typeof(ExtensionProvider), TestShell.Extensions)));
        var view = new ListEditor { DataContext = list };
        var window = new Window { Content = view, Width = 480, Height = 600 };
        using var notifications = new NotificationCapture();
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            view.GetVisualDescendants().OfType<Button>().Single(button => button is not ToggleButton)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            LibraryItemPickerFlyoutPresenter? picker = null;
            for (int i = 0; i < 200 && picker is null; i++)
            {
                await Task.Delay(25);
                HeadlessTestHelpers.Render();
                picker = window.GetVisualDescendants().OfType<LibraryItemPickerFlyoutPresenter>().SingleOrDefault();
            }
            Assert.That(picker, Is.Not.Null, "A list of an abstract type asks for the type to add.");

            picker!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(group.Children, Is.Empty);
                Assert.That(notifications.All, Is.Empty, "Closing the picker is not an error.");
            });
        }
        finally
        {
            window.Close();
        }
    }
}
