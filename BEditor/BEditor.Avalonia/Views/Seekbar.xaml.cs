using System;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace BEditor.Views
{
    public class Seekbar : UserControl
    {
        public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<Seekbar, double>("Maximum", 100d);
        public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<Seekbar, double>("Minimum", 0d);
        public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<Seekbar, double>("Value", 0d, defaultBindingMode: BindingMode.TwoWay);
        public static readonly StyledProperty<ICommand> PreviousProperty = AvaloniaProperty.Register<Seekbar, ICommand>("Previous");
        public static readonly StyledProperty<string> PreviousToolTipProperty = AvaloniaProperty.Register<Seekbar, string>("PreviousToolTip");
        public static readonly StyledProperty<ICommand> TopProperty = AvaloniaProperty.Register<Seekbar, ICommand>("Top");
        public static readonly StyledProperty<string> TopToolTipProperty = AvaloniaProperty.Register<Seekbar, string>("TopToolTip");
        public static readonly StyledProperty<ICommand> PlayPauseProperty = AvaloniaProperty.Register<Seekbar, ICommand>("PlayPause");
        public static readonly StyledProperty<string> PlayPauseToolTipProperty = AvaloniaProperty.Register<Seekbar, string>("PlayPauseToolTip");
        public static readonly StyledProperty<Geometry> PlayPauseIconProperty = AvaloniaProperty.Register<Seekbar, Geometry>("PlayPauseIcon", PathGeometry.Parse("M8,5.14V19.14L19,12.14L8,5.14Z"));
        public static readonly StyledProperty<ICommand> EndProperty = AvaloniaProperty.Register<Seekbar, ICommand>("End");
        public static readonly StyledProperty<string> EndToolTipProperty = AvaloniaProperty.Register<Seekbar, string>("EndToolTip");
        public static readonly StyledProperty<ICommand> NextProperty = AvaloniaProperty.Register<Seekbar, ICommand>("Next");
        public static readonly StyledProperty<string> NextToolTipProperty = AvaloniaProperty.Register<Seekbar, string>("NextToolTip");
        public static readonly StyledProperty<bool> IsPlayingProperty = AvaloniaProperty.Register<Seekbar, bool>("IsPlaying", notifying: IsPlayingChanging);

        private static void IsPlayingChanging(IAvaloniaObject arg1, bool arg2)
        {
            // play M8,5.14V19.14L19,12.14L8,5.14Z
            // pause M14,19H18V5H14M6,19H10V5H6V19Z

            var seek = (Seekbar)arg1;
            if (arg2)
            {
                seek.PlayPauseIcon = PathGeometry.Parse("M14,19H18V5H14M6,19H10V5H6V19Z");
            }
            else
            {
                seek.PlayPauseIcon = PathGeometry.Parse("M8,5.14V19.14L19,12.14L8,5.14Z");
            }
        }

        public Seekbar()
        {
            this.InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }


        public double Maximum
        {
            get => GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }
        public double Minimum
        {
            get => GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }
        public double Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
        public ICommand Previous
        {
            get => GetValue(PreviousProperty);
            set => SetValue(PreviousProperty, value);
        }
        public string PreviousToolTip
        {
            get => GetValue(PreviousToolTipProperty);
            set => SetValue(PreviousToolTipProperty, value);
        }
        public ICommand Top
        {
            get => GetValue(TopProperty);
            set => SetValue(TopProperty, value);
        }
        public string TopToolTip
        {
            get => GetValue(TopToolTipProperty);
            set => SetValue(TopToolTipProperty, value);
        }
        public ICommand PlayPause
        {
            get => GetValue(PlayPauseProperty);
            set => SetValue(PlayPauseProperty, value);
        }
        public string PlayPauseToolTip
        {
            get => GetValue(PlayPauseToolTipProperty);
            set => SetValue(PlayPauseToolTipProperty, value);
        }
        public Geometry PlayPauseIcon
        {
            get => GetValue(PlayPauseIconProperty);
            set => SetValue(PlayPauseIconProperty, value);
        }
        public ICommand End
        {
            get => GetValue(EndProperty);
            set => SetValue(EndProperty, value);
        }
        public string EndToolTip
        {
            get => GetValue(EndToolTipProperty);
            set => SetValue(EndToolTipProperty, value);
        }
        public ICommand Next
        {
            get => GetValue(NextProperty);
            set => SetValue(NextProperty, value);
        }
        public string NextToolTip
        {
            get => GetValue(NextToolTipProperty);
            set => SetValue(NextToolTipProperty, value);
        }

    }
}
