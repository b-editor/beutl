using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Layout;

namespace BEditor.Views.ManagePlugins
{
    public static class PluginSettingsUIBuilder
    {
        private static readonly (Func<string, Type, Control> create, Func<Control, Type, object> getValue, Action<Control, object> setValue, Type type)[] _typeTo =
        {
            #region Boolean

            ((header, _) => new Panel
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
            (ui, _) => ((ui as Panel)!.Children[0] as CheckBox)!.IsChecked!,
            (ui, value) => ((ui as Panel)!.Children[0] as CheckBox)!.IsChecked = (bool?)value,
            typeof(bool)),

            #endregion

            #region Byte

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
                            Maximum = byte.MaxValue,
                            Minimum = byte.MinValue,
                            Classes =
                            {
                                "SettingsNumericUpDown"
                            }
                        }
                    }
                };
            },
            (ui, _) => (byte)((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value,
            (ui, value) => ((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value = (byte)value,
            typeof(byte)),

            #endregion

            #region SByte

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
                            Maximum = sbyte.MaxValue,
                            Minimum = sbyte.MinValue,
                            Classes =
                            {
                                "SettingsNumericUpDown"
                            }
                        }
                    }
                };
            },
            (ui, _) => (sbyte)((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value,
            (ui, value) => ((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value = (sbyte)value,
            typeof(sbyte)),

            #endregion

            #region Int16

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
                            Maximum = short.MaxValue,
                            Minimum = short.MinValue,
                            Classes =
                            {
                                "SettingsNumericUpDown"
                            }
                        }
                    }
                };
            },
            (ui, _) => (short)((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value,
            (ui, value) => ((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value = (short)value,
            typeof(short)),

            #endregion

            #region UInt16

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
                            Maximum = ushort.MaxValue,
                            Minimum = ushort.MinValue,
                            Classes =
                            {
                                "SettingsNumericUpDown"
                            }
                        }
                    }
                };
            },
            (ui, _) => (ushort)((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value,
            (ui, value) => ((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value = (ushort)value,
            typeof(ushort)),

            #endregion

            #region Int32

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
                            Maximum = int.MaxValue,
                            Minimum = int.MinValue,
                            Classes =
                            {
                                "SettingsNumericUpDown"
                            }
                        }
                    }
                };
            },
            (ui, _) => (int)((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value,
            (ui, value) => ((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value = (int)value,
            typeof(int)),

            #endregion

            #region UInt32

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
                            Maximum = uint.MaxValue,
                            Minimum = uint.MinValue,
                            Classes =
                            {
                                "SettingsNumericUpDown"
                            }
                        }
                    }
                };
            },
            (ui, _) => (uint)((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value,
            (ui, value) => ((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value = (uint)value,
            typeof(uint)),

            #endregion

            #region Int64

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
                            Maximum = long.MaxValue,
                            Minimum = long.MinValue,
                            Classes =
                            {
                                "SettingsNumericUpDown"
                            }
                        }
                    }
                };
            },
            (ui, _) => (long)((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value,
            (ui, value) => ((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value = (long)value,
            typeof(long)),

            #endregion

            #region UInt64

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
                            Maximum = ulong.MaxValue,
                            Minimum = ulong.MinValue,
                            Classes =
                            {
                                "SettingsNumericUpDown"
                            }
                        }
                    }
                };
            },
            (ui, _) => (ulong)((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value,
            (ui, value) => ((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value = (ulong)value,
            typeof(ulong)),

            #endregion

            #region Char

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
                            MaxLength = 1,
                            Classes =
                            {
                                "SettingsTextBox"
                            }
                        }
                    }
                };
            },
            (ui, _) => ((ui as StackPanel)!.Children[1] as TextBox)!.Text[0],
            (ui, value) => ((ui as StackPanel)!.Children[1] as TextBox)!.Text = value.ToString(),
            typeof(char)),

            #endregion

            #region Double

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
            (ui, _) => (double)((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value,
            (ui, value) => ((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value = (double)value,
            typeof(double)),

            #endregion

            #region Single

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
                            Maximum = float.MaxValue,
                            Minimum = float.MinValue,
                            Classes =
                            {
                                "SettingsNumericUpDown"
                            }
                        }
                    }
                };
            },
            (ui, _) => (float)((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value,
            (ui, value) => ((ui as StackPanel)!.Children[1] as NumericUpDown)!.Value = (float)value,
            typeof(float)),

            #endregion

            #region DateTime

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
                        new DatePicker()
                    }
                };
            },
            (ui, _) => ((DateTimeOffset)((ui as StackPanel)!.Children[1] as DatePicker)!.SelectedDate!).DateTime,
            (ui, value) => ((ui as StackPanel)!.Children[1] as DatePicker)!.SelectedDate = new DateTimeOffset((DateTime)value),
            typeof(DateTime)),

            #endregion

            #region DateTimeOffset

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
                        new DatePicker()
                    }
                };
            },
            (ui, _) => (DateTimeOffset)((ui as StackPanel)!.Children[1] as DatePicker)!.SelectedDate!,
            (ui, value) => ((ui as StackPanel)!.Children[1] as DatePicker)!.SelectedDate = (DateTimeOffset?)value,
            typeof(DateTimeOffset)),

            #endregion

            #region Guid

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
            (ui, _) => Guid.TryParse(((ui as StackPanel)!.Children[1] as TextBox)!.Text, out var result) ? result : Guid.Empty,
            (ui, value) => ((ui as StackPanel)!.Children[1] as TextBox)!.Text = value.ToString(),
            typeof(Guid)),

            #endregion

            #region String

            ((header, _) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
            (ui, _) => ((ui as StackPanel)!.Children[1] as TextBox)!.Text,
            (ui, value) => ((ui as StackPanel)!.Children[1] as TextBox)!.Text = (string)value,
            typeof(string)),

            #endregion

            #region Enum

            ((header, type) =>
            {
                return new StackPanel
                {
                    Spacing = 0,
                    Orientation = Orientation.Vertical,
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
                        new ComboBox
                        {
                            Classes =
                            {
                                "SettingsComboBox"
                            },
                            Items = Enum.GetValues(type)
                        }
                    }
                };
            },
            (ui, _) => ((ui as StackPanel)!.Children[1] as ComboBox)!.SelectedItem!,
            (ui, value) => ((ui as StackPanel)!.Children[1] as ComboBox)!.SelectedItem = value,
            typeof(Enum)),

        	#endregion
        };

        public static StackPanel Create(object record)
        {
            var stack = new StackPanel
            {
                Spacing = 0,
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

                    var ui = item.create(name, param.ParameterType);
                    ToolTip.SetTip(ui, description);
                    ui.Margin = new(ui.Margin.Left, ui.Margin.Top, ui.Margin.Right, 16);

                    item.setValue(ui, prop.GetValue(record)!);

                    stack.Children.Add(ui);
                }
            }

            return stack;
        }
        public static StackPanel Create(Dictionary<string, object> dictonary)
        {
            var stack = new StackPanel
            {
                Spacing = 0,
                Orientation = Orientation.Vertical
            };

            foreach (var (key, value) in dictonary)
            {
                var type = value.GetType();
                var item = Find(value.GetType());

                var ui = item.create(key, type);
                ui.Margin = new(ui.Margin.Left, ui.Margin.Top, ui.Margin.Right, 16);

                item.setValue(ui, value);

                stack.Children.Add(ui);
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

                args[i] = item.getValue((Control)stack.Children[i], param.ParameterType);
            }

            return Activator.CreateInstance(type, BindingFlags.CreateInstance, null, args, null)!;
        }
        public static void GetValue(StackPanel stack, ref Dictionary<string, object> dictonary)
        {
            var result = new Dictionary<string, object>();
            var count = 0;
            foreach (var (key, value) in dictonary)
            {
                var type = value.GetType();
                var item = Find(type);

                result.Add(key, item.getValue((Control)stack.Children[count], type));
                count++;
            }
            dictonary = result;
        }

        private static (Func<string, Type, Control> create, Func<Control, Type, object> getValue, Action<Control, object> setValue, Type type) Find(Type type)
        {
            (Func<string, Type, Control> create, Func<Control, Type, object> getValue, Action<Control, object> setValue, Type type) result = default;

            Parallel.ForEach(_typeTo, v =>
            {
                if (type.IsAssignableTo(v.type)) result = v;
            });

            return result;
        }
    }
}