using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.ViewModels;
using BEditor.Views.ManagePlugins;

namespace BEditor.Views.VideoOutputPages
{
    public sealed class Video : UserControl
    {
        private readonly ContentPresenter _videoEncoderSettings;
        private bool _isSubscribed;

        public Video()
        {
            InitializeComponent();
            _videoEncoderSettings = this.FindControl<ContentPresenter>("VideoEncoderSettings");

        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_isSubscribed) return;

            if (DataContext is VideoOutputViewModel viewModel)
            {
                _isSubscribed = true;
                viewModel.VideoEncoderSettings.Subscribe(s =>
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

                viewModel.GetVideoSettings = () => Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var settings = viewModel.VideoEncoderSettings.Value;
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
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
