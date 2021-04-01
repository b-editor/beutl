using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;

namespace BEditor.Views.DialogContent
{
    public class EmptyDialog : Window
    {
        public EmptyDialog(IDialogContent content) : this()
        {
            Content = content;

            content.ButtonClicked += (_, _) => Close();
        }
        public EmptyDialog(object content) : this()
        {
            Content = content;
        }
        public EmptyDialog()
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
        }
    }
}
