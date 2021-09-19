using System.IO;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Controls;
using BEditor.Models;

namespace BEditor.Views
{
    public sealed class ObjectViewer : UserControl
    {
        private readonly ScrollViewer _scrollViewer;
        private readonly TreeView _treeView;
        private readonly FileSystemWatcher _watcher;

        public ObjectViewer()
        {
            _watcher = new FileSystemWatcher(AppModel.Current.Project.DirectoryName)
            {
                EnableRaisingEvents = true,
                IncludeSubdirectories = true,
            };

            InitializeComponent();

            _scrollViewer = this.FindControl<ScrollViewer>("scrollViewer");
            _treeView = new ProjectTreeView(AppModel.Current.Project, _watcher);
            _scrollViewer.Content = _treeView;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}