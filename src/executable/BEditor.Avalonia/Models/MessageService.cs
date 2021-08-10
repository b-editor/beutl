using System;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Threading;

using BEditor.Views;
using BEditor.Views.DialogContent;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;

using Reactive.Bindings;

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
            Snackbar(text, string.Empty);
        }

        public void Snackbar(
            string text,
            string title,
            IMessage.IconType icon = IMessage.IconType.Info,
            Action? close = null,
            Action<object?>? action = null,
            string actionName = "",
            object? parameter = null)
        {
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var window = App.GetMainWindow();
                if (window is MainWindow main)
                {
                    var info = new InfoBar
                    {
                        Title = title,
                        Message = text,
                        IsOpen = true,
                        Severity = icon switch
                        {
                            IMessage.IconType.Info => InfoBarSeverity.Informational,
                            IMessage.IconType.None => InfoBarSeverity.Informational,
                            IMessage.IconType.Error => InfoBarSeverity.Error,
                            IMessage.IconType.Success => InfoBarSeverity.Success,
                            IMessage.IconType.Warning => InfoBarSeverity.Warning,
                            _ => InfoBarSeverity.Informational,
                        },
                    };

                    if (close != null)
                    {
                        var closeCommand = new ReactiveCommand();
                        closeCommand.Subscribe(close);
                        info.CloseButtonCommand = closeCommand;
                    }

                    if (action != null)
                    {
                        var actionCommand = new ReactiveCommand<object?>();
                        actionCommand.Subscribe(action);
                        info.ActionButton = new Avalonia.Controls.Button
                        {
                            Command = actionCommand,
                            CommandParameter = parameter,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Content = actionName ?? "Action"
                        };
                    }

                    main._notifications.Children.Add(info);

                    await Task.Delay(5000);

                    main._notifications.Children.Remove(info);

                    if (info.IsOpen)
                    {
                        main._stackNotifications.Children.Add(info);

                        info.Closed += (s, _) =>
                        {
                            if (s is InfoBar infoBar
                                && infoBar.Parent is Panel panel)
                            {
                                panel.Children.Remove(infoBar);
                            }
                        };
                    }
                }
            });
        }
    }
}