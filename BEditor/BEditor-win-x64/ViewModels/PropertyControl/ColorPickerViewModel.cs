using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

using BEditor.ViewModels.CustomControl;
using BEditor.ViewModels.Helper;

using BEditor.Core.Data;
using BEditor.Core.Data.PropertyData;

namespace BEditor.ViewModels.PropertyControl {
    public class ColorPickerViewModel : BasePropertyChanged {
        public static ObservableCollection<ColorList> ColorList { get; } = new ObservableCollection<ColorList>();

        public ColorProperty Property { get; }
        public DelegateCommand<(byte, byte, byte, byte)> Command { get; }
        public SolidColorBrush Brush => new SolidColorBrush(Color.FromArgb(Property.Alpha, Property.Red, Property.Green, Property.Blue));

        public ColorPickerViewModel(ColorProperty property) {
            Property = property;
            Command = new DelegateCommand<(byte, byte, byte, byte)>(x => {
                UndoRedoManager.Do(new ColorProperty.ChangeColor(property, x.Item1, x.Item2, x.Item3, x.Item4));
            });
            property.PropertyChanged += (s, e) => {
                RaisePropertyChanged(nameof(Brush));
            };
        }
    }
}
