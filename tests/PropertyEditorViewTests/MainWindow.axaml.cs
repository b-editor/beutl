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
        private readonly EnumEditor _enumEditor;
        private readonly EnumEditor<Fruits> _typedenumEditor;
        private readonly NumberEditor<int> _indexEditor;

        public MainWindow()
        {
            InitializeComponent();
            //vector2Editor = new Vector2Editor<float>() { Header = "Property 3" };
            //vector3Editor = new Vector3Editor<float>() { Header = "Property 4" };
            var cornerRadiusEditor = new Vector4Editor<float>() { Header = "Property 5", Theme = (Avalonia.Styling.ControlTheme)this.FindResource("CornerRadiusEditorStyle")! };
            var thicknessEditor = new Vector4Editor<float>() { Header = "Property 5", Theme = (Avalonia.Styling.ControlTheme)this.FindResource("ThicknessEditorStyle")! };
            var boolEditor = new BooleanEditor() { Header = "Property 6" };
            var colorEditor = new ColorEditor() { Header = "Property 7" };
            _enumEditor = new EnumEditor() { Header = "Property 8", Items = new string[] { "Apple", "Grape", "Orange", "Lemon" } };
            _typedenumEditor = new EnumEditor<Fruits>() { Header = "Property 9" };
            _indexEditor = new NumberEditor<int>() { Header = "Property 9(Index)" };
            //stack.Children.Add(vector2Editor);
            //stack.Children.Add(vector3Editor);
            stack.Children.Add(cornerRadiusEditor);
            stack.Children.Add(thicknessEditor);
            stack.Children.Add(boolEditor);
            stack.Children.Add(colorEditor);
            stack.Children.Add(_enumEditor);
            stack.Children.Add(_typedenumEditor);
            stack.Children.Add(_indexEditor);

            _indexEditor.ValueChanged += IndexEditor_ValueChanged;
            _typedenumEditor.ValueConfirmed += EnumEditor_ValueConfirmed;
        }

        private void EnumEditor_ValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
        {
            Debug.WriteLine($"EnumEditor.ValueConfiemed: {e.NewValue}");
        }

        private void IndexEditor_ValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
        {
            _typedenumEditor.SelectedIndex = _indexEditor.Value;
        }
    }
}
