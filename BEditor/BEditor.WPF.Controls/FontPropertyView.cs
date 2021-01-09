using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BEditor.WPF.Controls
{
    public class FontPropertyView : BasePropertyView
    {
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<Drawing.Font>), typeof(FontPropertyView));
        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(nameof(SelectedItem), typeof(Drawing.Font), typeof(FontPropertyView));
        public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(FontPropertyView));

        static FontPropertyView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(FontPropertyView), new FrameworkPropertyMetadata(typeof(FontPropertyView)));
        }

        public IEnumerable<Drawing.Font> ItemsSource
        {
            get => (IEnumerable<Drawing.Font>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }
        public Drawing.Font SelectedItem
        {
            get => (Drawing.Font)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }
        public ICommand Command
        {
            get => (ICommand)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            var box = GetTemplateChild("box") as ComboBox;
            box!.SelectionChanged += (s, _) => Command.Execute((s as ComboBox)?.SelectedItem);

            box.PreviewMouseDown += Box_PreviewMouseDown;
        }

        private void Box_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {

            //e.Handled = true;
        }
    }
}
