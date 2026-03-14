using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class EnumEditorViewModel<T> : ValueEditorViewModel<T>
    where T : struct, Enum
{
    private readonly T[] _enumValues;
    private readonly EnumItem[] _enumItems;

    public EnumEditorViewModel(IPropertyAdapter<T> property) : base(property)
    {
        _enumValues = Enum.GetValues<T>();
        _enumItems = Enum.GetNames<T>()
            .Select(typeof(T).GetField)
            .Zip(_enumValues)
            .Select(t =>
            {
                if (t.First != null)
                {
                    return new EnumItem(
                        TypeDisplayHelpers.GetLocalizedName(t.First),
                        TypeDisplayHelpers.GetLocalizedDescription(t.First),
                        t.Second);
                }
                else
                {
                    return new EnumItem(
                        t.Second.ToString(),
                        null,
                        t.Second);
                }
            })
            .ToArray();
        SelectedIndex = Value.Select(v => _enumValues.IndexOf(v))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<int> SelectedIndex { get; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is EnumEditor editor && !Disposables.IsDisposed)
        {
            editor.Items = _enumItems;
            editor.Bind(EnumEditor.SelectedIndexProperty, SelectedIndex.ToBinding())
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<int> args)
        {
            var newValue = _enumValues[Math.Clamp(args.NewValue, 0, _enumValues.Length - 1)];
            SetValue(Value.Value, newValue);
        }
    }
}
