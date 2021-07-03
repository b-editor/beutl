using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.ViewModels;
using BEditor.Views.ManagePlugins;

namespace BEditor.Views
{
    public class VideoOutput : FluentWindow
    {
        private readonly ContentPresenter _audioEncoderSettings;
        private readonly ContentPresenter _videoEncoderSettings;
        private readonly VideoOutputViewModel _viewModel;

        public VideoOutput()
        {
            _viewModel = new VideoOutputViewModel();
            DataContext = _viewModel;
            InitializeComponent();

            _viewModel.Output.Subscribe(Close);
            _audioEncoderSettings = this.FindControl<ContentPresenter>("AudioEncoderSettings");
            _videoEncoderSettings = this.FindControl<ContentPresenter>("VideoEncoderSettings");
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
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}