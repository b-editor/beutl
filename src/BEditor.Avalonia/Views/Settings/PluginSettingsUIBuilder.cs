using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;

namespace BEditor.Views.Settings
{
    public static class PluginSettingsUIBuilder
    {
        private static readonly (Func<string, Control> create, Func<Control, object> getValue, Action<Control, object> setValue, Type type)[] TypeTo =
        {
            #region Float

            (header =>
            {
                return new StackPanel
                {
                    Children =
                    {
                        new Label
                        {
                            Content = header,
                            Classes =
                            {
                                "SettingsItemHeader"
                            }
                        },
                        new NumericUpDown
                        {
                            Classes =
                            {
                                "SettingsNumericUpDown"
                            }
                        }
                    }
                };
            },
            ui => (float)((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value,
            (ui, value) => ((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value = (float)value,
            typeof(float)),

            #endregion
    
            #region Int

            (header =>
            {
                return new StackPanel
                {
                    Children =
                    {
                        new Label
                        {
                            Content = header,
                            Classes =
                            {
                                "SettingsItemHeader"
                            }
                        },
                        new NumericUpDown
                        {
                            Classes =
                            {
                                "SettingsNumericUpDown"
                            }
                        }
                    }
                };
            },
            ui => (int)((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value,
            (ui, value) => ((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value = (int)value,
            typeof(int)),

            #endregion

            #region String

            (header =>
            {
                return new StackPanel
                {
                    Children =
                    {
                        new Label
                        {
                            Content = header,
                            Classes =
                            {
                                "SettingsItemHeader"
                            }
                        },
                        new TextBox
                        {
                            Classes =
                            {
                                "SettingsTextBox"
                            }
                        }
                    }
                };
            },
            ui => ((ui as StackPanel)!.Children[1] as TextBox)!.Text,
            (ui, value) => ((ui as StackPanel)!.Children[1] as TextBox)!.Text = (string)value,
            typeof(string)),

            #endregion

            #region Boolean

            (header => new Panel
            {
                Children =
                {
                    new CheckBox
                    {
                        Content = header,
                        Classes =
                        {
                            "SettingsCheckBox"
                        }
                    }
                }
            },
            ui => ((ui as Panel)!.Children[0] as CheckBox)!.IsChecked!,
            (ui, value) => ((ui as Panel)!.Children[0] as CheckBox)!.IsChecked = (bool?)value,
            typeof(bool)),

            #endregion
        };

        public static StackPanel Create(object record)
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };
            var type = record.GetType();
            var constructor = type.GetConstructors().First();

            foreach (var param in constructor.GetParameters())
            {
                var item = Find(param.ParameterType);
                var name = param.Name;
                var description = string.Empty;

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
                    ToolTip.SetTip(ui, description);
                    ui.Margin = new(ui.Margin.Left, ui.Margin.Top, ui.Margin.Right, 16);

                    item.setValue(ui, prop.GetValue(record)!);

                    stack.Children.Add(ui);
                }
            }

            return stack;
        }
        public static object GetValue(StackPanel stack, Type type)
        {
            var constructor = type.GetConstructors().First();
            var paramerters = constructor.GetParameters();
            var args = new object[paramerters.Length];

            for (var i = 0; i < paramerters.Length; i++)
            {
                var param = paramerters[i];
                var item = Find(param.ParameterType);

                args[i] = item.getValue((Control)stack.Children[i]);
            }

            return Activator.CreateInstance(type, BindingFlags.CreateInstance, null, args, null)!;
        }

        private static (Func<string, Control> create, Func<Control, object> getValue, Action<Control, object> setValue, Type type) Find(Type type)
        {
            (Func<string, Control> create, Func<Control, object> getValue, Action<Control, object> setValue, Type type) result = default;

            Parallel.ForEach(TypeTo, v =>
            {
                if (v.type == type) result = v;
            });

            return result;
        }
    }
}