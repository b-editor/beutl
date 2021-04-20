using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Models;
using BEditor.Models.Extension;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;
using BEditor.Views.TimeLines;
using BEditor.WPF.Controls;

namespace BEditor.Views.PropertyControls
{
    /// <summary>
    /// EasePropertyControl.xaml の相互作用ロジック
    /// </summary>
    public sealed partial class EaseControl : UserControl, ICustomTreeViewItem, ISizeChangeMarker, IDisposable
    {
        private EaseProperty _property;
        private double _openHeight;
        private float _oldvalue;
        private readonly Storyboard _openStoryboard = new();
        private readonly Storyboard _closeStoryboard = new();
        private readonly DoubleAnimation _openAnm = new() { Duration = TimeSpan.FromSeconds(0.25) };
        private readonly DoubleAnimation _closeAnm = new() { Duration = TimeSpan.FromSeconds(0.25), To = 32.5 };

        public EaseControl(EaseProperty property)
        {
            DataContext = new EasePropertyViewModel(property);
            InitializeComponent();

            _property = property;

            _openHeight = (double)(_openAnm.To = 32.5 * property.Value.Count + 10);

            _property.Value.CollectionChanged += Value_CollectionChanged;

            _openStoryboard.Children.Add(_openAnm);
            _closeStoryboard.Children.Add(_closeAnm);

            Storyboard.SetTarget(_openAnm, this);
            Storyboard.SetTargetProperty(_openAnm, new PropertyPath("(Height)"));

            Storyboard.SetTarget(_closeAnm, this);
            Storyboard.SetTargetProperty(_closeAnm, new PropertyPath("(Height)"));
        }
        ~EaseControl()
        {
            Dispose();
        }


        public event EventHandler? SizeChange;


        public double LogicHeight
        {
            get
            {
                double h;
                if ((bool)togglebutton.IsChecked!)
                {
                    h = _openHeight;
                }
                else
                {
                    h = 32.5;
                }

                return h;
            }
        }


        private void Value_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove)
            {
                _openHeight = (double)(_openAnm.To = 32.5 * _property.Value.Count + 10);
                ListToggleClick(null, null);
            }
        }

        private void ListToggleClick(object? sender, RoutedEventArgs? e)
        {
            //開く
            if ((bool)togglebutton.IsChecked!)
            {
                _openStoryboard.Begin();
            }
            else
            {
                _closeStoryboard.Begin();
            }

            SizeChange?.Invoke(this, EventArgs.Empty);
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            var tb = (TextBox)sender;


            int index = AttachmentProperty.GetInt(tb);

            _oldvalue = _property.Value[index];
        }
        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            var tb = (TextBox)sender;

            int index = AttachmentProperty.GetInt(tb);

            if (float.TryParse(tb.Text, out var _out))
            {
                _property.Value[index] = _oldvalue;

                _property.ChangeValue(index, _out).Execute();
            }
        }
        private void TextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var textBox = (TextBox)sender;

            if (textBox.IsKeyboardFocused && float.TryParse(textBox.Text, out var val))
            {
                int index = AttachmentProperty.GetInt(textBox);

                int v = 10;//定数増え幅

                if (Keyboard.IsKeyDown(Key.LeftShift)) v = 1;

                val += e.Delta / 120 * v;

                _property.Value[index] = _property.Clamp(val);

                (AppData.Current.Project!).PreviewUpdate(_property.GetParent<ClipElement>()!);

                e.Handled = true;
            }
        }
        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            var textBox = (TextBox)sender;
            int index = AttachmentProperty.GetInt(textBox);


            if (float.TryParse(textBox.Text, out var _out))
            {
                _property.Value[index] = _property.Clamp(_out);

                (AppData.Current.Project!).PreviewUpdate(_property.GetParent<ClipElement>()!);
            }
        }

        public void Dispose()
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }

            DataContext = null;
            _property.Value.CollectionChanged -= Value_CollectionChanged;
            _property = null!;

            GC.SuppressFinalize(this);
        }
    }
}