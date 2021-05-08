using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;

using BEditor.Data;

namespace BEditor.ViewModels.CustomControl
{
    public class ColorList : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs nameArgs = new(nameof(Name));
        private string name = "";

        public ColorList(ObservableCollection<ColorListProperty> colors, string name)
        {
            Name = name;
            Colors = colors;
        }

        public string Name
        {
            get => name;
            set => SetValue(value, ref name, nameArgs);
        }
        public ObservableCollection<ColorListProperty> Colors { get; }
    }

    public class ColorListProperty : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs redArgs = new(nameof(Red));
        private static readonly PropertyChangedEventArgs greenArgs = new(nameof(Green));
        private static readonly PropertyChangedEventArgs blueArgs = new(nameof(Blue));
        private static readonly PropertyChangedEventArgs nameArgs = new(nameof(Name));
        private static readonly PropertyChangedEventArgs brushArgs = new(nameof(Brush));
        private byte red;
        private byte green;
        private byte blue;
        private string name;

        public ColorListProperty(byte r, byte g, byte b, string name)
        {
            red = r;
            green = g;
            blue = b;
            this.name = name;
        }

        public byte Red
        {
            get => red;
            set
            {
                SetValue(value, ref red, redArgs);
                RaiseEvent();
            }
        }
        public byte Green
        {
            get => green;
            set
            {
                SetValue(value, ref green, greenArgs);
                RaiseEvent();
            }
        }
        public byte Blue
        {
            get => blue;
            set
            {
                SetValue(value, ref blue, blueArgs);
                RaiseEvent();
            }
        }

        public string Name
        {
            get => name;
            set => SetValue(value, ref name, nameArgs);
        }

        private void RaiseEvent() => base.RaisePropertyChanged(brushArgs);
        public Brush Brush => new SolidColorBrush(Color.FromRgb(Red, Green, Blue));
    }
}