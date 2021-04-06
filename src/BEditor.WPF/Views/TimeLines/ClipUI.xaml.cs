using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Animation;

using BEditor.Data;
using BEditor.Models;
using BEditor.ViewModels.TimeLines;
using BEditor.WPF.Controls;

namespace BEditor.Views.TimeLines
{
    /// <summary>
    /// ClipUI.xaml の相互作用ロジック
    /// </summary>
    public sealed partial class ClipUI : UserControl, ICustomTreeViewItem, IDisposable
    {
        public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register("IsExpanded", typeof(bool), typeof(ClipUI), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, IsExpandedChanged));
        private readonly Storyboard _openStoryboard = new();
        private readonly Storyboard _closeStoryboard = new();
        private readonly DoubleAnimation _openAnm = new() { Duration = TimeSpan.FromSeconds(0.25) };


        public ClipUI(ClipElement clip)
        {
            DataContext = clip.GetCreateClipViewModel();
            InitializeComponent();

            SetBinding(IsExpandedProperty, new Binding("IsExpanded.Value") { Mode = BindingMode.TwoWay });

            ClipElement.Effect.CollectionChanged += Value_SizeChange;

            Height = ClipUIViewModel.TrackHeight;

            {
                var closeAnm = new DoubleAnimation()
                {
                    Duration = TimeSpan.FromSeconds(0.25),
                    To = Setting.ClipHeight
                };

                Storyboard.SetTarget(_openAnm, this);
                Storyboard.SetTargetProperty(_openAnm, new PropertyPath("(Height)"));

                Storyboard.SetTarget(closeAnm, this);
                Storyboard.SetTargetProperty(closeAnm, new PropertyPath("(Height)"));

                _openStoryboard.Children.Add(_openAnm);
                _closeStoryboard.Children.Add(closeAnm);
            }
        }
        ~ClipUI()
        {
            Dispose();
        }


        public double LogicHeight
        {
            get
            {
                double tmp = Setting.ClipHeight;

                foreach (var effect in ClipElement.Effect)
                {
                    var control = effect.GetCreateKeyFrameView();
                    tmp += control.LogicHeight;

                    control.SizeChange -= Value_SizeChange;

                    control.SizeChange += Value_SizeChange;
                }

                return tmp;
            }
        }
        public ClipElement ClipElement => ViewModel.ClipElement;
        public ClipUIViewModel ViewModel => (ClipUIViewModel)DataContext;
        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }


        private async void Value_SizeChange(object? sender, EventArgs? e)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (IsExpanded)
                {
                    _openAnm.To = LogicHeight;

                    _openStoryboard.Begin();
                }
                else
                {
                    _closeStoryboard.Begin();
                }
            });
        }
        private static void IsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue == e.OldValue) return;

            if (d is ClipUI clip)
            {
                if (clip.IsExpanded)
                {
                    clip._openAnm.To = clip.LogicHeight;

                    clip._openStoryboard.Begin();
                }
                else
                {
                    clip._closeStoryboard.Begin();
                }
            }
        }
        public void Dispose()
        {
            ClipElement.Effect.CollectionChanged -= Value_SizeChange;
            DataContext = null;

            GC.SuppressFinalize(this);
        }
    }
}
