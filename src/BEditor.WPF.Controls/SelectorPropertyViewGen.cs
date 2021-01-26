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
    public class SelectorPropertyViewGen : BasePropertyView
    {
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SelectorPropertyViewGen));
        public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(nameof(DisplayMemberPath), typeof(string), typeof(SelectorPropertyViewGen));
        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(SelectorPropertyViewGen));
        public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(SelectorPropertyViewGen));

        static SelectorPropertyViewGen()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SelectorPropertyViewGen), new FrameworkPropertyMetadata(typeof(SelectorPropertyViewGen)));
        }

        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }
        public string DisplayMemberPath
        {
            get => (string)GetValue(DisplayMemberPathProperty);
            set => SetValue(DisplayMemberPathProperty, value);
        }
        public object SelectedItem
        {
            get => GetValue(SelectedItemProperty);
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
            var box = (ComboBox)GetTemplateChild("box");

            box.SelectionChanged += (s, _) => Command.Execute(((ComboBox)s).SelectedItem);
        }
    }
}
