using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;
using BEditor.Models;
using BEditor.Properties;
using BEditor.ViewModels.Properties;
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
            PropertyViewBuilder.Create<SelectorProperty>(p => new SelectorPropertyView(new SelectorPropertyViewModel(p))),
            new(typeof(SelectorProperty<>), prop =>
            {
                var vmtype = typeof(SelectorPropertyViewModel<>).MakeGenericType(prop.GetType().GenericTypeArguments);
                return new SelectorPropertyView((ISelectorPropertyViewModel)Activator.CreateInstance(vmtype, prop)!);
            }),
            PropertyViewBuilder.Create<ValueProperty>(p => new ValuePropertyView(p)),
            PropertyViewBuilder.Create<TextProperty>(p => new TextPropertyView(p)),
            PropertyViewBuilder.Create<EaseProperty>(p => new EasePropertyView(p)),
            PropertyViewBuilder.Create<DocumentProperty>(p => new DocumentPropertyView(p)),
            PropertyViewBuilder.Create<FontProperty>(p => new FontPropertyView(p)),
            PropertyViewBuilder.Create<ColorProperty>(p => new ColorPropertyView(p)),
            PropertyViewBuilder.Create<FileProperty>(p => new FilePropertyView(p)),
            PropertyViewBuilder.Create<FolderProperty>(p => new FolderPropertyView(p)),
            PropertyViewBuilder.Create<ColorAnimationProperty>(p => new ColorAnimationPropertyView(p)),
            PropertyViewBuilder.Create<ButtonComponent>(p => new ButtonCompornentView(p)),
            PropertyViewBuilder.Create<LabelComponent>(p =>
            {
                var label = new Label
                {
                    Height = 40,
                    Background = Brushes.Transparent,
                    DataContext = p
                };
                label.Bind(ContentControl.ContentProperty, new Binding("Text"));

                return label;
            }),
            PropertyViewBuilder.Create<DialogProperty>(p => new DialogPropertyView(p)),
            PropertyViewBuilder.Create<ExpandGroup>(p =>
            {
                var header = new Label
                {
                    Content = p.PropertyMetadata?.Name ?? string.Empty,
                    Height = 24,
                    Foreground = (IBrush)App.Current.FindResource("SystemControlForegroundBaseHighBrush")!
                };
                var expander = new Expander
                {
                    Header = header,
                    Classes = { "property" }
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
            PropertyViewBuilder.Create<Group>(p =>
            {
                var stack = new StackPanel();

                foreach (var item in p.Children)
                {
                    var content = item.GetCreatePropertyElementView();

                    stack.Children.Add(content);
                }

                return stack;
            }),
        };
        public static readonly List<KeyFrameViewBuilder> KeyframeViewBuilders = new()
        {
            // EaseProperty
            KeyFrameViewBuilder.Create<EaseProperty>(prop => new KeyframeView(prop)),
            // ColorAnimation
            KeyFrameViewBuilder.Create<ColorAnimationProperty>(prop => new KeyframeView(prop)),
            KeyFrameViewBuilder.Create<ExpandGroup>(p =>
            {
                var header = new Label
                {
                    Content = p.PropertyMetadata?.Name ?? string.Empty,
                    Height = 24,
                    Foreground = (IBrush)App.Current.FindResource("SystemControlForegroundBaseHighBrush")!
                };
                var expander = new Expander
                {
                    Header = header,
                    Classes =
                    {
                        "expandkeyframe",
                        "keyframe"
                    }
                };
                var stack = new StackPanel();

                foreach (var item in p.Children)
                {
                    if (item is IKeyframeProperty property)
                    {
                        var content = property.GetCreateKeyframeView();

                        stack.Children.Add(content);
                    }
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
            })
        };
        public static readonly EditingProperty<Timeline> TimelineProperty = EditingProperty.Register<Timeline, Scene>("GetTimeline", EditingPropertyOptions<Timeline>.Create(isDisposable: true));
        public static readonly EditingProperty<TimelineViewModel> TimelineViewModelProperty = EditingProperty.Register<TimelineViewModel, Scene>("GetTimelineViewModel", EditingPropertyOptions<TimelineViewModel>.Create(isDisposable: true));
        public static readonly EditingProperty<ClipView> ClipViewProperty = EditingProperty.Register<ClipView, ClipElement>("GetClipView", EditingPropertyOptions<ClipView>.Create(isDisposable: true));
        public static readonly EditingProperty<ClipPropertyView> ClipPropertyViewProperty = EditingProperty.Register<ClipPropertyView, ClipElement>("GetClipPropertyView", EditingPropertyOptions<ClipPropertyView>.Create(isDisposable: true));
        public static readonly EditingProperty<ClipViewModel> ClipViewModelProperty = EditingProperty.Register<ClipViewModel, ClipElement>("GetClipViewModel", EditingPropertyOptions<ClipViewModel>.Create(isDisposable: true));
        public static readonly EditingProperty<Control> EffectElementViewProperty = EditingProperty.Register<Control, EffectElement>("GetPropertyView", EditingPropertyOptions<Control>.Create(isDisposable: true));
        public static readonly EditingProperty<Control> PropertyElementViewProperty = EditingProperty.Register<Control, PropertyElement>("GetPropertyView", EditingPropertyOptions<Control>.Create(isDisposable: true));
        public static readonly EditingProperty<Control> EasePropertyViewProperty = EditingProperty.Register<Control, EasingFunc>("GetPropertyView", EditingPropertyOptions<Control>.Create(isDisposable: true));
        public static readonly EditingProperty<Control> KeyframeProperty = EditingProperty.Register<Control, EffectElement>("GetKeyframe", EditingPropertyOptions<Control>.Create(isDisposable: true));
        public static readonly EditingProperty<Control> KeyframeViewProperty = EditingProperty.Register<Control, IKeyframeProperty>("GetKeyframeView", EditingPropertyOptions<Control>.Create(isDisposable: true));

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

                property[PropertyElementViewProperty] = func?.CreateFunc?.Invoke(property) ?? new TextBlock { Height = 40 };
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

        public static Control GetCreateKeyframeView(this IKeyframeProperty property)
        {
            if (property[KeyframeViewProperty] is null)
            {
                var type = property.GetType();
                var func = KeyframeViewBuilders.Find(x => x.PropertyType.IsAssignableFrom(type));

                property[KeyframeViewProperty] = func?.CreateFunc?.Invoke(property) ?? new TextBlock { Height = 40 };
            }
            return property.GetValue(KeyframeViewProperty);
        }

        public static Timeline GetCreateTimelineSafe(this Scene scene)
        {
            if (scene[TimelineProperty] is null)
            {
                AppModel.Current.UIThread.Send(static s =>
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
                AppModel.Current.UIThread.Send(static s =>
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
                AppModel.Current.UIThread.Send(static c =>
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
                AppModel.Current.UIThread.Send(static c =>
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
                AppModel.Current.UIThread.Send(static c =>
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
                AppModel.Current.UIThread.Send(static c =>
                {
                    var effect = (EffectElement)c!;

                    var (ex, stack) = (effect is ObjectElement obj) ? CreateObjectExpander(obj) : CreateEffectExpander(effect);

                    stack.Children.AddRange(effect.Children.Select(p => p.GetCreatePropertyElementView()));

                    effect[EffectElementViewProperty] = ex;
                }, effect);
            }
            var ctr = effect.GetValue(EffectElementViewProperty);

            if (ctr.Parent is ContentControl parent) parent.Content = null;

            return ctr;
        }

        public static Control GetCreateKeyFrameViewSafe(this EffectElement effect)
        {
            if (effect[KeyframeProperty] is null)
            {
                AppModel.Current.UIThread.Send(static c =>
                {
                    var effect = (EffectElement)c!;
                    var keyFrame = new Expander
                    {
                        Classes = { "keyframe" }
                    };
                    var header = new Label();
                    var stack = new StackPanel();

                    keyFrame.Content = stack;
                    keyFrame.Header = header;

                    foreach (var item in effect.Children)
                    {
                        if (item is IKeyframeProperty e)
                        {
                            var tmp = e.GetCreateKeyframeView();
                            stack.Children.Add(tmp);
                        }
                    }

                    header.Bind(Layoutable.WidthProperty, new Binding("Parent.Bounds.Width")
                    {
                        Mode = BindingMode.OneWay,
                        Source = header,
                        Converter = ExpanderWidthConverter
                    });
                    header.Bind(ContentControl.ContentProperty, new Binding("Name") { Mode = BindingMode.OneTime, Source = effect });
                    keyFrame.Bind(Expander.IsExpandedProperty, new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = effect });

                    effect[KeyframeProperty] = keyFrame;
                }, effect);
            }
            var ctr = effect.GetValue(KeyframeProperty);

            if (ctr.Parent is ContentPresenter parent) parent.Content = null;

            return ctr;
        }

        private static readonly IValueConverter ExpanderWidthConverter = new FuncValueConverter<double, double>(i => i - 38);

        public static (Expander, StackPanel) CreateObjectExpander(ObjectElement obj)
        {
            var clip = obj.Parent;

            var expander = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Classes = { "property" }
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
                    Text = obj.Name,
                    Foreground = (IBrush)App.Current.FindResource("SystemControlForegroundBaseHighBrush")!
                };

                var header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Background = Brushes.Transparent,
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

                // コンテキストメニュー
                var contextmenu = new ContextMenu();
                var copyId = new MenuItem
                {
                    Icon = new PathIcon { Data = (Geometry)Application.Current.FindResource("Copy24Regular")! },
                    Header = Strings.CopyID,
                    DataContext = obj
                };

                contextmenu.Items = new MenuItem[] { copyId };

                // 作成したコンテキストメニューをListBox1に設定
                header.ContextMenu = contextmenu;

                copyId.Click += async (s, e) =>
                {
                    if (s is MenuItem menu && menu.DataContext is EffectElement effect)
                    {
                        await Application.Current.Clipboard.SetTextAsync(effect.Id.ToString());
                    }
                };
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
                Classes = { "property" }
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
                        Data = (Geometry)Application.Current.FindResource("ArrowUp28Regular")!
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
                        Data = (Geometry)Application.Current.FindResource("ArrowDown28Regular")!
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
                    Text = effect.Name,
                    Foreground = (IBrush)App.Current.FindResource("SystemControlForegroundBaseHighBrush")!
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
                var remove = new MenuItem
                {
                    Icon = new PathIcon { Data = (Geometry)Application.Current.FindResource("Delete24Regular")! },
                    Header = Strings.Remove,
                    DataContext = effect
                };
                var copyId = new MenuItem
                {
                    Icon = new PathIcon { Data = (Geometry)Application.Current.FindResource("Copy24Regular")! },
                    Header = Strings.CopyID,
                    DataContext = effect
                };

                contextmenu.Items = new MenuItem[] { remove, copyId };

                // 作成したコンテキストメニューをListBox1に設定
                header.ContextMenu = contextmenu;

                remove.Click += (s, e) =>
                {
                    if (s is MenuItem menu && menu.DataContext is EffectElement effect)
                    {
                        effect.Parent!.RemoveEffect(effect).Execute();
                    }
                };
                copyId.Click += async (s, e) =>
                {
                    if (s is MenuItem menu && menu.DataContext is EffectElement effect)
                    {
                        await Application.Current.Clipboard.SetTextAsync(effect.Id.ToString());
                    }
                };
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

        public record KeyFrameViewBuilder(Type PropertyType, Func<IKeyframeProperty, Control> CreateFunc)
        {
            public static KeyFrameViewBuilder Create<T>(Func<T, Control> CreateFunc) where T : IKeyframeProperty
            {
                return new(typeof(T), (p) => CreateFunc((T)p));
            }
        }
    }
}