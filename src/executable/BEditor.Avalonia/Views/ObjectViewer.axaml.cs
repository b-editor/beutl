using System;
using System.IO;
using System.Reactive.Linq;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Controls;
using BEditor.Models;

using Reactive.Bindings.Extensions;

namespace BEditor.Views
{
    public sealed class ObjectViewer : UserControl
    {
        private readonly ScrollViewer _scrollViewer;
        private FileSystemWatcher? _watcher;

        public ObjectViewer()
        {
            InitializeComponent();

            _scrollViewer = this.FindControl<ScrollViewer>("scrollViewer");

            AppModel.Current.ObserveProperty(p => p.Project)
                .ObserveOnUIDispatcher()
                .Subscribe(proj =>
                {
                    if (proj != null)
                    {
                        _watcher = new FileSystemWatcher(proj.DirectoryName)
                        {
                            EnableRaisingEvents = true,
                            IncludeSubdirectories = true,
                        };

                        _scrollViewer.Content = new ProjectTreeView(proj, _watcher);
                    }
                    else
                    {
                        _watcher?.Dispose();
                        _scrollViewer.Content = null;
                    }
                });
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}