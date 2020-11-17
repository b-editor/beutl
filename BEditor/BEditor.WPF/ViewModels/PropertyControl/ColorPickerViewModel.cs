using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;

using BEditor.Core.Data;
using BEditor.Core.Data.PropertyData;
using BEditor.ViewModels.CustomControl;
using BEditor.ViewModels.Helper;

namespace BEditor.ViewModels.PropertyControl
{
    public class ColorPickerViewModel : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs brushArgs = new(nameof(Brush));
        public static ObservableCollection<ColorList> ColorList { get; } = new ObservableCollection<ColorList>();

        public ColorProperty Property { get; }
        public DelegateCommand<(byte, byte, byte, byte)> Command { get; }
        public SolidColorBrush Brush => new SolidColorBrush(Color.FromArgb(Property.Alpha, Property.Red, Property.Green, Property.Blue));

        public ColorPickerViewModel(ColorProperty property)
        {
            Property = property;
            Command = new DelegateCommand<(byte, byte, byte, byte)>(x =>
            {
                UndoRedoManager.Do(new ColorProperty.ChangeColorCommand(property, x.Item1, x.Item2, x.Item3, x.Item4));
            });
            property.PropertyChanged += (s, e) =>
            {
                RaisePropertyChanged(brushArgs);
            };
        }
    }
}
