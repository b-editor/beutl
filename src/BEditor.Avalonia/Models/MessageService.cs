using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;

using BEditor.Properties;
using BEditor.Views.DialogContent;

namespace BEditor.Models
{
    public class MessageService : IMessage
    {
        public async ValueTask<IMessage.ButtonType?> DialogAsync(string text, IMessage.IconType icon = IMessage.IconType.Info, IMessage.ButtonType[]? types = null)
        {
            var task = Dispatcher.UIThread.InvokeAsync<IMessage.ButtonType?>(async () =>
            {
                var control = new MessageContent(types, text, icon);
                var dialog = new EmptyDialog(control);

                await dialog.ShowDialog(App.GetMainWindow());

                return control.DialogResult;
            });

            return await task;
        }

        public void Snackbar(string text = "")
        {
        }
    }
}