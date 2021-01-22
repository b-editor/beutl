using System;
using System.Collections.Specialized;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Models;
using BEditor.ViewModels.PropertyControl;

namespace BEditor.Views.Properties
{
    public class EasePropertyView : UserControl
    {
        private readonly EaseProperty property;
        private float oldvalue;
        private double OpenHeight;
        private readonly Animation OpenAnm = new()
        {
            Duration = TimeSpan.FromSeconds(0.25),
            Children =
            {
                new()
                {
                    Setters =
                    {
                        new Setter()
                        {
                            Property = HeightProperty,
                            Value = 32.5d
                        }
                    },
                    Cue = new(0)
                },
                new()
                {
                    Setters =
                    {
                        new Setter()
                        {
                            Property = HeightProperty,
                            Value = 42.5d
                        }
                    },
                    Cue = new(1)
                }
            }
        };
        private readonly Animation CloseAnm = new()
        {
            Duration = TimeSpan.FromSeconds(0.25),
            Children =
            {
                new()
                {
                    Setters =
                    {
                        new Setter()
                        {
                            Property = HeightProperty,
                            Value = 42.5d
                        }
                    },
                    Cue = new(0)
                },
                new()
                {
                    Setters =
                    {
                        new Setter()
                        {
                            Property = HeightProperty,
                            Value = 32.5d
                        }
                    },
                    Cue = new(1)
                }
            }
        };


#pragma warning disable CS8618 // null 非許容のフィールドには、コンストラクターの終了時に null 以外の値が入っていなければなりません。Null 許容として宣言することをご検討ください。
        public EasePropertyView()
#pragma warning restore CS8618 // null 非許容のフィールドには、コンストラクターの終了時に null 以外の値が入っていなければなりません。Null 許容として宣言することをご検討ください。
        {
            this.InitializeComponent();
        }
        public EasePropertyView(EaseProperty property)
        {
            DataContext = new EasePropertyViewModel(property);
            InitializeComponent();

            this.property = property;

            this.property.Value.CollectionChanged += Value_CollectionChanged;
            OpenHeight = (double)(OpenAnm.Children[1].Setters[0].Value = (32.5 * property.Value.Count) + 10);
            CloseAnm.Children[0].Setters[0].Value = OpenHeight;
        }

        private ToggleButton togglebutton => this.FindControl<ToggleButton>("togglebutton");

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        public void PopClick(object sender, RoutedEventArgs args)
        {
            this.FindControl<Popup>("Pop")?.Open();
        }

        private void Value_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove)
            {
                OpenHeight = (double)(OpenAnm.Children[1].Setters[0].Value = (32.5 * property.Value.Count) + 10);
                ListToggleClick(null, null);
            }
        }

        private async void ListToggleClick(object? sender, RoutedEventArgs? e)
        {
            //開く
            if (togglebutton.IsChecked ?? true)
            {
                await OpenAnm.RunAsync(this);
                Height = OpenHeight;
            }
            else
            {
                await CloseAnm.RunAsync(this);
                Height = 32.5;
            }
        }

        public void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var tb = (TextBox)sender;


            int index = AttachmentProperty.GetInt(tb);

            oldvalue = property.Value[index];
        }
        public void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var tb = (TextBox)sender;

            int index = AttachmentProperty.GetInt(tb);

            if (float.TryParse(tb.Text, out float _out))
            {
                property.Value[index] = oldvalue;

                Core.Command.CommandManager.Do(new EaseProperty.ChangeValueCommand(property, index, _out));
            }
        }
        public void TextBox_MouseWheel(object sender, PointerWheelEventArgs e)
        {
            TextBox textBox = (TextBox)sender;

            if (textBox.IsFocused && float.TryParse(textBox.Text, out var val))
            {
                int index = AttachmentProperty.GetInt(textBox);

                int v = 10;//定数増え幅

                //if (Keyboard.IsKeyDown(Key.LeftShift)) v = 1;

                val += (float)e.Delta.Y / 120 * v;

                property.Value[index] = property.Clamp(val);

                //Todo: AppData.Current.Project.PreviewUpdate(property.GetParent2());

                e.Handled = true;
            }
        }
        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox textBox = (TextBox)sender;
            int index = AttachmentProperty.GetInt(textBox);


            if (float.TryParse(textBox.Text, out float _out))
            {
                property.Value[index] = property.Clamp(_out);

                //Todo: AppData.Current.Project.PreviewUpdate(property.GetParent2());
            }
        }
    }
}
