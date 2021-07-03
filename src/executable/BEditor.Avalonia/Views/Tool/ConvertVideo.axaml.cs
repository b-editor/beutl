using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Media;
using BEditor.Media.Decoding;
using BEditor.Models;
using BEditor.Models.Tool;
using BEditor.Properties;
using BEditor.ViewModels.Tool;
using BEditor.Views.ManagePlugins;

namespace BEditor.Views.Tool
{
    public partial class ConvertVideo : FluentWindow
    {
        private readonly ContentPresenter _audioEncoderSettings;
        private readonly ContentPresenter _videoEncoderSettings;
        private ConvertVideoViewModel? _viewModel;

        public ConvertVideo()
        {
            InitializeComponent();
            _audioEncoderSettings = this.FindControl<ContentPresenter>("AudioEncoderSettings");
            _videoEncoderSettings = this.FindControl<ContentPresenter>("VideoEncoderSettings");
#if DEBUG
            this.AttachDevTools();
#endif
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            var record = new OpenFileRecord
            {
                Filters =
                {
                    new FileFilter(Strings.VideoFile, DecodingRegistory.EnumerateDecodings()
                    .SelectMany(i => i.SupportExtensions())
                    .Distinct()
                    .Select(i => i.Trim('.'))
                    .ToArray())
                },
            };
            var app = AppModel.Current;

            if (await app.FileDialog.ShowOpenFileDialogAsync(record))
            {
                try
                {
                    using var file = MediaFile.Open(record.FileName);

                    if (file.Video is null || file.Audio is null)
                    {
                        await app.Message.DialogAsync(Strings.ThisFileCannotBeConverted, IMessage.IconType.Info);
                        Close();
                        return;
                    }

                    var videoInfo = new ConvertVideoSource(file.Video.Info, file.Audio.Info, record.FileName);

                    DataContext = _viewModel = new ConvertVideoViewModel(videoInfo);

                    _viewModel.Output.Subscribe(() =>
                    {
                        if (VisualRoot is Window win)
                        {
                            win.Close();
                        }
                    });
                    _viewModel.AudioEncoderSettings.Subscribe(s =>
                    {
                        if (s is null)
                        {
                            _audioEncoderSettings.Content = null;
                        }
                        else
                        {
                            _audioEncoderSettings.Content = PluginSettingsUIBuilder.Create(s);
                        }
                    });
                    _viewModel.VideoEncoderSettings.Subscribe(s =>
                    {
                        if (s is null)
                        {
                            _videoEncoderSettings.Content = null;
                        }
                        else
                        {
                            _videoEncoderSettings.Content = PluginSettingsUIBuilder.Create(s);
                        }
                    });

                    _viewModel.GetAudioSettings = () => Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var settings = _viewModel.AudioEncoderSettings.Value;
                        if (_audioEncoderSettings.Content is StackPanel stack && settings is not null)
                        {
                            PluginSettingsUIBuilder.GetValue(stack, ref settings);
                            return settings;
                        }
                        else
                        {
                            return null;
                        }
                    });

                    _viewModel.GetVideoSettings = () => Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var settings = _viewModel.VideoEncoderSettings.Value;
                        if (_videoEncoderSettings.Content is StackPanel stack && settings is not null)
                        {
                            PluginSettingsUIBuilder.GetValue(stack, ref settings);
                            return settings;
                        }
                        else
                        {
                            return null;
                        }
                    });
                }
                catch
                {
                    await app.Message.DialogAsync(Strings.ThisFileCannotBeConverted, IMessage.IconType.Info);
                    Close();
                }
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}