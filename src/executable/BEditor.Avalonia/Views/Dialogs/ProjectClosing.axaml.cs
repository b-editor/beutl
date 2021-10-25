using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Controls;
using BEditor.Data;
using BEditor.Models;
using BEditor.Properties;

namespace BEditor.Views.Dialogs
{
    public partial class ProjectClosing : FluentWindow
    {
        private readonly Project _project = AppModel.Current.Project;

        public ProjectClosing()
        {
            InitializeComponent();
            this.FindControl<TextBlock>("tb").Text = string.Format(Strings.DoYouWantToSaveYourChangesTo, _project.Name);
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public void Save(object s, RoutedEventArgs e)
        {
            _project.Save();

            // プロジェクトを閉じる場合trueを返す。
            Close(true);
        }

        public void DontSave(object s, RoutedEventArgs e)
        {
            Close(true);
        }

        public void Cancel(object s, RoutedEventArgs e)
        {
            Close(false);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
