using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Data;

namespace BEditor.Views.DialogContent
{
    public partial class SelectObjectMetadata : FluentWindow
    {
        public SelectObjectMetadata()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public ObjectMetadata[] Metadatas
        {
            set => this.FindControl<ListBox>("List").Items = value;
            get => (ObjectMetadata[])this.FindControl<ListBox>("List").Items;
        }

        public ObjectMetadata? Selected
        {
            get => this.FindControl<ListBox>("List").SelectedItem as ObjectMetadata;
            set => this.FindControl<ListBox>("List").SelectedItem = value;
        }

        public void CancelClick(object s, RoutedEventArgs e)
        {
            Close(null);
        }

        public void AddClick(object s, RoutedEventArgs e)
        {
            Close(Selected);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}