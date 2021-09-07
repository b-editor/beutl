using System.Net;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;

using BEditor.ViewModels.ManagePlugins;

namespace BEditor.Views.ManagePlugins
{
    public sealed class Update : UserControl
    {
        public Update()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}