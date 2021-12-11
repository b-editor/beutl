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
        { typeof(float), new(_ => new FloatEditor(), s => new FloatEditorViewModel((Setter<float>)s)) },
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
