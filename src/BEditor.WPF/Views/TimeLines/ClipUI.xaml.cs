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
    public partial class ClipUI : UserControl, ICustomTreeViewItem
    {
        public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register("IsExpanded", typeof(bool), typeof(ClipUI), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, IsExpandedChanged));
        private readonly Storyboard OpenStoryboard = new Storyboard();
        private readonly Storyboard CloseStoryboard = new Storyboard();
        private readonly DoubleAnimation OpenAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25) };
        private readonly DoubleAnimation CloseAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25), To = Setting.ClipHeight };


        public ClipUI(ClipElement clip)
        {
            DataContext = clip.GetCreateClipViewModel();
            InitializeComponent();
            ClipElement = clip;

            SetBinding(IsExpandedProperty, new Binding("IsExpanded.Value") { Mode = BindingMode.TwoWay });

            Unloaded += ClipUI_Unloaded;
            Loaded += ClipUI_Loaded;

            Height = ClipUIViewModel.TrackHeight;

            Storyboard.SetTarget(OpenAnm, this);
            Storyboard.SetTargetProperty(OpenAnm, new PropertyPath("(Height)"));

            Storyboard.SetTarget(CloseAnm, this);
            Storyboard.SetTargetProperty(CloseAnm, new PropertyPath("(Height)"));

            OpenStoryboard.Children.Add(OpenAnm);
            CloseStoryboard.Children.Add(CloseAnm);
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
        public ClipElement ClipElement { get; }
        public ClipUIViewModel ViewModel => (ClipUIViewModel)DataContext;
        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }


        private void ClipUI_Loaded(object sender, RoutedEventArgs e)
        {
            ClipElement.Effect.CollectionChanged += Value_SizeChange;
        }
        private void ClipUI_Unloaded(object sender, RoutedEventArgs e)
        {
            ClipElement.Effect.CollectionChanged -= Value_SizeChange;
        }
        private async void Value_SizeChange(object? sender, EventArgs? e)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (IsExpanded)
                {
                    OpenAnm.To = LogicHeight;

                    OpenStoryboard.Begin();
                }
                else
                {
                    CloseStoryboard.Begin();
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
                    clip.OpenAnm.To = clip.LogicHeight;

                    clip.OpenStoryboard.Begin();
                }
                else
                {
                    clip.CloseStoryboard.Begin();
                }
            }
        }
    }
}
