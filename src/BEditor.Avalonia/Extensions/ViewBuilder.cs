using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;
using BEditor.Properties;
using BEditor.ViewModels.Timelines;
using BEditor.Views.Properties;
using BEditor.Views.Timelines;

namespace BEditor.Extensions
{
    public static class ViewBuilder
    {
        public static readonly List<PropertyViewBuilder> PropertyViewBuilders = new()
        {
            PropertyViewBuilder.Create<CheckProperty>(p => new CheckPropertyView(p)),
            PropertyViewBuilder.Create<SelectorProperty>(p => new SelectorPropertyView(p)),
            PropertyViewBuilder.Create<EaseProperty>(p => new EasePropertyView(p)),
            PropertyViewBuilder.Create<ExpandGroup>(p =>
            {
                var header = new TextBlock
                {
                    Text = p.PropertyMetadata?.Name ?? string.Empty,
                };
                var expander = new Expander
                {
                    Header = header,
                };
                var stack = new StackPanel();
                var margin = new Thickness(32.5, 0, 0, 0);

                foreach (var item in p.Children)
                {
                    var content = item.GetCreatePropertyElementView();

                    content.Margin = margin;

                    stack.Children.Add(content);
                }

                expander.Content = stack;

                // binding
                var widthbind = new Binding("Parent.Bounds.Width")
                {
                    Mode = BindingMode.OneWay,
                    Source = expander,
                    Converter = ExpanderWidthConverter
                };
                var isExpandedbind = new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = p };

                header.Bind(Layoutable.WidthProperty, widthbind);
                expander.Bind(Expander.IsExpandedProperty, isExpandedbind);

                return expander;
            }),
        };
        public static readonly EditingProperty<Timeline> TimelineProperty = EditingProperty.Register<Timeline, Scene>("GetTimeline");
        public static readonly EditingProperty<TimelineViewModel> TimelineViewModelProperty = EditingProperty.Register<TimelineViewModel, Scene>("GetTimelineViewModel");
        public static readonly EditingProperty<ClipView> ClipViewProperty = EditingProperty.Register<ClipView, ClipElement>("GetClipView");
        public static readonly EditingProperty<ClipPropertyView> ClipPropertyViewProperty = EditingProperty.Register<ClipPropertyView, ClipElement>("GetClipPropertyView");
        public static readonly EditingProperty<ClipViewModel> ClipViewModelProperty = EditingProperty.Register<ClipViewModel, ClipElement>("GetClipViewModel");
        public static readonly EditingProperty<Control> EffectElementViewProperty = EditingProperty.Register<Control, EffectElement>("GetPropertyView");
        public static readonly EditingProperty<Control> PropertyElementViewProperty = EditingProperty.Register<Control, PropertyElement>("GetPropertyView");
        public static readonly EditingProperty<Control> EasePropertyViewProperty = EditingProperty.Register<Control, EasingFunc>("GetPropertyView");

        public static Timeline GetCreateTimeline(this Scene scene)
        {
            if (scene[TimelineProperty] is null)
            {
                scene[TimelineProperty] = new Timeline(scene);
            }
            return scene.GetValue(TimelineProperty);
        }
        public static TimelineViewModel GetCreateTimelineViewModel(this Scene scene)
        {
            if (scene[TimelineViewModelProperty] is null)
            {
                scene[TimelineViewModelProperty] = new TimelineViewModel(scene);
            }
            return scene.GetValue(TimelineViewModelProperty);
        }
        public static ClipView GetCreateClipView(this ClipElement clip)
        {
            if (clip[ClipViewProperty] is null)
            {
                clip[ClipViewProperty] = new ClipView(clip);
            }
            return clip.GetValue(ClipViewProperty);
        }
        public static ClipPropertyView GetCreateClipPropertyView(this ClipElement clip)
        {
            if (clip[ClipPropertyViewProperty] is null)
            {
                clip[ClipPropertyViewProperty] = new ClipPropertyView(clip);
            }
            return clip.GetValue(ClipPropertyViewProperty);
        }
        public static ClipViewModel GetCreateClipViewModel(this ClipElement clip)
        {
            if (clip[ClipViewModelProperty] is null)
            {
                clip[ClipViewModelProperty] = new ClipViewModel(clip);
            }
            return clip.GetValue(ClipViewModelProperty);
        }
        public static Control GetCreateEffectPropertyView(this EffectElement effect)
        {
            if (effect[EffectElementViewProperty] is null)
            {
                var (ex, stack) = (effect is ObjectElement obj) ? CreateObjectExpander(obj) : CreateEffectExpander(effect);

                stack.Children.AddRange(effect.Children.Select(p => p.GetCreatePropertyElementView()));

                effect[EffectElementViewProperty] = ex;
            }
            return effect.GetValue(EffectElementViewProperty);
        }
        public static Control GetCreatePropertyElementView(this PropertyElement property)
        {
            if (property[PropertyElementViewProperty] is null)
            {
                var type = property.GetType();
                var func = PropertyViewBuilders.Find(x =>
                {
                    if (type.IsGenericType)
                    {
                        return type.GetGenericTypeDefinition() == x.PropertyType;
                    }

                    return x.PropertyType.IsAssignableFrom(type);
                });

                property[PropertyElementViewProperty] = func?.CreateFunc?.Invoke(property) ?? new TextBlock { Height = 32.5 };
            }
            return property.GetValue(PropertyElementViewProperty);
        }
        public static Control GetCreateEasingFuncView(this EasingFunc easing)
        {
            if (easing[EasePropertyViewProperty] is null)
            {
                var stack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Width = float.NaN,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                foreach (var setting in easing.Children)
                {
                    stack.Children.Add(((PropertyElement)setting).GetCreatePropertyElementView());
                }

                easing[EasePropertyViewProperty] = stack;
            }
            return easing.GetValue(EasePropertyViewProperty);
        }

        public static Timeline GetCreateTimelineSafe(this Scene scene)
        {
            if (scene[TimelineProperty] is null)
            {
                scene.Synchronize.Send(static s =>
                {
                    var scene = (Scene)s!;
                    scene[TimelineProperty] = new Timeline(scene);
                }, scene);
            }
            return scene.GetValue(TimelineProperty);
        }
        public static TimelineViewModel GetCreateTimelineViewModelSafe(this Scene scene)
        {
            if (scene[TimelineViewModelProperty] is null)
            {
                scene.Synchronize.Send(static s =>
                {
                    var scene = (Scene)s!;
                    scene[TimelineViewModelProperty] = new TimelineViewModel(scene);
                }, scene);
            }
            return scene.GetValue(TimelineViewModelProperty);
        }
        public static ClipView GetCreateClipViewSafe(this ClipElement clip)
        {
            if (clip[ClipViewProperty] is null)
            {
                clip.Synchronize.Send(static c =>
                {
                    var clip = (ClipElement)c!;
                    clip[ClipViewProperty] = new ClipView(clip);
                }, clip);
            }
            return clip.GetValue(ClipViewProperty);
        }
        public static ClipPropertyView GetCreateClipPropertyViewSafe(this ClipElement clip)
        {
            if (clip[ClipPropertyViewProperty] is null)
            {
                clip.Synchronize.Send(static c =>
                {
                    var clip = (ClipElement)c!;
                    clip[ClipPropertyViewProperty] = new ClipPropertyView(clip);
                }, clip);
            }
            return clip.GetValue(ClipPropertyViewProperty);
        }
        public static ClipViewModel GetCreateClipViewModelSafe(this ClipElement clip)
        {
            if (clip[ClipViewModelProperty] is null)
            {
                clip.Synchronize.Send(static c =>
                {
                    var clip = (ClipElement)c!;
                    clip[ClipViewModelProperty] = new ClipViewModel(clip);
                }, clip);
            }
            return clip.GetValue(ClipViewModelProperty);
        }
        public static Control GetCreateEffectPropertyViewSafe(this EffectElement effect)
        {
            if (effect[EffectElementViewProperty] is null)
            {
                effect.Synchronize.Send(static c =>
                {
                    var effect = (EffectElement)c!;

                    var (ex, stack) = (effect is ObjectElement obj) ? CreateObjectExpander(obj) : CreateEffectExpander(effect);

                    stack.Children.AddRange(effect.Children.Select(p => p.GetCreatePropertyElementView()));

                    effect[EffectElementViewProperty] = ex;
                }, effect);
            }
            return effect.GetValue(EffectElementViewProperty);
        }

        private static readonly IValueConverter ExpanderWidthConverter = new FuncValueConverter<double, double>(i => i - 38);
        public static (Expander, StackPanel) CreateObjectExpander(ObjectElement obj)
        {
            var clip = obj.Parent;

            var expander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            #region Header

            {
                var checkBox = new CheckBox
                {
                    Margin = new(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var textBlock = new TextBlock
                {
                    Margin = new(5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = obj.Name
                };

                var header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        checkBox,
                        textBlock
                    }
                };
                expander.Header = header;

                // event設定
                checkBox.Click += (s, e) => obj.ChangeIsEnabled((bool)((CheckBox)s!).IsChecked!).Execute();

                // binding設定
                var widthbind = new Binding("Parent.Bounds.Width")
                {
                    Mode = BindingMode.OneWay,
                    Source = header,
                    Converter = ExpanderWidthConverter
                };
                var isEnablebind = new Binding("IsEnabled")
                {
                    Mode = BindingMode.OneWay,
                    Source = obj
                };
                header.Bind(Layoutable.WidthProperty, widthbind);
                checkBox.Bind(ToggleButton.IsCheckedProperty, isEnablebind);
            }

            #endregion

            // binding設定
            var isExpandedbinding = new Binding("IsExpanded")
            {
                Mode = BindingMode.TwoWay,
                Source = obj
            };

            expander.Bind(Expander.IsExpandedProperty, isExpandedbinding);

            var stack = new StackPanel { Margin = new Thickness(32, 0, 0, 0) };

            expander.Content = stack;

            return (expander, stack);
        }
        public static (Expander, StackPanel) CreateEffectExpander(EffectElement effect)
        {
            var clip = effect.Parent;

            var expander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            #region Header

            {
                var checkBox = new CheckBox
                {
                    Margin = new(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 32
                };
                var upbutton = new Button
                {
                    Content = new PathIcon
                    {
                        Data = (Geometry)Application.Current.FindResource(@"\arrow_up\svg\ic_fluent_arrow_up_28_regular.svg_regular")!
                    },
                    Margin = new Thickness(5, 0, 0, 0),
                    Background = null,
                    BorderBrush = null,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var downbutton = new Button
                {
                    Content = new PathIcon
                    {
                        Data = (Geometry)Application.Current.FindResource(@"\arrow_down\svg\ic_fluent_arrow_down_28_regular.svg_regular")!
                    },
                    Margin = new Thickness(0, 0, 5, 0),
                    Background = null,
                    BorderBrush = null,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var textBlock = new TextBlock
                {
                    Margin = new(5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = effect.Name
                };

                var header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Background = Brushes.Transparent,
                    Children =
                    {
                        checkBox,
                        upbutton,
                        downbutton,
                        textBlock
                    }
                };
                expander.Header = header;

                // event設定
                checkBox.Click += (s, e) => effect.ChangeIsEnabled((bool)((CheckBox)s!).IsChecked!).Execute();

                upbutton.Click += (s, e) => effect.BringForward().Execute();

                downbutton.Click += (s, e) => effect.SendBackward().Execute();

                // binding設定
                var widthbind = new Binding("Parent.Bounds.Width")
                {
                    Mode = BindingMode.OneWay,
                    Source = header,
                    Converter = ExpanderWidthConverter
                };
                var isEnablebind = new Binding("IsEnabled")
                {
                    Mode = BindingMode.OneWay,
                    Source = effect
                };
                header.Bind(Layoutable.WidthProperty, widthbind);
                checkBox.Bind(ToggleButton.IsCheckedProperty, isEnablebind);

                // コンテキストメニュー
                var contextmenu = new ContextMenu();
                var remove = new MenuItem();

                // 削除項目の設定
                var menu = new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };
                menu.Children.Add(new PathIcon
                {
                    Data = (Geometry)Application.Current.FindResource(@"\delete\svg\ic_fluent_delete_24_regular.svg_regular")!,
                    Margin = new Thickness(5, 0, 5, 0)
                });
                menu.Children.Add(new TextBlock
                {
                    Text = Strings.Remove,
                    Margin = new Thickness(20, 0, 5, 0)
                });
                remove.Header = menu;

                contextmenu.Items = new MenuItem[] { remove };

                // 作成したコンテキストメニューをListBox1に設定
                header.ContextMenu = contextmenu;

                remove.Click += (s, e) => effect.Parent!.RemoveEffect(effect).Execute();
            }

            #endregion

            // binding設定
            var isExpandedbinding = new Binding("IsExpanded")
            {
                Mode = BindingMode.TwoWay,
                Source = effect
            };

            expander.Bind(Expander.IsExpandedProperty, isExpandedbinding);

            var stack = new StackPanel { Margin = new Thickness(32, 0, 0, 0) };

            expander.Content = stack;

            return (expander, stack);
        }

        public record PropertyViewBuilder(Type PropertyType, Func<PropertyElement, Control> CreateFunc)
        {
            public static PropertyViewBuilder Create<T>(Func<T, Control> CreateFunc) where T : PropertyElement
            {
                return new(typeof(T), (p) => CreateFunc((T)p));
            }
        }

        public record KeyFrameViewBuilder(Type PropertyType, Func<IKeyFrameProperty, Control> CreateFunc)
        {
            public static KeyFrameViewBuilder Create<T>(Func<T, Control> CreateFunc) where T : IKeyFrameProperty
            {
                return new(typeof(T), (p) => CreateFunc((T)p));
            }
        }
    }
}
