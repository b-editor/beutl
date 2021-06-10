using System;

using Avalonia.Controls;

using static BEditor.IMessage;

namespace BEditor.Views.DialogContent
{
    public interface IDialogContent : IContentControl
    {
        public ButtonType DialogResult { get; }

        public event EventHandler? ButtonClicked;
    }
}