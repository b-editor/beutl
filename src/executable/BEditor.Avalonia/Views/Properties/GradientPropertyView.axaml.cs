using System.Collections.Specialized;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public sealed class GradientPropertyView : UserControl
    {
        private readonly GradientProperty _property;
        private readonly StackPanel _stackPanel;

#pragma warning disable CS8618
        public GradientPropertyView()
#pragma warning restore CS8618
        {
            InitializeComponent();
        }

        public GradientPropertyView(GradientProperty property)
        {
            DataContext = new GradientPropertyViewModel(property);
            InitializeComponent();

            _stackPanel = this.FindControl<StackPanel>("stackPanel");
            _property = property;
            property.KeyPoints.CollectionChanged += KeyPoints_CollectionChanged;

            // StackPanel‚ÉNumeric‚ð’Ç‰Á
            foreach (var item in property.KeyPoints.Select((_, i) => CreateItem(i)))
            {
                _stackPanel.Children.Add(item);
            }
        }

        public void Add_Click(object s, RoutedEventArgs e)
        {
            static byte Func(float t, byte max, byte min)
            {
                return (byte)(((max - min) * t) + min);
            }

            var start = _property.KeyPoints[0];
            var end = _property.KeyPoints[1];
            var pos = (end.Position - start.Position) / 2;
            var color = Color.FromArgb(
                Func(pos, end.Color.A, start.Color.A),
                Func(pos, end.Color.R, start.Color.R),
                Func(pos, end.Color.G, start.Color.G),
                Func(pos, end.Color.B, start.Color.B));

            _property.AddPoint(new GradientKeyPoint(color, pos)).Execute();
        }

        private GradientEditor CreateItem(int index)
        {
            return new GradientEditor(_property, index);
        }

        private void ResetIndex()
        {
            for (var i = 0; i < _stackPanel.Children.Count; i++)
            {
                if (_stackPanel.Children[i] is GradientEditor obj)
                {
                    obj.Index = i;
                }
            }
        }

        private void KeyPoints_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add)
            {
                _stackPanel.Children.Insert(e.NewStartingIndex, CreateItem(e.NewStartingIndex));
                ResetIndex();
            }
            else if (e.Action is NotifyCollectionChangedAction.Remove)
            {
                _stackPanel.Children.RemoveAt(e.OldStartingIndex);
                ResetIndex();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
