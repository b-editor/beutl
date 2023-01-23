using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Beutl.Controls.PropertyEditors;

namespace PropertyEditorViewTests
{
    public partial class MainWindow : Window
    {
        private Vector2Editor<float> vector2Editor;
        private Vector3Editor<float> vector3Editor;
        private Vector4Editor<float> vector4Editor;

        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            vector2Editor = new Vector2Editor<float>() { Header = "Property 3" };
            vector3Editor = new Vector3Editor<float>() { Header = "Property 4" };
            vector4Editor = new Vector4Editor<float>() { Header = "Property 5" };
            stack.Children.Add(vector2Editor);
            stack.Children.Add(vector3Editor);
            stack.Children.Add(vector4Editor);
            vector2Editor.ValueChanged += Vector2Editor_ValueChanged;
            vector2Editor.ValueChanging += Vector2Editor_ValueChanging;
        }

        private void Vector2Editor_ValueChanging(object? sender, PropertyEditorValueChangedEventArgs e)
        {

        }

        private void Vector2Editor_ValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
        {

        }
    }
}
