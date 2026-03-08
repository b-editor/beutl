using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;

namespace PropertyEditorViewTests
{
    public partial class MainWindow : Window
    {
        //private Vector2Editor<float> vector2Editor;
        //private Vector3Editor<float> vector3Editor;
        //private Vector4Editor<float> vector4Editor;
        private readonly EnumEditor _enumEditor;
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
            _enumEditor = new EnumEditor() { Header = "Property 8", Items = ["Apple", "Grape", "Orange", "Lemon"] };
            _indexEditor = new NumberEditor<int>() { Header = "Property 9(Index)" };
            //stack.Children.Add(vector2Editor);
            //stack.Children.Add(vector3Editor);
            stack.Children.Add(cornerRadiusEditor);
            stack.Children.Add(thicknessEditor);
            stack.Children.Add(boolEditor);
            stack.Children.Add(colorEditor);
            stack.Children.Add(_enumEditor);
            stack.Children.Add(_indexEditor);
        }
    }
}
