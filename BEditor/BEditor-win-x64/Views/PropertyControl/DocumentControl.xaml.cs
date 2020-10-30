using System;
using System.Windows.Controls;

using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;

using BEditor.NET.Data.PropertyData;

namespace BEditor.Views.PropertyControls {
    /// <summary>
    /// Document.xaml の相互作用ロジック
    /// </summary>
    public partial class Document : UserControl, ICustomTreeViewItem {
        //ICustomTreeViewItem
        public double LogicHeight => document.HeightProperty ?? 125;


        DocumentProperty document;


        public Document(DocumentProperty document) {
            InitializeComponent();

            DataContext = new DocumentPropertyViewModel(document);
            this.document = document;
        }
    }
}
