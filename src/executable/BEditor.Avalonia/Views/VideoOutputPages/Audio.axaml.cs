using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.ViewModels;
using BEditor.Views.ManagePlugins;

namespace BEditor.Views.VideoOutputPages
{
    public partial class Audio : UserControl
    {
        private readonly ContentPresenter _audioEncoderSettings;
        private bool _isSubscribed;

        public Audio()
        {
            InitializeComponent();
            _audioEncoderSettings = this.FindControl<ContentPresenter>("AudioEncoderSettings");
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_isSubscribed) return;

            if (DataContext is VideoOutputViewModel viewModel)
            {
                _isSubscribed = true;
                viewModel.AudioEncoderSettings.Subscribe(s =>
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

                viewModel.GetAudioSettings = () => Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var settings = viewModel.AudioEncoderSettings.Value;
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
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
