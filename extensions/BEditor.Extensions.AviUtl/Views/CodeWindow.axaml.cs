using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using AvaloniaEdit;

using BEditor.Controls;

namespace BEditor.Extensions.AviUtl.Views
{
    public partial class CodeWindow : FluentWindow
    {
        public CodeWindow()
        {
            InitializeComponent();
            Editor = this.FindControl<TextEditor>("Editor");
            Editor.Encoding = CodePagesEncodingProvider.Instance.GetEncoding("shift-jis")!;
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public TextEditor Editor { get; }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
