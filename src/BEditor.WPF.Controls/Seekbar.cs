using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using MaterialDesignThemes.Wpf;

namespace BEditor.WPF.Controls
{
    public class Seekbar : Control
    {
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register("Maximum", typeof(double), typeof(Seekbar), new(100d));
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register("Minimum", typeof(double), typeof(Seekbar), new(0d));
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(double), typeof(Seekbar), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public static readonly DependencyProperty PreviousProperty = DependencyProperty.Register("Previous", typeof(ICommand), typeof(Seekbar));
        public static readonly DependencyProperty PreviousToolTipProperty = DependencyProperty.Register("PreviousToolTip", typeof(string), typeof(Seekbar));
        public static readonly DependencyProperty TopProperty = DependencyProperty.Register("Top", typeof(ICommand), typeof(Seekbar));
        public static readonly DependencyProperty TopToolTipProperty = DependencyProperty.Register("TopToolTip", typeof(string), typeof(Seekbar));
        public static readonly DependencyProperty PlayPauseProperty = DependencyProperty.Register("PlayPause", typeof(ICommand), typeof(Seekbar));
        public static readonly DependencyProperty PlayPauseToolTipProperty = DependencyProperty.Register("PlayPauseToolTip", typeof(string), typeof(Seekbar));
        public static readonly DependencyProperty PlayPauseIconProperty = DependencyProperty.Register("PlayPauseIcon", typeof(PackIconKind), typeof(Seekbar));
        public static readonly DependencyProperty EndProperty = DependencyProperty.Register("End", typeof(ICommand), typeof(Seekbar));
        public static readonly DependencyProperty EndToolTipProperty = DependencyProperty.Register("EndToolTip", typeof(string), typeof(Seekbar));
        public static readonly DependencyProperty NextProperty = DependencyProperty.Register("Next", typeof(ICommand), typeof(Seekbar));
        public static readonly DependencyProperty NextToolTipProperty = DependencyProperty.Register("NextToolTip", typeof(string), typeof(Seekbar));

        static Seekbar()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(Seekbar), new FrameworkPropertyMetadata(typeof(Seekbar)));
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }
        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }
        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
        public ICommand Previous
        {
            get => (ICommand)GetValue(PreviousProperty);
            set => SetValue(PreviousProperty, value);
        }
        public string PreviousToolTip
        {
            get => (string)GetValue(PreviousToolTipProperty);
            set => SetValue(PreviousToolTipProperty, value);
        }
        public ICommand Top
        {
            get => (ICommand)GetValue(TopProperty);
            set => SetValue(TopProperty, value);
        }
        public string TopToolTip
        {
            get => (string)GetValue(TopToolTipProperty);
            set => SetValue(TopToolTipProperty, value);
        }
        public ICommand PlayPause
        {
            get => (ICommand)GetValue(PlayPauseProperty);
            set => SetValue(PlayPauseProperty, value);
        }
        public string PlayPauseToolTip
        {
            get => (string)GetValue(PlayPauseToolTipProperty);
            set => SetValue(PlayPauseToolTipProperty, value);
        }
        public PackIconKind PlayPauseIcon
        {
            get => (PackIconKind)GetValue(PlayPauseIconProperty);
            set => SetValue(PlayPauseIconProperty, value);
        }
        public ICommand End
        {
            get => (ICommand)GetValue(EndProperty);
            set => SetValue(EndProperty, value);
        }
        public string EndToolTip
        {
            get => (string)GetValue(EndToolTipProperty);
            set => SetValue(EndToolTipProperty, value);
        }
        public ICommand Next
        {
            get => (ICommand)GetValue(NextProperty);
            set => SetValue(NextProperty, value);
        }
        public string NextToolTip
        {
            get => (string)GetValue(NextToolTipProperty);
            set => SetValue(NextToolTipProperty, value);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            var slider = (Slider)GetTemplateChild("slider");

            slider.ValueChanged += (s, e) => Value = e.NewValue;
        }
    }
}