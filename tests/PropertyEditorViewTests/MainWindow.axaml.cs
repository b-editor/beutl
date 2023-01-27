using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Beutl.Controls.PropertyEditors;

namespace PropertyEditorViewTests
{
    public enum Fruits
    {
        [Display(Name = "リンゴ")]
        Apple,
        [Display(Name = "ブドウ")]
        Grape,
        [Display(Name = "オレンジ")]
        Orange,
        [Display(Name = "レモン")]
        Lemon
    }

    public partial class MainWindow : Window
    {
        //private Vector2Editor<float> vector2Editor;
        //private Vector3Editor<float> vector3Editor;
        //private Vector4Editor<float> vector4Editor;
        private EnumEditor enumEditor;
        private EnumEditor<Fruits> typedenumEditor;
        private NumberEditor<int> indexEditor;

        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            //vector2Editor = new Vector2Editor<float>() { Header = "Property 3" };
            //vector3Editor = new Vector3Editor<float>() { Header = "Property 4" };
            var cornerRadiusEditor = new Vector4Editor<float>() { Header = "Property 5", Theme = (Avalonia.Styling.ControlTheme)this.FindResource("CornerRadiusEditorStyle")! };
            var thicknessEditor = new Vector4Editor<float>() { Header = "Property 5", Theme = (Avalonia.Styling.ControlTheme)this.FindResource("ThicknessEditorStyle")! };
            var boolEditor = new BooleanEditor() { Header = "Property 6" };
            var colorEditor = new ColorEditor() { Header = "Property 7" };
            enumEditor = new EnumEditor() { Header = "Property 8", Items = new string[] { "Apple", "Grape", "Orange", "Lemon" } };
            typedenumEditor = new EnumEditor<Fruits>() { Header = "Property 9" };
            indexEditor = new NumberEditor<int>() { Header = "Property 9(Index)" };
            //stack.Children.Add(vector2Editor);
            //stack.Children.Add(vector3Editor);
            stack.Children.Add(cornerRadiusEditor);
            stack.Children.Add(thicknessEditor);
            stack.Children.Add(boolEditor);
            stack.Children.Add(colorEditor);
            stack.Children.Add(enumEditor);
            stack.Children.Add(typedenumEditor);
            stack.Children.Add(indexEditor);

            indexEditor.ValueChanging += IndexEditor_ValueChanged;
            typedenumEditor.ValueChanged += EnumEditor_ValueChanged;
        }

        private void EnumEditor_ValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
        {
            Debug.WriteLine($"EnumEditor.ValueChanged: {e.NewValue}");
        }

        private void IndexEditor_ValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
        {
            typedenumEditor.SelectedIndex = indexEditor.Value;
        }
    }
}
