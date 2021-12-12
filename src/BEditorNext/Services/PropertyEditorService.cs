using Avalonia.Controls;

using BEditorNext.ProjectSystem;
using BEditorNext.ViewModels.Editors;
using BEditorNext.Views.Editors;

namespace BEditorNext.Services;

public static class PropertyEditorService
{
    private record struct Editor(Func<ISetter, Control?> CreateEditor, Func<ISetter, object?> CreateViewModel);

    private static readonly Dictionary<Type, Editor> s_editors = new()
    {
        { typeof(bool), new(_ => new BooleanEditor(), s => new BooleanEditorViewModel((Setter<bool>)s)) },
        { typeof(byte), new(_ => new NumberEditor<byte>(), s => new ByteEditorViewModel((Setter<byte>)s)) },
        { typeof(decimal), new(_ => new NumberEditor<decimal>(), s => new DecimalEditorViewModel((Setter<decimal>)s)) },
        { typeof(double), new(_ => new NumberEditor<double>(), s => new DoubleEditorViewModel((Setter<double>)s)) },
        { typeof(float), new(_ => new NumberEditor<float>(), s => new FloatEditorViewModel((Setter<float>)s)) },
        { typeof(short), new(_ => new NumberEditor<short>(), s => new Int16EditorViewModel((Setter<short>)s)) },
        { typeof(int), new(_ => new NumberEditor<int>(), s => new Int32EditorViewModel((Setter<int>)s)) },
        { typeof(long), new(_ => new NumberEditor<long>(), s => new Int64EditorViewModel((Setter<long>)s)) },
        { typeof(sbyte), new(_ => new NumberEditor<sbyte>(), s => new SByteEditorViewModel((Setter<sbyte>)s)) },
        { typeof(ushort), new(_ => new NumberEditor<ushort>(), s => new UInt16EditorViewModel((Setter<ushort>)s)) },
        { typeof(uint), new(_ => new NumberEditor<uint>(), s => new UInt32EditorViewModel((Setter<uint>)s)) },
        { typeof(ulong), new(_ => new NumberEditor<ulong>(), s => new UInt64EditorViewModel((Setter<ulong>)s)) },
    };

    public static object? CreateEditor(ISetter setter)
    {
        if (s_editors.ContainsKey(setter.Property.PropertyType))
        {
            Editor editor = s_editors[setter.Property.PropertyType];
            Control? control = editor.CreateEditor(setter);

            if (control != null)
            {
                control.DataContext = editor.CreateViewModel(setter);
            }

            return control;
        }

        return null;
    }
}
