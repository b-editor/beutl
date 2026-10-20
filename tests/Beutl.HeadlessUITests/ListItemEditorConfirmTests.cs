using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.ViewModels.Editors;
using Reactive.Bindings;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using AColor = Avalonia.Media.Color;

namespace Beutl.HeadlessUITests;

// A list item with an ordinary editor gets the editor every other surface gets, so its view model attaches its
// handlers once and one confirmed edit writes the value once.
[TestFixture]
public sealed class ListItemEditorConfirmTests
{
    [AvaloniaTest]
    public void A_boolean_item_writes_a_confirmed_edit_once()
    {
        Assert.That(
            WritesForOneConfirmation(false, new PropertyEditorValueChangedEventArgs<bool>(true, false, PropertyEditor.ValueConfirmedEvent)),
            Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void A_string_item_writes_a_confirmed_edit_once()
    {
        Assert.That(
            WritesForOneConfirmation<string?>("old", new PropertyEditorValueChangedEventArgs<string?>("new", "old", PropertyEditor.ValueConfirmedEvent)),
            Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void A_number_item_writes_a_confirmed_edit_once()
    {
        Assert.That(
            WritesForOneConfirmation(1f, new PropertyEditorValueChangedEventArgs<float>(2f, 1f, PropertyEditor.ValueConfirmedEvent)),
            Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void A_color_item_writes_a_confirmed_edit_once()
    {
        Assert.That(
            WritesForOneConfirmation(
                Colors.White,
                new PropertyEditorValueChangedEventArgs<AColor>(Avalonia.Media.Colors.Red, Avalonia.Media.Colors.White, PropertyEditor.ValueConfirmedEvent)),
            Is.EqualTo(1));
    }

    private static int WritesForOneConfirmation<T>(T initial, RoutedEventArgs confirmation)
    {
        var adapter = new CountingAdapter<T>(initial);
        (BaseEditorViewModel vm, Control control) = CreateControl(adapter, EditorSurface.ListItem);
        using BaseEditorViewModel viewModel = vm;
        using var history = new HistoryScope(new Beutl.ProjectSystem.Element());
        viewModel.Accept(ServiceVisitor.For(history.History));

        control.RaiseEvent(confirmation);
        return adapter.Writes;
    }

    // An item adapter that counts the writes it receives.
    private sealed class CountingAdapter<T>(T value) : IPropertyAdapter<T>
    {
        private readonly ReactivePropertySlim<T?> _value = new(value);

        public int Writes { get; private set; }

        public Type ImplementedType => typeof(CountingAdapter<T>);

        public Type PropertyType => typeof(T);

        public string DisplayName => "Item";

        public string? Description => null;

        public bool IsReadOnly => false;

        public object? GetDefaultValue() => default(T);

        public void SetValue(T? newValue)
        {
            Writes++;
            _value.Value = newValue;
        }

        public T? GetValue() => _value.Value;

        public IObservable<T?> GetObservable() => _value;
    }
}
