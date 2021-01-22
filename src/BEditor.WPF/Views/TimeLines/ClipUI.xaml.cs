using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Animation;

using BEditor.Core.Data;
using BEditor.Models;
using BEditor.ViewModels.TimeLines;
using BEditor.Views.CustomControl;
using BEditor.WPF.Controls;

using CustomTreeView = BEditor.WPF.Controls.ExpandTree;

namespace BEditor.Views.TimeLines
{

    /// <summary>
    /// ClipUI.xaml の相互作用ロジック
    /// </summary>
    public partial class ClipUI : UserControl, ICustomTreeViewItem
    {
        public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register("IsExpanded", typeof(bool), typeof(ClipUI), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, IsExpandedChanged));


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

        public ClipUI(ClipData _Data)
        {
            DataContext = _Data.GetCreateClipViewModel();
            InitializeComponent();
            ClipData = _Data;

            SetBinding(IsExpandedProperty, new Binding("IsExpanded.Value") { Mode = BindingMode.TwoWay });

            _Data.Effect.CollectionChanged += (s, e) =>
            {
                Value_SizeChange(this, null);
            };

            Height = ViewModel.TrackHeight;

            Storyboard.SetTarget(OpenAnm, this);
            Storyboard.SetTargetProperty(OpenAnm, new PropertyPath("(Height)"));

            Storyboard.SetTarget(CloseAnm, this);
            Storyboard.SetTargetProperty(CloseAnm, new PropertyPath("(Height)"));

            OpenStoryboard.Children.Add(OpenAnm);
            CloseStoryboard.Children.Add(CloseAnm);
        }

        #region ICustomTreeViewItem
        public double LogicHeight
        {
            get
            {
                double tmp = Setting.ClipHeight;

                foreach (var effect in ClipData.Effect)
                {
                    var control = (CustomTreeView)effect.GetCreateKeyFrameView();
                    tmp += control.LogicHeight;

                    control.SizeChange -= Value_SizeChange;

                    control.SizeChange += Value_SizeChange;
                }

                return tmp;
            }
        }

        private void Value_SizeChange(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
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
        #endregion

        public ClipData ClipData { get; set; }
        public ClipUIViewModel ViewModel => (ClipUIViewModel)DataContext;


        #region Storyboard

        internal Storyboard OpenStoryboard = new Storyboard();
        internal Storyboard CloseStoryboard = new Storyboard();
        internal DoubleAnimation OpenAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25) };
        internal DoubleAnimation CloseAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25), To = Setting.ClipHeight };

        #endregion


        /// <summary>
        /// 開いている場合True
        /// </summary>
        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }
    }
}
