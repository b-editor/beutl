using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.ViewModels;
using BEditor.Views;
using BEditor.Views.MessageContent;

namespace BEditor.Models.Services
{
    public class MessageService : IMessage
    {
        public async ValueTask<IMessage.ButtonType?> DialogAsync(string text, IMessage.IconType icon = IMessage.IconType.Info, IMessage.ButtonType[]? types = null)
        {
            return await System.Windows.Application.Current.Dispatcher.InvokeAsync<IMessage.ButtonType?>(() =>
            {
                var control = new MessageUI(types, text, icon);
                var dialog = new NoneDialog(control);

                dialog.ShowDialog();

                return control.DialogResult;
            });
        }

        public void Snackbar(string text = "")
        {
            MainWindowViewModel.Current.MessageQueue.Enqueue(text);
        }
    }
}
