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
    public class SelectorPropertyView : BasePropertyView
    {
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SelectorPropertyView));
        public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(nameof(DisplayMemberPath), typeof(string), typeof(SelectorPropertyView));
        public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(SelectorPropertyView));
        public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(SelectorPropertyView));

        static SelectorPropertyView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SelectorPropertyView), new FrameworkPropertyMetadata(typeof(SelectorPropertyView)));
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
        public int SelectedIndex
        {
            get => (int)GetValue(SelectedIndexProperty);
            set => SetValue(SelectedIndexProperty, value);
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

            box.SelectionChanged += Box_SelectionChanged;
        }

        private void Box_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Command?.Execute(((ComboBox)sender).SelectedIndex);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var box = (ComboBox)GetTemplateChild("box");

                if (box is null) return;

                box.SelectionChanged -= Box_SelectionChanged;
            }

            base.Dispose(disposing);
        }
    }
}