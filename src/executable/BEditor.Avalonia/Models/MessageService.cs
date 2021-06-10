using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Threading;

using BEditor.Views;
using BEditor.Views.DialogContent;

namespace BEditor.Models
{
    public sealed class MessageService : IMessage
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
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                App.GetMainWindow().FindControl<StackPanel>("NotifyStack").Children.Add(new NotifyBar
                {
                    Content = text
                });
            });
        }
    }
}