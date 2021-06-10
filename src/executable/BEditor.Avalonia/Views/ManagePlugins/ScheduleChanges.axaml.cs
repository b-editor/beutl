using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;

namespace BEditor.Views.ManagePlugins
{
    public partial class ScheduleChanges : UserControl
    {
        public ScheduleChanges()
        {
            InitializeComponent();
        }

        protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromLogicalTree(e);
            if (DataContext is System.IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}