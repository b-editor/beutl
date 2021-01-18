using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Properties;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views.Properties;

namespace BEditor.Views
{
    public static class ViewBuilder
    {
        public static List<PropertyViewBuilder> PropertyViewBuilders { get; } = new()
        {
            // ExpandGroup
            PropertyViewBuilder.Create<ExpandGroup>(group =>
            {
                var _settingcontrol = new Expander()
                {
                    Header = new TextBlock()
                    {
                        Text = group.PropertyMetadata.Name,
                        Foreground = Brushes.White
                    },
                };

                var stack = new StackPanel();

                var margin = new Thickness(32.5, 0, 0, 0);

                foreach (var item in group.Children)
                {
                    var content = item.GetCreatePropertyView();
                    content.Margin = margin;

                    stack.Children.Add(content);
                }

                _settingcontrol.Content = stack;

                _settingcontrol.Bind(Expander.IsExpandedProperty, new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = group });

                return _settingcontrol;
            }),
            // Group
            PropertyViewBuilder.Create<Group>(group =>
            {
                var stack = new StackPanel();

                foreach (var item in group.Children)
                {
                    var content = item.GetCreatePropertyView();

                    stack.Children.Add(content);
                }

                return stack;
            }),
            // SelectorProperty
            PropertyViewBuilder.Create<SelectorProperty>(s =>
            {
                return new SelectorPropertyView(new SelectorPropertyViewModel(s));
            })
        };

        public static Control GetCreatePropertyView(this PropertyElement property)
        {
            if (!property.ComponentData.ContainsKey("GetPropertyView"))
            {
                var type = property.GetType();
                var func = PropertyViewBuilders.Find(x => type == x.PropertyType || type.IsSubclassOf(x.PropertyType));

                property.ComponentData.Add("GetPropertyView", func?.CreateFunc(property) ?? new TextBlock() { Text = $"{property.GetType().Name} is not supported on this platform.\nUse the WPF platform." });
            }
            return property.ComponentData["GetPropertyView"];
        }
        //public static UIElement GetCreateKeyFrameView(this IKeyFrameProperty property)
        //{
        //    if (!property.ComponentData.ContainsKey("GetKeyFrameView"))
        //    {
        //        var type = property.GetType();
        //        var func = KeyFrameViewBuilders.Find(x => type == x.PropertyType || type.IsSubclassOf(x.PropertyType));

        //        property.ComponentData.Add("GetKeyFrameView", func.CreateFunc?.Invoke(property));
        //    }
        //    return property.ComponentData["GetKeyFrameView"];
        //}
        //public static ClipUI GetCreateClipView(this ClipData clip)
        //{
        //    if (!clip.ComponentData.ContainsKey("GetClipView"))
        //    {
        //        clip.ComponentData.Add("GetClipView", new ClipUI(clip)
        //        {
        //            Name = clip.Name,
        //            HorizontalAlignment = HorizontalAlignment.Left,
        //            VerticalAlignment = VerticalAlignment.Top
        //        });
        //    }
        //    return clip.ComponentData["GetClipView"];
        //}
        //public static ClipUIViewModel GetCreateClipViewModel(this ClipData clip)
        //{
        //    if (!clip.ComponentData.ContainsKey("GetClipViewModel"))
        //    {
        //        clip.ComponentData.Add("GetClipViewModel", new ClipUIViewModel(clip));
        //    }
        //    return clip.ComponentData["GetClipViewModel"];
        //}
        public static Control GetCreatePropertyView(this EffectElement effect)
        {
            if (!effect.ComponentData.ContainsKey("GetControl"))
            {
                var (expander, stack) = (effect is ObjectElement @object) ? CreateTreeObject(@object) : CreateTreeEffect(effect);

                foreach (var item in effect.Children)
                {
                    var tmp = item.GetCreatePropertyView();

                    stack.Children.Add(tmp);
                }

                effect.ComponentData.Add("GetControl", expander);
            }
            return effect.ComponentData["GetControl"];
        }
        //public static UIElement GetCreateKeyFrameView(this EffectElement effect)
        //{
        //    if (!effect.ComponentData.ContainsKey("GetKeyFrame"))
        //    {
        //        var keyFrame = new ExpandTree() { HeaderHeight = Setting.ClipHeight + 1 };

        //        var stack = new VirtualizingStackPanel();
        //        VirtualizingPanel.SetIsVirtualizing(stack, true);
        //        VirtualizingPanel.SetVirtualizationMode(stack, VirtualizationMode.Recycling);

        //        keyFrame.Content = stack;

        //        var binding = new Binding("ActualWidth") { Mode = BindingMode.OneWay, Source = keyFrame };

        //        foreach (var item in effect.Children)
        //        {
        //            if (item is IKeyFrameProperty e)
        //            {

        //                var tmp = e.GetCreateKeyFrameView();
        //                (tmp as FrameworkElement)?.SetBinding(FrameworkElement.WidthProperty, binding);
        //                stack.Children.Add(tmp);
        //            }
        //        }

        //        keyFrame.SetBinding(ExpandTree.HeaderProperty, new Binding("Name") { Mode = BindingMode.OneTime, Source = effect });
        //        keyFrame.SetBinding(ExpandTree.IsExpandedProperty, new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = effect });

        //        //エクスパンダーをアップデート
        //        keyFrame.ExpanderUpdate();

        //        effect.ComponentData.Add("GetKeyFrame", keyFrame);
        //    }
        //    return effect.ComponentData["GetKeyFrame"];
        //}
        public static ClipProperty GetCreatePropertyView(this ClipData clip)
        {
            if (!clip.ComponentData.ContainsKey("GetPropertyView"))
            {
                clip.ComponentData.Add("GetPropertyView", new ClipProperty(clip));
            }
            return clip.ComponentData["GetPropertyView"];
        }
        //public static TimeLine GetCreateTimeLineView(this Scene scene)
        //{
        //    if (!scene.ComponentData.ContainsKey("GetTimeLine"))
        //    {
        //        scene.ComponentData.Add("GetTimeLine", new TimeLine(scene));
        //    }
        //    return scene.ComponentData["GetTimeLine"];
        //}
        //public static TimeLineViewModel GetCreateTimeLineViewModel(this Scene scene)
        //{
        //    if (!scene.ComponentData.ContainsKey("GetTimeLineViewModel"))
        //    {
        //        scene.ComponentData.Add("GetTimeLineViewModel", new TimeLineViewModel(scene));
        //    }
        //    return scene.ComponentData["GetTimeLineViewModel"];
        //}
        public static PropertyTab GetCreatePropertyTab(this Scene scene)
        {
            if (!scene.ComponentData.ContainsKey("GetPropertyTab"))
            {
                scene.ComponentData.Add("GetPropertyTab", new PropertyTab(scene));
            }
            return scene.ComponentData["GetPropertyTab"];
        }
        //public static UIElement GetCreatePropertyView(this EasingFunc easing)
        //{
        //    if (!easing.ComponentData.ContainsKey("GetPropertyView"))
        //    {
        //        var _createdControl = new VirtualizingStackPanel()
        //        {
        //            Orientation = Orientation.Vertical,
        //            Width = float.NaN,
        //            HorizontalAlignment = HorizontalAlignment.Stretch
        //        };

        //        foreach (var setting in easing.Children)
        //        {
        //            _createdControl.Children.Add(((PropertyElement)setting).GetCreatePropertyView());
        //        }

        //        easing.ComponentData.Add("GetPropertyView", _createdControl);
        //    }
        //    return easing.ComponentData["GetPropertyView"];
        //}

        public static (Expander, StackPanel) CreateTreeObject(ObjectElement obj)
        {
            var expander = new Expander()
            {
                Padding = new(32.5, 0, 0, 0)
            };
            var stack = new StackPanel();

            var header = new VirtualizingStackPanel() { Orientation = Orientation.Horizontal };

            expander.Header = header;

            var checkBox = new CheckBox()
            {
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var textBlock = new TextBlock()
            {
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };

            header.Children.Add(checkBox);
            header.Children.Add(textBlock);


            #region コンテキストメニュー
            var menuListBox = new ContextMenu();
            var Delete = new MenuItem()
            {
                Header = new VirtualizingStackPanel()
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new PathIcon()
                        {
                            Data = PathGeometry.Parse("M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z"),
                            Margin = new Thickness(5, 0, 5, 0)
                        },
                        new TextBlock()
                        {
                            Text = Resources.Remove,
                            Margin = new Thickness(20, 0, 5, 0)
                        }
                    }
                }
            };

            menuListBox.Items = new MenuItem[] { Delete };
            expander.ContextMenu = menuListBox;

            #endregion

            #region イベント
            checkBox.Click += (sender, e) =>
            {
                obj.CreateCheckCommand((bool)((CheckBox)sender!).IsChecked!).Execute();
            };

            #endregion

            #region Binding

            var isenablebinding = new Binding("IsEnabled")
            {
                Mode = BindingMode.OneWay,
                Source = obj
            };
            checkBox.Bind(ToggleButton.IsCheckedProperty, isenablebinding);

            var textbinding = new Binding("Name")
            {
                Mode = BindingMode.OneTime,
                Source = obj
            };
            textBlock.Bind(TextBlock.TextProperty, textbinding);

            var isExpandedbinding = new Binding("IsExpanded")
            {
                Mode = BindingMode.TwoWay,
                Source = obj
            };
            expander.Bind(Expander.IsExpandedProperty, isExpandedbinding);

            #endregion

            expander.Content = stack;

            return (expander, stack);
        }
        public static (Expander, StackPanel) CreateTreeEffect(EffectElement effect)
        {
            var data = effect.Parent;

            var expander = new Expander()
            {
                Padding = new(32.5, 0, 0, 0)
            };
            var stack = new StackPanel();

            #region Header

            var header = new StackPanel() { Orientation = Orientation.Horizontal };

            expander.Header = header;

            var checkBox = new CheckBox()
            {
                Margin = new(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var upbutton = new Button()
            {
                Content = new PathIcon()
                {
                    Data = PathGeometry.Parse("M7.41,15.41L12,10.83L16.59,15.41L18,14L12,8L6,14L7.41,15.41Z"),
                    Width = 14,
                    Height = 14,
                },
                Margin = new Thickness(5, 0, 0, 0),
                Background = null,
                BorderBrush = null,
                Classes = new("Flat"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var downbutton = new Button()
            {
                Content = new PathIcon()
                {
                    Data = PathGeometry.Parse("M7.41,8.58L12,13.17L16.59,8.58L18,10L12,16L6,10L7.41,8.58Z"),
                    Width = 14,
                    Height = 14,
                },
                Margin = new Thickness(0, 0, 5, 0),
                Background = null,
                BorderBrush = null,
                Classes = new("Flat"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var textBlock = new TextBlock()
            {
                Margin = new(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };

            header.Children.Add(checkBox);
            header.Children.Add(upbutton);
            header.Children.Add(downbutton);
            header.Children.Add(textBlock);

            #endregion


            #region コンテキストメニュー
            var menuListBox = new ContextMenu();
            var Delete = new MenuItem();

            //削除項目の設定
            var menu = new VirtualizingStackPanel() { Orientation = Orientation.Horizontal };
            menu.Children.Add(new PathIcon()
            {
                Data = PathGeometry.Parse("M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z"),
                Margin = new Thickness(5, 0, 5, 0)
            });
            menu.Children.Add(new TextBlock() { Text = Resources.Remove, Margin = new Thickness(20, 0, 5, 0) });
            Delete.Header = menu;

            menuListBox.Items = new MenuItem[] { Delete };

            // 作成したコンテキストメニューをListBox1に設定
            expander.ContextMenu = menuListBox;
            #endregion

            #region イベント

            checkBox.Click += (sender, e) => effect.CreateCheckCommand((bool)((CheckBox)sender!).IsChecked!).Execute();

            upbutton.Click += (sender, e) => effect.CreateUpCommand().Execute();

            downbutton.Click += (sender, e) => effect.CreateDownCommand().Execute();

            Delete.Click += (sender, e) => effect.Parent.CreateRemoveCommand(effect).Execute();

            #endregion

            #region Binding

            var isenablebinding = new Binding("IsEnabled")
            {
                Mode = BindingMode.OneWay,
                Source = effect
            };
            checkBox.Bind(ToggleButton.IsCheckedProperty, isenablebinding);

            var textbinding = new Binding("Name")
            {
                Mode = BindingMode.OneTime,
                Source = effect
            };
            textBlock.Bind(TextBlock.TextProperty, textbinding);

            var isExpandedbinding = new Binding("IsExpanded")
            {
                Mode = BindingMode.TwoWay,
                Source = effect
            };
            expander.Bind(Expander.IsExpandedProperty, isExpandedbinding);

            #endregion

            expander.Content = stack;

            return (expander, stack);
        }

        public record PropertyViewBuilder(Type PropertyType, Func<PropertyElement, Control> CreateFunc)
        {
            public static PropertyViewBuilder Create<T>(Func<T, Control> CreateFunc) where T : PropertyElement
            {
                return new(typeof(T), (e) => CreateFunc((T)e));
            }
        }

        public record KeyFrameViewBuilder(Type PropertyType, Func<IKeyFrameProperty, Control> CreateFunc);
    }
}
