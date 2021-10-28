using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;
using BEditor.LangResources;
using BEditor.Models;
using BEditor.ViewModels.Properties;
using BEditor.ViewModels.Timelines;
using BEditor.Views.Properties;
using BEditor.Views.Timelines;

using Reactive.Bindings;

namespace BEditor.Extensions
{
    public static partial class ViewBuilder
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
            PropertyViewBuilder.Create<GradientProperty>(p => new GradientPropertyView(p)),
            PropertyViewBuilder.Create<LabelComponent>(p =>
            {
                var label = new ContentControl
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
                var expander = new Expander
                {
                    Header = p.PropertyMetadata?.Name ?? string.Empty,
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
                var isExpandedbind = new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = p };

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
                var expander = new Expander
                {
                    Header = p.PropertyMetadata?.Name ?? string.Empty,
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
                var isExpandedbind = new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = p };

                expander.Bind(Expander.IsExpandedProperty, isExpandedbind);

                return expander;
            })
        };

        static ViewBuilder()
        {
            _removeEffect.Subscribe(efct => efct.Parent.RemoveEffect(efct).Execute());

            _copyEffectId.Subscribe(async efct => await Application.Current.Clipboard.SetTextAsync(efct.Id.ToString()));

            _saveEffect.Subscribe(async efct =>
            {
                var record = new SaveFileRecord
                {
                    Filters =
                    {
                        new(Strings.EffectFile, new string[]{ "befct" })
                    }
                };

                if (await AppModel.Current.FileDialog.ShowSaveFileDialogAsync(record)
                && !await Serialize.SaveToFileAsync(new EffectWrapper(efct), record.FileName))
                {
                    AppModel.Current.Message.Snackbar(Strings.FailedToSave, string.Empty, IMessage.IconType.Error);
                }
            });

            _saveObject.Subscribe(async obj =>
            {
                var record = new SaveFileRecord
                {
                    Filters =
                    {
                        new(Strings.ObjectFile, new string[]{ "bobj" })
                    }
                };
                if (await AppModel.Current.FileDialog.ShowSaveFileDialogAsync(record)
                && !await Serialize.SaveToFileAsync(new EffectWrapper(obj), record.FileName))
                {
                    AppModel.Current.Message.Snackbar(Strings.FailedToSave, string.Empty, IMessage.IconType.Error);
                }
            });
        }

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

        public static ClipPropertyViewModel GetCreateClipPropertyViewModel(this ClipElement clip)
        {
            if (clip[ClipPropertyViewModelProperty] is null)
            {
                clip[ClipPropertyViewModelProperty] = new ClipPropertyViewModel(clip);
            }
            return clip.GetValue(ClipPropertyViewModelProperty);
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
                AppModel.Current.UIThread.Send(static c =>
                {
                    var easing = (EasingFunc)c!;
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
                }, easing);
            }
            var ctr = easing.GetValue(EasePropertyViewProperty);
            if (ctr.Parent is ContentControl contentCtr)
            {
                contentCtr.Content = null;
            }
            return ctr;
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
                    var stack = new StackPanel();

                    keyFrame.Content = stack;

                    foreach (var item in effect.Children)
                    {
                        if (item is IKeyframeProperty e)
                        {
                            var tmp = e.GetCreateKeyframeView();
                            stack.Children.Add(tmp);
                        }
                    }

                    keyFrame.Bind(Expander.HeaderProperty, new Binding("Name") { Mode = BindingMode.OneTime, Source = effect });
                    keyFrame.Bind(Expander.IsExpandedProperty, new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = effect });

                    effect[KeyframeProperty] = keyFrame;
                }, effect);
            }
            var ctr = effect.GetValue(KeyframeProperty);

            if (ctr.Parent is ContentPresenter parent) parent.Content = null;

            return ctr;
        }

        private static readonly ReactiveCommand<EffectElement> _removeEffect = new();

        private static readonly ReactiveCommand<EffectElement> _copyEffectId = new();

        private static readonly ReactiveCommand<EffectElement> _saveEffect = new();

        private static readonly ReactiveCommand<ObjectElement> _saveObject = new();

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
                    DataContext = obj,
                    VerticalAlignment = VerticalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Content = obj.Name
                };

                var copyId = new MenuItem
                {
                    Command = _copyEffectId,
                    CommandParameter = obj,
                    Header = Strings.CopyID,
                    Icon = new FluentAvalonia.UI.Controls.SymbolIcon
                    {
                        Symbol = FluentAvalonia.UI.Controls.Symbol.Copy,
                        FontSize = 20,
                    },
                };

                var save = new MenuItem
                {
                    Command = _saveObject,
                    CommandParameter = obj,
                    Header = Strings.SaveAs,
                    Icon = new FluentAvalonia.UI.Controls.SymbolIcon
                    {
                        Symbol = FluentAvalonia.UI.Controls.Symbol.Save,
                        FontSize = 20,
                    },
                };

                var header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 4,
                    Children =
                    {
                        checkBox,
                    }
                };

                expander.Header = header;

                header.ContextMenu = new ContextMenu
                {
                    Items = new MenuItem[]
                    {
                        copyId,
                        save,
                    },
                };

                // event設定
                checkBox.Click += (s, e) =>
                {
                    if (s is CheckBox check && check.DataContext is EffectElement efct)
                    {
                        efct.ChangeIsEnabled(check.IsChecked ?? false).Execute();
                    }
                };

                // binding設定
                var isEnablebind = new Binding("IsEnabled")
                {
                    Mode = BindingMode.OneWay,
                    Source = obj
                };
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
                Classes = { "property" }
            };

            #region Header

            {
                var checkBox = new CheckBox
                {
                    DataContext = effect,
                    VerticalAlignment = VerticalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Content = effect.Name
                };
                var upbutton = new Button
                {
                    DataContext = effect,
                    BorderThickness = default,
                    Background = null,
                    Content = new FluentAvalonia.UI.Controls.SymbolIcon
                    {
                        Symbol = FluentAvalonia.UI.Controls.Symbol.ChevronUp,
                        FontSize = 20,
                    },
                };
                var downbutton = new Button
                {
                    DataContext = effect,
                    BorderThickness = default,
                    Background = null,
                    Content = new FluentAvalonia.UI.Controls.SymbolIcon
                    {
                        Symbol = FluentAvalonia.UI.Controls.Symbol.ChevronDown,
                        FontSize = 20,
                    },
                };

                var remove = new MenuItem
                {
                    Command = _removeEffect,
                    CommandParameter = effect,
                    Header = Strings.Remove,
                    Icon = new FluentAvalonia.UI.Controls.SymbolIcon
                    {
                        Symbol = FluentAvalonia.UI.Controls.Symbol.Delete,
                        FontSize = 20,
                    },
                };

                var copyId = new MenuItem
                {
                    Command = _copyEffectId,
                    CommandParameter = effect,
                    Header = Strings.CopyID,
                    Icon = new FluentAvalonia.UI.Controls.SymbolIcon
                    {
                        Symbol = FluentAvalonia.UI.Controls.Symbol.Copy,
                        FontSize = 20,
                    },
                };

                var save = new MenuItem
                {
                    Command = _saveEffect,
                    CommandParameter = effect,
                    Header = Strings.SaveAs,
                    Icon = new FluentAvalonia.UI.Controls.SymbolIcon
                    {
                        Symbol = FluentAvalonia.UI.Controls.Symbol.Save,
                        FontSize = 20,
                    },
                };

                var header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 4,
                    Children =
                    {
                        checkBox,
                        upbutton,
                        downbutton,
                    }
                };

                expander.Header = header;

                header.ContextMenu = new ContextMenu
                {
                    Items = new MenuItem[]
                    {
                        remove,
                        copyId,
                        save,
                    },
                };

                // event設定
                checkBox.Click += (s, e) =>
                {
                    if (s is CheckBox check && check.DataContext is EffectElement efct)
                    {
                        efct.ChangeIsEnabled(check.IsChecked ?? false).Execute();
                    }
                };

                upbutton.Click += (s, e) =>
                {
                    if (s is Button btn && btn.DataContext is EffectElement efct)
                    {
                        efct.BringForward().Execute();
                    }
                };

                downbutton.Click += (s, e) =>
                {
                    if (s is Button btn && btn.DataContext is EffectElement efct)
                    {
                        efct.SendBackward().Execute();
                    }
                };

                // binding設定
                var isEnablebind = new Binding("IsEnabled")
                {
                    Mode = BindingMode.OneWay,
                    Source = effect
                };
                checkBox.Bind(ToggleButton.IsCheckedProperty, isEnablebind);
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

        public sealed record PropertyViewBuilder(Type PropertyType, Func<PropertyElement, Control> CreateFunc)
        {
            public static PropertyViewBuilder Create<T>(Func<T, Control> CreateFunc) where T : PropertyElement
            {
                return new(typeof(T), (p) => CreateFunc((T)p));
            }
        }

        public sealed record KeyFrameViewBuilder(Type PropertyType, Func<IKeyframeProperty, Control> CreateFunc)
        {
            public static KeyFrameViewBuilder Create<T>(Func<T, Control> CreateFunc) where T : IKeyframeProperty
            {
                return new(typeof(T), (p) => CreateFunc((T)p));
            }
        }
    }
}