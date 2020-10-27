using System.Collections.ObjectModel;
using System.Windows.Media;
using BEditor.ViewModels.PropertyControl;

namespace BEditor.ViewModels.CustomControl {
    public class ColorList : BasePropertyChanged {
        private string name;

        public ColorList(ObservableCollection<ColorListProperty> colors, string name) {
            Name = name;
            Colors = colors;
        }

        public string Name { get => name; set => SetValue(value, ref name, nameof(Name)); }
        public ObservableCollection<ColorListProperty> Colors { get; }
    }

    public class ColorListProperty : BasePropertyChanged {
        private byte red;
        private byte green;
        private byte blue;
        private string name;

        public ColorListProperty(byte r, byte g, byte b, string name) {
            red = r;
            green = g;
            blue = b;
            this.name = name;
        }

        public byte Red {
            get => red;
            set {
                SetValue(value, ref red, nameof(Red));
                RaiseEvent();
            }
        }
        public byte Green {
            get => green;
            set {
                SetValue(value, ref green, nameof(Green));
                RaiseEvent();
            }
        }
        public byte Blue {
            get => blue;
            set {
                SetValue(value, ref blue, nameof(Blue));
                RaiseEvent();
            }
        }

        public string Name { get => name; set => SetValue(value, ref name, nameof(Name)); }

        private void RaiseEvent() => base.RaisePropertyChanged(nameof(Brush));
        public Brush Brush => new SolidColorBrush(Color.FromRgb(Red, Green, Blue));
    }
}
