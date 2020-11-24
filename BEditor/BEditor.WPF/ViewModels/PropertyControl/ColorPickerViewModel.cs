using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.ViewModels.CustomControl;
using BEditor.ViewModels.Helper;

namespace BEditor.ViewModels.PropertyControl
{
    public class ColorPickerViewModel : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs brushArgs = new(nameof(Brush));
        
        public ColorPickerViewModel(ColorProperty property)
        {
            Property = property;
            Command = new(x =>
            {
                CommandManager.Do(new ColorProperty.ChangeColorCommand(Property, new(x.Item1, x.Item2, x.Item3, x.Item4)));
            });
            property.PropertyChanged += (s, e) =>
            {
                RaisePropertyChanged(brushArgs);
            };
            Reset = new(() =>
            {
                CommandManager.Do(new ColorProperty.ChangeColorCommand(Property, Property.PropertyMetadata.DefaultColor));
            });
        }
        
        public static ObservableCollection<ColorList> ColorList { get; } = new();
        public ColorProperty Property { get; }
        public DelegateCommand<(byte, byte, byte, byte)> Command { get; }
        public DelegateCommand Reset { get; }
        public SolidColorBrush Brush => new(Color.FromArgb(Property.Color.A, Property.Color.R, Property.Color.G, Property.Color.B));
    }
}
