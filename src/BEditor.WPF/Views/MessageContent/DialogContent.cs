using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Controls;

namespace BEditor.Views.MessageContent
{
    public abstract class DialogContent : UserControl
    {
        public abstract IMessage.ButtonType DialogResult { get; protected set; }
        public abstract event EventHandler? ButtonClicked;
    }
}