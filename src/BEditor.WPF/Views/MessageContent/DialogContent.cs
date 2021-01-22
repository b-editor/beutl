using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Controls;

using BEditor.Core.Extensions;

namespace BEditor.Views.MessageContent
{
    public abstract class DialogContent : UserControl
    {
        public abstract ButtonType DialogResult { get; protected set; }
        public abstract event EventHandler? ButtonClicked;
    }
}
