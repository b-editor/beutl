using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

using BEditor.Models.Settings;
using BEditor.ViewModels.TimeLines;
using BEditor.Views.CustomControl;
using BEditor.Views.PropertyControls;
using BEditor.Views.TimeLines;

using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;
using BEditor.Core.Extensions;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Control;
using BEditor.Core.Data;
using BEditor.Core.Data.Primitive.Components;
using BEditor.Views.PropertyControl;
using BEditor.WPF.Controls;
using BEditor.ViewModels.PropertyControl;
using BEditor.ViewModels.Converters;
using CustomTreeView = BEditor.WPF.Controls.ExpandTree;

namespace BEditor.Views
{
    public static class ModelToComponent
    {
        public static List<PropertyViewBuilder> PropertyViewBuilders { get; } = new List<PropertyViewBuilder>();
        public static List<KeyFrameViewBuilder> KeyFrameViewBuilders { get; } = new List<KeyFrameViewBuilder>();

        private static readonly IMultiValueConverter HeaderConverter = new PropertyHeaderTextConverter();
        private static readonly Binding HeaderBinding = new("Metadata.Value.Name") { Mode = BindingMode.OneTime };
        private static readonly Binding ClipNameBinding = new("Property.Parent.Name") { Mode = BindingMode.OneTime };
        private static readonly Binding ClipTextBinding = new("Property.Parent.Parent.LabelText") { Mode = BindingMode.OneWay };
        private static readonly Binding PropertyIsCheckedBinding = new("Property.IsChecked") { Mode = BindingMode.OneWay };
        private static readonly Binding PropertyTextBinding = new("Property.Text") { Mode = BindingMode.OneWay };
        private static readonly Binding PropertyFileBinding = new("Property.File") { Mode = BindingMode.OneWay };
        private static readonly Binding PropertyFolderBinding = new("Property.Folder") { Mode = BindingMode.OneWay };
        private static readonly Binding PropertyIndexBinding = new("Property.Index") { Mode = BindingMode.OneWay };
        private static readonly Binding PropertySelectBinding = new("Property.Select") { Mode = BindingMode.OneWay };
        private static readonly Binding PropertyValueBinding = new("Property.Value") { Mode = BindingMode.OneWay };
        private static readonly Binding ItemsSourcePropertyBinding = new("Metadata.Value.ItemSource") { Mode = BindingMode.OneWay };
        private static readonly Binding DisplayMemberPathBinding = new("Metadata.Value.MemberPath") { Mode = BindingMode.OneTime };
        private static readonly Binding BrushBinding = new("Brush") { Mode = BindingMode.OneWay };
        private static readonly Binding OpenDialogBinding = new("OpenDialog") { Mode = BindingMode.OneTime };
        private static readonly Binding CommandBinding = new("Command") { Mode = BindingMode.OneTime };
        private static readonly Binding ResetBinding = new("Reset") { Mode = BindingMode.OneTime };
        private static readonly Binding BindBinding = new("Bind") { Mode = BindingMode.OneTime };
        private static readonly Binding GotFocusBinding = new("GotFocus") { Mode = BindingMode.OneTime };
        private static readonly Binding LostFocusBinding = new("LostFocus") { Mode = BindingMode.OneTime };
        private static readonly Binding TextChangedBinding = new("TextChanged") { Mode = BindingMode.OneTime };
        private static readonly Binding PreviewMouseWheelBinding = new("PreviewMouseWheel") { Mode = BindingMode.OneTime };
        private static readonly MultiBinding TooltipBinding = new()
        {
            Converter = HeaderConverter,
            Bindings =
            {
                HeaderBinding,
                ClipNameBinding,
                ClipTextBinding,
            }
        };


        static ModelToComponent()
        {

            #region CreatePropertyView
            // CheckProperty
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(CheckProperty),
                CreateFunc = (elm) =>
                {
                    var view = new CheckPropertyView()
                    {
                        DataContext = new CheckPropertyViewModel((CheckProperty)elm)
                    };
                    
                    view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                    view.SetBinding(CheckPropertyView.IsCheckedProperty, PropertyIsCheckedBinding);
                    view.SetBinding(CheckPropertyView.CheckCommandProperty, CommandBinding);
                    view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                    view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);

                    view.SetBinding(FrameworkElement.ToolTipProperty, TooltipBinding);

                    return view;
                }
            });
            // ColorAnimation
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(ColorAnimationProperty),
                CreateFunc = (elm) => new PropertyControl.ColorAnimation(elm as ColorAnimationProperty)
            });
            // ColorProperty
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(ColorProperty),
                CreateFunc = (elm) =>
                {
                    var view = new ColorPropertyView()
                    {
                        DataContext = new ColorPickerViewModel((ColorProperty)elm)
                    };

                    view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                    view.SetBinding(ColorPropertyView.ColorProperty, BrushBinding);
                    view.SetBinding(ColorPropertyView.ClickCommandProperty, OpenDialogBinding);
                    view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                    view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);

                    view.SetBinding(FrameworkElement.ToolTipProperty, TooltipBinding);

                    return view;
                }
            });
            // DocumentProperty
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(DocumentProperty),
                CreateFunc = (elm) =>
                {
                    var view = new DocumentPropertyView()
                    {
                        DataContext = new DocumentPropertyViewModel((DocumentProperty)elm)
                    };

                    view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                    view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);
                    view.SetBinding(DocumentPropertyView.TextProperty, PropertyTextBinding);
                    view.SetBinding(DocumentPropertyView.GotFocusCommandProperty, GotFocusBinding);
                    view.SetBinding(DocumentPropertyView.LostFocusCommandProperty, LostFocusBinding);
                    view.SetBinding(DocumentPropertyView.TextChangedCommandProperty, TextChangedBinding);

                    return view;
                }
            });
            // EaseProperty
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(EaseProperty),
                CreateFunc = (elm) => new EaseControl(elm as EaseProperty)
            });
            // DialogProperty
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(DialogProperty),
                CreateFunc = (elm) => new DialogControl(elm as DialogProperty)
            });
            // ExpandGroup
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(ExpandGroup),
                CreateFunc = (elm) =>
                {
                    var group = elm as ExpandGroup;
                    var _settingcontrol = new CustomTreeView()
                    {
                        Header = group.PropertyMetadata.Name,
                        HeaderHeight = 35F
                    };

                    var stack = new VirtualizingStackPanel();
                    VirtualizingPanel.SetIsVirtualizing(stack, true);
                    VirtualizingPanel.SetVirtualizationMode(stack, VirtualizationMode.Recycling);

                    var margin = new Thickness(32.5, 0, 0, 0);

                    foreach (var item in group.Children)
                    {
                        var content = item.GetCreatePropertyView();

                        if (content is FrameworkElement fe)
                        {
                            fe.Margin = margin;
                        }

                        stack.Children.Add(content);
                    }

                    _settingcontrol.Content = stack;

                    _settingcontrol.SetResourceReference(CustomTreeView.HeaderColorProperty, "MaterialDesignBody");
                    _settingcontrol.SetBinding(CustomTreeView.IsExpandedProperty, new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = group });

                    _settingcontrol.ExpanderUpdate();

                    return _settingcontrol;
                }
            });
            // FileProperty
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(FileProperty),
                CreateFunc = (elm) =>
                {
                    var view = new FilePropertyView()
                    {
                        DataContext = new FilePropertyViewModel((FileProperty)elm)
                    };

                    view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                    view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                    view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);
                    view.SetBinding(FrameworkElement.ToolTipProperty, TooltipBinding);

                    view.SetBinding(FilePropertyView.FileProperty, PropertyFileBinding);
                    view.SetBinding(FilePropertyView.OpenFileCommandProperty, CommandBinding);

                    return view;
                }
            });
            // FontProperty
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(FontProperty),
                CreateFunc = (elm) =>
                {
                    var view = new FontPropertyView()
                    {
                        DataContext = new FontPropertyViewModel((FontProperty)elm)
                    };

                    view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                    view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                    view.SetBinding(FrameworkElement.ToolTipProperty, TooltipBinding);

                    view.SetBinding(FontPropertyView.ItemsSourceProperty, ItemsSourcePropertyBinding);
                    view.SetBinding(FontPropertyView.CommandProperty, CommandBinding);
                    view.SetBinding(FontPropertyView.SelectedItemProperty, PropertySelectBinding);

                    return view;
                }
            });
            // Group
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(Group),
                CreateFunc = (elm) =>
                {
                    VirtualizingStackPanel stack = new VirtualizingStackPanel();
                    VirtualizingPanel.SetIsVirtualizing(stack, true);
                    VirtualizingPanel.SetVirtualizationMode(stack, VirtualizationMode.Recycling);

                    var group = elm as Group;

                    foreach (var item in group.Children)
                    {
                        var content = item.GetCreatePropertyView();

                        stack.Children.Add(content);
                    }

                    return stack;
                }
            });
            // SelectorProperty
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(SelectorProperty),
                CreateFunc = (elm) =>
                {
                    var view = new SelectorPropertyView()
                    {
                        DataContext = new SelectorPropertyViewModel((SelectorProperty)elm)
                    };

                    view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                    view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                    view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);
                    view.SetBinding(FrameworkElement.ToolTipProperty, TooltipBinding);

                    view.SetBinding(SelectorPropertyView.ItemsSourceProperty, ItemsSourcePropertyBinding);
                    view.SetBinding(SelectorPropertyView.CommandProperty, CommandBinding);
                    view.SetBinding(SelectorPropertyView.SelectedIndexProperty, PropertyIndexBinding);
                    view.SetBinding(SelectorPropertyView.DisplayMemberPathProperty, DisplayMemberPathBinding);

                    return view;
                }
            });
            // ValueProperty
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(ValueProperty),
                CreateFunc = (elm) =>
                {
                    var view = new ValuePropertyView()
                    {
                        DataContext = new ValuePropertyViewModel((ValueProperty)elm)
                    };

                    view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                    view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                    view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);
                    view.SetBinding(FrameworkElement.ToolTipProperty, TooltipBinding);

                    view.SetBinding(ValuePropertyView.ValueProperty, PropertyValueBinding);
                    view.SetBinding(ValuePropertyView.GotFocusCommandProperty, GotFocusBinding);
                    view.SetBinding(ValuePropertyView.LostFocusCommandProperty, LostFocusBinding);
                    view.SetBinding(ValuePropertyView.PreviewMouseWheelCommandProperty, PreviewMouseWheelBinding);
                    view.SetBinding(ValuePropertyView.KeyDownCommandProperty, TextChangedBinding);

                    return view;
                }
            });
            // TextProperty
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(TextProperty),
                CreateFunc = (elm) =>
                {
                    var view = new TextPropertyView()
                    {
                        DataContext = new TextPropertyViewModel((TextProperty)elm)
                    };

                    view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                    view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                    view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);
                    view.SetBinding(FrameworkElement.ToolTipProperty, TooltipBinding);

                    view.SetBinding(TextPropertyView.TextProperty, PropertyValueBinding);
                    view.SetBinding(TextPropertyView.GotFocusCommandProperty, GotFocusBinding);
                    view.SetBinding(TextPropertyView.LostFocusCommandProperty, LostFocusBinding);
                    view.SetBinding(TextPropertyView.TextChangedCommandProperty, TextChangedBinding);

                    return view;
                }
            });
            // ButtonComponent
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(ButtonComponent),
                CreateFunc = (elm) =>
                {
                    var view = new ButtonComponentView()
                    {
                        DataContext = new ButtonComponentViewModel((ButtonComponent)elm)
                    };

                    view.SetBinding(ButtonComponentView.TextProperty, HeaderBinding);
                    view.SetBinding(ButtonComponentView.CommandProperty, CommandBinding);

                    return view;
                }
            });
            // FolderProperty
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(FolderProperty),
                CreateFunc = elm =>
                {
                    var view = new FolderPropertyView()
                    {
                        DataContext = new FolderPropertyViewModel((FolderProperty)elm)
                    };

                    view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                    view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                    view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);
                    view.SetBinding(FrameworkElement.ToolTipProperty, TooltipBinding);

                    view.SetBinding(FolderPropertyView.FolderProperty, PropertyFolderBinding);
                    view.SetBinding(FolderPropertyView.OpenFolderCommandProperty, CommandBinding);

                    return view;
                }
            });
            // LabelComponent
            PropertyViewBuilders.Add(new()
            {
                PropertyType = typeof(LabelComponent),
                CreateFunc = elm =>
                {
                    var view = new LabelComponentView()
                    {
                        DataContext = elm
                    };

                    view.SetBinding(LabelComponentView.TextProperty, "Text");

                    return view;
                }
            });
            #endregion

            #region CreateKeyFrameView
            // EaseProperty
            KeyFrameViewBuilders.Add(new()
            {
                PropertyType = typeof(EaseProperty),
                CreateFunc = (elm) => new KeyFrame(elm.GetParent3(), elm as EaseProperty)
            });
            // ColorAnimation
            KeyFrameViewBuilders.Add(new()
            {
                PropertyType = typeof(ColorAnimationProperty),
                CreateFunc = (elm) => new TimeLines.ColorAnimation(elm as ColorAnimationProperty)
            });
            // ExpandGroup
            KeyFrameViewBuilders.Add(new()
            {
                PropertyType = typeof(ExpandGroup),
                CreateFunc = (elm) =>
                {
                    var group = elm as ExpandGroup;

                    var expander = new CustomTreeView()
                    {
                        Header = group.PropertyMetadata.Name,
                        HeaderHeight = Setting.ClipHeight + 1,
                        TreeStair = 1
                    };

                    var stack = new VirtualizingStackPanel();
                    VirtualizingPanel.SetIsVirtualizing(stack, true);
                    VirtualizingPanel.SetVirtualizationMode(stack, VirtualizationMode.Recycling);

                    expander.Content = stack;

                    var binding = new Binding("ActualWidth") { Mode = BindingMode.OneWay, Source = stack };

                    foreach (var item in group.Children)
                    {
                        if (item is IKeyFrameProperty easing)
                        {
                            var tmp = easing.GetCreateKeyFrameView();

                            (tmp as FrameworkElement)?.SetBinding(FrameworkElement.WidthProperty, binding);

                            stack.Children.Add(tmp);
                        }
                    }

                    expander.SetBinding(CustomTreeView.IsExpandedProperty, new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = group });

                    expander.ExpanderUpdate();

                    return expander;
                }
            });
            // Group
            KeyFrameViewBuilders.Add(new()
            {
                PropertyType = typeof(Group),
                CreateFunc = (elm) =>
                {
                    var group = elm as Group;

                    var stack = new VirtualizingStackPanel();
                    VirtualizingPanel.SetIsVirtualizing(stack, true);
                    VirtualizingPanel.SetVirtualizationMode(stack, VirtualizationMode.Recycling);

                    var binding = new Binding("ActualWidth") { Mode = BindingMode.OneWay, Source = stack };

                    foreach (var item in group.Children)
                    {
                        if (item is IKeyFrameProperty easing)
                        {
                            var tmp = easing.GetCreateKeyFrameView();
                            (tmp as FrameworkElement)?.SetBinding(FrameworkElement.WidthProperty, binding);
                            stack.Children.Add(tmp);
                        }
                    }

                    return stack;
                }
            });
            #endregion
        }

        public class PropertyViewBuilder
        {
            public Type PropertyType { get; set; }
            public Func<PropertyElement, UIElement> CreateFunc { get; set; }
        }

        public class KeyFrameViewBuilder
        {
            public Type PropertyType { get; set; }
            public Func<IKeyFrameProperty, UIElement> CreateFunc { get; set; }
        }

        public static UIElement GetCreatePropertyView(this PropertyElement property)
        {
            if (!property.ComponentData.ContainsKey("GetPropertyView"))
            {
                var type = property.GetType();
                var func = PropertyViewBuilders.Find(x => type == x.PropertyType || type.IsSubclassOf(x.PropertyType));

                property.ComponentData.Add("GetPropertyView", func.CreateFunc?.Invoke(property));
            }
            return property.ComponentData["GetPropertyView"];
        }
        public static UIElement GetCreateKeyFrameView(this IKeyFrameProperty property)
        {
            if (!property.ComponentData.ContainsKey("GetKeyFrameView"))
            {
                var type = property.GetType();
                var func = KeyFrameViewBuilders.Find(x => type == x.PropertyType || type.IsSubclassOf(x.PropertyType));

                property.ComponentData.Add("GetKeyFrameView", func.CreateFunc?.Invoke(property));
            }
            return property.ComponentData["GetKeyFrameView"];
        }
        public static ClipUI GetCreateClipView(this ClipData clip)
        {
            if (!clip.ComponentData.ContainsKey("GetClipView"))
            {
                clip.ComponentData.Add("GetClipView", new ClipUI(clip)
                {
                    Name = clip.Name,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                });
            }
            return clip.ComponentData["GetClipView"];
        }
        public static ClipUIViewModel GetCreateClipViewModel(this ClipData clip)
        {
            if (!clip.ComponentData.ContainsKey("GetClipViewModel"))
            {
                clip.ComponentData.Add("GetClipViewModel", new ClipUIViewModel(clip));
            }
            return clip.ComponentData["GetClipViewModel"];
        }
        public static UIElement GetCreatePropertyView(this EffectElement effect)
        {
            if (!effect.ComponentData.ContainsKey("GetControl"))
            {
                CustomTreeView expander;
                VirtualizingStackPanel stack;

                if (effect is ObjectElement @object)
                {
                    (expander, stack) = App.CreateTreeObject(@object);
                }
                else
                {
                    (expander, stack) = App.CreateTreeEffect(effect);
                }

                //Get毎にnewされると非効率なのでローカルに置く

                var margin = new Thickness(0, 0, 32.5, 0);
                foreach (var item in effect.Children)
                {
                    var tmp = item.GetCreatePropertyView();

                    if (tmp is FrameworkElement element)
                    {
                        element.Margin = margin;
                    }

                    stack.Children.Add(tmp);
                }

                //エクスパンダーをアップデート
                expander.ExpanderUpdate();

                effect.ComponentData.Add("GetControl", expander);
            }
            return effect.ComponentData["GetControl"];
        }
        public static UIElement GetCreateKeyFrameView(this EffectElement effect)
        {
            if (!effect.ComponentData.ContainsKey("GetKeyFrame"))
            {
                var keyFrame = new CustomTreeView() { HeaderHeight = Setting.ClipHeight + 1 };

                var stack = new VirtualizingStackPanel();
                VirtualizingPanel.SetIsVirtualizing(stack, true);
                VirtualizingPanel.SetVirtualizationMode(stack, VirtualizationMode.Recycling);

                keyFrame.Content = stack;

                var binding = new Binding("ActualWidth") { Mode = BindingMode.OneWay, Source = keyFrame };

                foreach (var item in effect.Children)
                {
                    if (item is IKeyFrameProperty e)
                    {

                        var tmp = e.GetCreateKeyFrameView();
                        (tmp as FrameworkElement)?.SetBinding(FrameworkElement.WidthProperty, binding);
                        stack.Children.Add(tmp);
                    }
                }

                keyFrame.SetBinding(CustomTreeView.HeaderProperty, new Binding("Name") { Mode = BindingMode.OneTime, Source = effect });
                keyFrame.SetBinding(CustomTreeView.IsExpandedProperty, new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = effect });

                //エクスパンダーをアップデート
                keyFrame.ExpanderUpdate();

                effect.ComponentData.Add("GetKeyFrame", keyFrame);
            }
            return effect.ComponentData["GetKeyFrame"];
        }
        public static UIElement GetCreatePropertyView(this ClipData clip)
        {
            if (!clip.ComponentData.ContainsKey("GetPropertyView"))
            {
                clip.ComponentData.Add("GetPropertyView", new Object_Setting(clip));
            }
            return clip.ComponentData["GetPropertyView"];
        }
        public static TimeLine GetCreateTimeLineView(this Scene scene)
        {
            if (!scene.ComponentData.ContainsKey("GetTimeLine"))
            {
                scene.ComponentData.Add("GetTimeLine", new TimeLine(scene));
            }
            return scene.ComponentData["GetTimeLine"];
        }
        public static TimeLineViewModel GetCreateTimeLineViewModel(this Scene scene)
        {
            if (!scene.ComponentData.ContainsKey("GetTimeLineViewModel"))
            {
                scene.ComponentData.Add("GetTimeLineViewModel", new TimeLineViewModel(scene));
            }
            return scene.ComponentData["GetTimeLineViewModel"];
        }
        public static PropertyTab GetCreatePropertyTab(this Scene scene)
        {
            if (!scene.ComponentData.ContainsKey("GetPropertyTab"))
            {
                scene.ComponentData.Add("GetPropertyTab", new PropertyTab() { DataContext = scene });
            }
            return scene.ComponentData["GetPropertyTab"];
        }
        public static UIElement GetCreatePropertyView(this EasingFunc easing)
        {
            if (!easing.ComponentData.ContainsKey("GetPropertyView"))
            {
                var _createdControl = new VirtualizingStackPanel()
                {
                    Orientation = Orientation.Vertical,
                    Width = float.NaN,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                foreach (var setting in easing.Children)
                {
                    _createdControl.Children.Add(((PropertyElement)setting).GetCreatePropertyView());
                }

                easing.ComponentData.Add("GetPropertyView", _createdControl);
            }
            return easing.ComponentData["GetPropertyView"];
        }
    }
}
