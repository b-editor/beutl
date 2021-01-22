using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BEditor.Views.SettingsControl
{
    public class UIBuilderFromRecord
    {
        private static (Func<string, FrameworkElement> create, Func<FrameworkElement, object> getValue, Action<FrameworkElement, object> setValue, Type type)[] TypeTo = new (Func<string, FrameworkElement> create, Func<FrameworkElement, object> getValue, Action<FrameworkElement, object> setValue, Type type)[]
        {
            #region Float

            (header =>
            {
                var label = new Label()
                {
                    Content = header
                };
                var text = new TextBox();

                label.Style = (Style)App.Current.Resources["SettingsHeader"];
                text.Style = (Style)App.Current.Resources["SettingsTextBox"];

                Grid.SetColumn(text, 1);

                var grid = new Grid()
                {
                    Children =
                    {
                        label,
                        text
                    },
                    ColumnDefinitions =
                    {
                        new() { Width = new(1, GridUnitType.Auto) },
                        new() { Width = new(1, GridUnitType.Star) },
                    }
                };

                return grid;
            },
            ui =>
            {
                var text = ((ui as Grid)!.Children[1] as TextBox)!.Text;

                if (float.TryParse(text, out var v)) return v;

                return 0f;
            },
            (ui, value) => ((ui as Grid)!.Children[1] as TextBox)!.Text = value.ToString(),
            typeof(float)),

            #endregion
    
            #region Int

            (header =>
            {
                var label = new Label()
                {
                    Content = header
                };
                var text = new TextBox();

                label.Style = (Style)App.Current.Resources["SettingsHeader"];
                text.Style = (Style)App.Current.Resources["SettingsTextBox"];

                Grid.SetColumn(text, 1);

                var grid = new Grid()
                {
                    Children =
                    {
                        label,
                        text
                    },
                    ColumnDefinitions =
                    {
                        new() { Width = new(1, GridUnitType.Auto) },
                        new() { Width = new(1, GridUnitType.Star) },
                    }
                };

                return grid;
            },
            ui =>
            {
                var text = ((ui as Grid)!.Children[1] as TextBox)!.Text;

                if (int.TryParse(text, out var v)) return v;

                return 0;
            },
            (ui, value) => ((ui as Grid)!.Children[1] as TextBox)!.Text = value.ToString(),
            typeof(int)),

            #endregion

            #region String
        
            (header =>
            {
                var label = new Label()
                {
                    Content = header
                };
                var text = new TextBox();

                label.Style = (Style)App.Current.Resources["SettingsHeader"];
                text.Style = (Style)App.Current.Resources["SettingsTextBox"];

                Grid.SetColumn(text, 1);

                var grid = new Grid()
                {
                    Children =
                    {
                        label,
                        text
                    },
                    ColumnDefinitions =
                    {
                        new() { Width = new(1, GridUnitType.Auto) },
                        new() { Width = new(1, GridUnitType.Star) },
                    }
                };

                return grid;
            },
            ui => ((ui as Grid)!.Children[1] as TextBox)!.Text,
            (ui, value) => ((ui as Grid)!.Children[1] as TextBox)!.Text = value.ToString(),
            typeof(string)),

            #endregion

            #region Boolean

            (header =>
            {
                var label = new TextBlock()
                {
                    Text = header
                };
                var text = new CheckBox();

                label.Style = (Style)App.Current.Resources["SettingsCheckBoxContent"];
                text.Style = (Style)App.Current.Resources["SettingsCheckBox"];

                var grid = new VirtualizingStackPanel()
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        text,
                        label,
                    }
                };

                return grid;
            },
            ui => ((ui as VirtualizingStackPanel)!.Children[0] as CheckBox)!.IsChecked!,
            (ui, value) => ((ui as VirtualizingStackPanel)!.Children[0] as CheckBox)!.IsChecked = (bool?)value,
            typeof(bool)),

	        #endregion
        };

        public static FrameworkElement Create(object record)
        {
            var stack = new VirtualizingStackPanel();
            var type = record.GetType();
            var constructor = type.GetConstructors().First();
            var paramerters = constructor.GetParameters();

            foreach (var param in paramerters)
            {
                var item = Find(param.ParameterType);
                var name = param.Name;
                var description = "";
                if (name is not null && type.GetProperty(name) is var prop && prop is not null)
                {
                    // 名前を取得
                    if (Attribute.GetCustomAttribute(prop, typeof(DisplayNameAttribute)) is DisplayNameAttribute attribute)
                    {
                        name = attribute.DisplayName;
                    }
                    if (Attribute.GetCustomAttribute(param, typeof(DescriptionAttribute)) is DescriptionAttribute dattribute)
                    {
                        description = dattribute.Description;
                    }

                    var ui = item.create(name);
                    ui.ToolTip = description;

                    item.setValue(ui, prop.GetValue(record)!);

                    stack.Children.Add(ui);
                }
            }

            return stack;
        }
        public static object GetValue(VirtualizingStackPanel stack, Type type)
        {
            var constructor = type.GetConstructors().First();
            var paramerters = constructor.GetParameters();
            var args = new object[paramerters.Length];

            for (int i = 0; i < paramerters.Length; i++)
            {
                var param = paramerters[i];
                var item = Find(param.ParameterType);

                var value = item.getValue((FrameworkElement)stack.Children[i]);
                args[i] = value;
            }

            return Activator.CreateInstance(type, BindingFlags.CreateInstance, null, args, null)!;
        }

        private static (Func<string, FrameworkElement> create, Func<FrameworkElement, object> getValue, Action<FrameworkElement, object> setValue, Type type) Find(Type type)
        {
            (Func<string, FrameworkElement> create, Func<FrameworkElement, object> getValue, Action<FrameworkElement, object> setValue, Type type) result = default;

            Parallel.ForEach(TypeTo, v =>
            {
                if (v.type == type) result = v;
            });

            return result;
        }
    }
}
