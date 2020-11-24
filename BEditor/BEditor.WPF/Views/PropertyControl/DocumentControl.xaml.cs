using System;
using System.Windows.Controls;

using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;

using BEditor.Core.Data.Property;
using BEditor.Core.Data.Primitive.Properties;
using System.Windows;

namespace BEditor.Views.PropertyControls
{
    /// <summary>
    /// Document.xaml の相互作用ロジック
    /// </summary>
    public partial class Document : UserControl, ICustomTreeViewItem
    {
        public double LogicHeight => document.HeightProperty ?? 125;
        private readonly DocumentProperty document;

        public Document(DocumentProperty document)
        {
            InitializeComponent();

            DataContext = new DocumentPropertyViewModel(this.document = document);
        }

        private void BindClick(object sender, RoutedEventArgs e)
        {
            var window = new BindSettings()
            {
                DataContext = new BindSettingsViewModel<string>(document)
            };
            window.ShowDialog();
        }
    }
}
