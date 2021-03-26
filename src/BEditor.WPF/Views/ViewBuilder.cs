using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;
using BEditor.Properties;
using BEditor.Models;
using BEditor.ViewModels.Converters;
using BEditor.ViewModels.PropertyControl;
using BEditor.ViewModels.TimeLines;
using BEditor.Views.CustomControl;
using BEditor.Views.PropertyControl;
using BEditor.Views.PropertyControls;
using BEditor.Views.TimeLines;
using BEditor.WPF.Controls;

using MaterialDesignThemes.Wpf;

namespace BEditor.Views
{
    public static class ViewBuilder
    {
        private static readonly Binding HeaderBinding = new("Metadata.Value.Name") { Mode = BindingMode.OneTime };
        private static readonly Binding FileModeBinding = new("PathMode.Value") { Mode = BindingMode.TwoWay };
        private static readonly Binding PropertyIndexBinding = new("Property.Index") { Mode = BindingMode.OneWay };
        private static readonly Binding PropertySelectItemBinding = new("Property.SelectItem") { Mode = BindingMode.OneWay };
        private static readonly Binding PropertyValueBinding = new("Property.Value") { Mode = BindingMode.OneWay };
        private static readonly Binding ItemsSourcePropertyBinding = new("Metadata.Value.ItemSource") { Mode = BindingMode.OneWay };
        private static readonly Binding DisplayMemberPathBinding = new("Metadata.Value.MemberPath") { Mode = BindingMode.OneTime };
        private static readonly Binding BrushBinding = new("Brush.Value") { Mode = BindingMode.OneWay };
        private static readonly Binding OpenDialogBinding = new("OpenDialog") { Mode = BindingMode.OneTime };
        private static readonly Binding CommandBinding = new("Command") { Mode = BindingMode.OneTime };
        private static readonly Binding ResetBinding = new("Reset") { Mode = BindingMode.OneTime };
        private static readonly Binding BindBinding = new("Bind") { Mode = BindingMode.OneTime };
        private static readonly Binding GotFocusBinding = new("GotFocus") { Mode = BindingMode.OneTime };
        private static readonly Binding LostFocusBinding = new("LostFocus") { Mode = BindingMode.OneTime };
        private static readonly Binding TextChangedBinding = new("TextChanged") { Mode = BindingMode.OneTime };
        private static readonly Binding PreviewMouseWheelBinding = new("PreviewMouseWheel") { Mode = BindingMode.OneTime };


        static ViewBuilder()
        {
            #region CreatePropertyView
            // CheckProperty
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<CheckProperty>(prop =>
            {
                var view = new CheckPropertyView()
                {
                    DataContext = new CheckPropertyViewModel(prop)
                };

                view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                view.SetBinding(CheckPropertyView.IsCheckedProperty, PropertyValueBinding);
                view.SetBinding(CheckPropertyView.CheckCommandProperty, CommandBinding);
                view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);

                return view;
            }));
            // ColorAnimation
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<ColorAnimationProperty>(prop => new PropertyControl.ColorAnimation(prop)));
            // ColorProperty
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<ColorProperty>(prop =>
            {
                var view = new ColorPropertyView()
                {
                    DataContext = new ColorPickerViewModel(prop)
                };

                view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                view.SetBinding(ColorPropertyView.ColorProperty, BrushBinding);
                view.SetBinding(ColorPropertyView.ClickCommandProperty, OpenDialogBinding);
                view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);

                return view;
            }));
            // DocumentProperty
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<DocumentProperty>(prop =>
            {
                var view = new DocumentPropertyView()
                {
                    DataContext = new DocumentPropertyViewModel(prop)
                };

                view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);
                view.SetBinding(DocumentPropertyView.TextProperty, PropertyValueBinding);
                view.SetBinding(DocumentPropertyView.GotFocusCommandProperty, GotFocusBinding);
                view.SetBinding(DocumentPropertyView.LostFocusCommandProperty, LostFocusBinding);
                view.SetBinding(DocumentPropertyView.TextChangedCommandProperty, TextChangedBinding);

                return view;
            }));
            // EaseProperty
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<EaseProperty>(prop => new EaseControl(prop)));
            // DialogProperty
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<DialogProperty>(prop => new DialogControl(prop)));
            // ExpandGroup
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<ExpandGroup>(group =>
            {
                var _settingcontrol = new ExpandTree()
                {
                    Header = group.PropertyMetadata?.Name ?? "",
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

                _settingcontrol.SetResourceReference(ExpandTree.HeaderColorProperty, "MaterialDesignBody");
                _settingcontrol.SetBinding(ExpandTree.IsExpandedProperty, new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = group });

                _settingcontrol.ExpanderUpdate();

                return _settingcontrol;
            }));
            // FileProperty
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<FileProperty>(prop =>
            {
                var view = new FilePropertyView()
                {
                    DataContext = new FilePropertyViewModel(prop)
                };

                view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);
                
                view.SetBinding(FilePropertyView.ModeIndexProperty, FileModeBinding);
                view.SetBinding(FilePropertyView.FileProperty, PropertyValueBinding);
                view.SetBinding(FilePropertyView.OpenFileCommandProperty, CommandBinding);

                return view;
            }));
            // FontProperty
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<FontProperty>(prop => new FontPropertyView(new FontPropertyViewModel(prop))));
            // Group
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<Group>(group =>
            {
                var stack = new VirtualizingStackPanel();
                VirtualizingPanel.SetIsVirtualizing(stack, true);
                VirtualizingPanel.SetVirtualizationMode(stack, VirtualizationMode.Recycling);

                foreach (var item in group.Children)
                {
                    var content = item.GetCreatePropertyView();

                    stack.Children.Add(content);
                }

                return stack;
            }));
            // SelectorProperty
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<SelectorProperty>(prop =>
            {
                var view = new SelectorPropertyView()
                {
                    DataContext = new SelectorPropertyViewModel(prop)
                };

                view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);
                
                view.SetBinding(SelectorPropertyView.ItemsSourceProperty, ItemsSourcePropertyBinding);
                view.SetBinding(SelectorPropertyView.CommandProperty, CommandBinding);
                view.SetBinding(SelectorPropertyView.SelectedIndexProperty, PropertyIndexBinding);
                view.SetBinding(SelectorPropertyView.DisplayMemberPathProperty, DisplayMemberPathBinding);

                return view;
            }));
            // SelectorProperty
            PropertyViewBuilders.Add(new PropertyViewBuilder(typeof(SelectorProperty<>), prop =>
            {
                var vmtype = typeof(SelectorPropertyViewModel<>).MakeGenericType(prop.GetType().GenericTypeArguments);

                //Activator.CreateInstance(vmtype, prop);

                var view = new SelectorPropertyViewGen()
                {
                    DataContext = Activator.CreateInstance(vmtype, prop)
                };

                view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);
                
                view.SetBinding(SelectorPropertyViewGen.ItemsSourceProperty, ItemsSourcePropertyBinding);
                view.SetBinding(SelectorPropertyViewGen.CommandProperty, CommandBinding);
                view.SetBinding(SelectorPropertyViewGen.SelectedItemProperty, PropertySelectItemBinding);
                view.SetBinding(SelectorPropertyViewGen.DisplayMemberPathProperty, DisplayMemberPathBinding);

                return view;
            }));
            // ValueProperty
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<ValueProperty>(prop =>
            {
                var view = new ValuePropertyView()
                {
                    DataContext = new ValuePropertyViewModel(prop)
                };

                view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);
                
                view.SetBinding(ValuePropertyView.ValueProperty, PropertyValueBinding);
                view.SetBinding(ValuePropertyView.GotFocusCommandProperty, GotFocusBinding);
                view.SetBinding(ValuePropertyView.LostFocusCommandProperty, LostFocusBinding);
                view.SetBinding(ValuePropertyView.PreviewMouseWheelCommandProperty, PreviewMouseWheelBinding);
                view.SetBinding(ValuePropertyView.KeyDownCommandProperty, TextChangedBinding);

                return view;
            }));
            // TextProperty
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<TextProperty>(prop =>
            {
                var view = new TextPropertyView()
                {
                    DataContext = new TextPropertyViewModel(prop)
                };

                view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);
                
                view.SetBinding(TextPropertyView.TextProperty, PropertyValueBinding);
                view.SetBinding(TextPropertyView.GotFocusCommandProperty, GotFocusBinding);
                view.SetBinding(TextPropertyView.LostFocusCommandProperty, LostFocusBinding);
                view.SetBinding(TextPropertyView.TextChangedCommandProperty, TextChangedBinding);

                return view;
            }));
            // ButtonComponent
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<ButtonComponent>(prop =>
            {
                var view = new ButtonComponentView()
                {
                    DataContext = new ButtonComponentViewModel(prop)
                };

                view.SetBinding(ButtonComponentView.TextProperty, HeaderBinding);
                view.SetBinding(ButtonComponentView.CommandProperty, CommandBinding);

                return view;
            }));
            // FolderProperty
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<FolderProperty>(prop =>
            {
                var view = new FolderPropertyView()
                {
                    DataContext = new FolderPropertyViewModel(prop)
                };

                view.SetBinding(BasePropertyView.HeaderProperty, HeaderBinding);
                view.SetBinding(BasePropertyView.ResetCommandProperty, ResetBinding);
                view.SetBinding(BasePropertyView.BindCommandProperty, BindBinding);
                
                view.SetBinding(FolderPropertyView.ModeIndexProperty, FileModeBinding);
                view.SetBinding(FolderPropertyView.FolderProperty, PropertyValueBinding);
                view.SetBinding(FolderPropertyView.OpenFolderCommandProperty, CommandBinding);

                return view;
            }));
            // LabelComponent
            PropertyViewBuilders.Add(PropertyViewBuilder.Create<LabelComponent>(prop =>
            {
                var view = new LabelComponentView()
                {
                    DataContext = prop
                };

                view.SetBinding(LabelComponentView.TextProperty, "Text");

                return view;
            }));
            #endregion

            #region CreateKeyFrameView
            // EaseProperty
            KeyFrameViewBuilders.Add(KeyFrameViewBuilder.Create<EaseProperty>(prop => new KeyFrame(prop)));
            // ColorAnimation
            KeyFrameViewBuilders.Add(KeyFrameViewBuilder.Create<ColorAnimationProperty>(prop => new KeyFrame(prop)));
            // ExpandGroup
            KeyFrameViewBuilders.Add(KeyFrameViewBuilder.Create<ExpandGroup>(group =>
            {
                var expander = new ExpandTree()
                {
                    Header = group.PropertyMetadata?.Name ?? "",
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

                expander.SetBinding(ExpandTree.IsExpandedProperty, new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = group });

                expander.ExpanderUpdate();

                return expander;
            }));
            // Group
            KeyFrameViewBuilders.Add(KeyFrameViewBuilder.Create<Group>((group) =>
            {
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
            }));
            #endregion
        }

        public static List<PropertyViewBuilder> PropertyViewBuilders { get; } = new List<PropertyViewBuilder>();
        public static List<KeyFrameViewBuilder> KeyFrameViewBuilders { get; } = new List<KeyFrameViewBuilder>();
        public static readonly EditingProperty<UIElement> PropertyViewProperty = EditingProperty.Register<UIElement, PropertyElement>("GetPropertyView");
        public static readonly EditingProperty<UIElement> ClipPropertyViewProperty = EditingProperty.Register<UIElement, ClipElement>("GetPropertyView");
        public static readonly EditingProperty<UIElement> EasePropertyViewProperty = EditingProperty.Register<UIElement, EasingFunc>("GetPropertyView");
        public static readonly EditingProperty<UIElement> KeyFrameViewProperty = EditingProperty.Register<UIElement, IKeyFrameProperty>("GetKeyFrameView");
        public static readonly EditingProperty<ClipUI> ClipViewProperty = EditingProperty.Register<ClipUI, ClipElement>("GetClipView");
        public static readonly EditingProperty<ClipUIViewModel> ClipViewModelProperty = EditingProperty.Register<ClipUIViewModel, ClipElement>("GetClipViewModel");
        public static readonly EditingProperty<UIElement> EffectPropertyViewProperty = EditingProperty.Register<UIElement, EffectElement>("GetControl");
        public static readonly EditingProperty<ExpandTree> KeyFrameProperty = EditingProperty.Register<ExpandTree, EffectElement>("GetKeyFrame");
        public static readonly EditingProperty<TimeLine> TimeLineProperty = EditingProperty.Register<TimeLine, Scene>("GetTimeLine");
        public static readonly EditingProperty<TimeLineViewModel> TimeLineViewModelProperty = EditingProperty.Register<TimeLineViewModel, Scene>("GetTimeLineViewModel");
        public static readonly EditingProperty<PropertyTab> PropertyTabProperty = EditingProperty.Register<PropertyTab, Scene>("GetPropertyTab");

        public static UIElement GetCreatePropertyView(this PropertyElement property)
        {
            if (property[PropertyViewProperty] is null)
            {
                var type = property.GetType();
                var func = PropertyViewBuilders.Find(x =>
                {
                    if (type.IsGenericType)
                    {
                        return type.GetGenericTypeDefinition() == x.PropertyType;
                    }

                    return type == x.PropertyType || type.IsSubclassOf(x.PropertyType);
                });

                property[PropertyViewProperty] = func?.CreateFunc?.Invoke(property) ?? new TextBlock() { Height = 32.5 };
            }
            return property.GetValue(PropertyViewProperty);
        }
        public static UIElement GetCreateKeyFrameView(this IKeyFrameProperty property)
        {
            if (property[KeyFrameViewProperty] is null)
            {
                var type = property.GetType();
                var func = KeyFrameViewBuilders.Find(x => type == x.PropertyType || type.IsSubclassOf(x.PropertyType));

                property[KeyFrameViewProperty] = func?.CreateFunc?.Invoke(property) ?? new TextBlock();
            }
            return property.GetValue(KeyFrameViewProperty);
        }
        public static ClipUI GetCreateClipView(this ClipElement clip)
        {
            if (clip[ClipViewProperty] is null)
            {
                clip[ClipViewProperty] = new ClipUI(clip)
                {
                    Name = clip.Name,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                };
            }
            return clip.GetValue(ClipViewProperty);
        }
        public static ClipUIViewModel GetCreateClipViewModel(this ClipElement clip)
        {
            if (clip[ClipViewModelProperty] is null)
            {
                clip[ClipViewModelProperty] = new ClipUIViewModel(clip);
            }
            return clip.GetValue(ClipViewModelProperty);
        }
        public static UIElement GetCreatePropertyView(this EffectElement effect)
        {
            if (effect[EffectPropertyViewProperty] is null)
            {
                ExpandTree expander;
                VirtualizingStackPanel stack;

                if (effect is ObjectElement @object)
                {
                    (expander, stack) = CreateTreeObject(@object);
                }
                else
                {
                    (expander, stack) = CreateTreeEffect(effect);
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

                effect[EffectPropertyViewProperty] = expander;
            }
            return effect.GetValue(EffectPropertyViewProperty);
        }
        public static ExpandTree GetCreateKeyFrameView(this EffectElement effect)
        {
            if (effect[KeyFrameProperty] is null)
            {
                var keyFrame = new ExpandTree() { HeaderHeight = Setting.ClipHeight + 1 };

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

                keyFrame.SetBinding(ExpandTree.HeaderProperty, new Binding("Name") { Mode = BindingMode.OneTime, Source = effect });
                keyFrame.SetBinding(ExpandTree.IsExpandedProperty, new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = effect });

                //エクスパンダーをアップデート
                keyFrame.ExpanderUpdate();

                effect[KeyFrameProperty] = keyFrame;
            }
            return effect.GetValue(KeyFrameProperty);
        }
        public static UIElement GetCreatePropertyView(this ClipElement clip)
        {
            if (clip[ClipPropertyViewProperty] is null)
            {
                clip[ClipPropertyViewProperty] = new Object_Setting(clip);
            }
            return clip.GetValue(ClipPropertyViewProperty);
        }
        public static TimeLine GetCreateTimeLineView(this Scene scene)
        {
            if (scene[TimeLineProperty] is null)
            {
                scene[TimeLineProperty] = new TimeLine(scene);
            }
            return scene.GetValue(TimeLineProperty);
        }
        public static TimeLineViewModel GetCreateTimeLineViewModel(this Scene scene)
        {
            if (scene[TimeLineViewModelProperty] is null)
            {
                scene[TimeLineViewModelProperty] = new TimeLineViewModel(scene);
            }
            return scene.GetValue(TimeLineViewModelProperty);
        }
        public static PropertyTab GetCreatePropertyTab(this Scene scene)
        {
            if (scene[PropertyTabProperty] is null)
            {
                scene[PropertyTabProperty] = new PropertyTab(scene);
            }
            return scene.GetValue(PropertyTabProperty);
        }
        public static UIElement GetCreatePropertyView(this EasingFunc easing)
        {
            if (easing[EasePropertyViewProperty] is null)
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

                easing[EasePropertyViewProperty] = _createdControl;
            }
            return easing.GetValue(EasePropertyViewProperty);
        }
        public static (ExpandTree, VirtualizingStackPanel) CreateTreeObject(ObjectElement obj)
        {
            var _expander = new ExpandTree()
            {
                HeaderHeight = 35d
            };

            var stack = new VirtualizingStackPanel() { Margin = new Thickness(32.5, 0, 0, 0) };
            VirtualizingPanel.SetIsVirtualizing(stack, true);
            VirtualizingPanel.SetVirtualizationMode(stack, VirtualizationMode.Recycling);


            var header = new VirtualizingStackPanel() { Orientation = Orientation.Horizontal };
            VirtualizingPanel.SetIsVirtualizing(header, true);
            VirtualizingPanel.SetVirtualizationMode(header, VirtualizationMode.Recycling);

            _expander.Header = header;

            var checkBox = new CheckBox()
            {
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var textBlock = new TextBlock()
            {
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            header.Children.Add(checkBox);
            header.Children.Add(textBlock);


            #region コンテキストメニュー
            var menuListBox = new ContextMenu();
            var Delete = new MenuItem();

            //削除項目の設定
            var menu = new VirtualizingStackPanel()
            {
                Orientation = Orientation.Horizontal
            };
            menu.Children.Add(new PackIcon()
            {
                Kind = PackIconKind.Delete,
                Margin = new Thickness(5, 0, 5, 0)
            });
            menu.Children.Add(new TextBlock()
            {
                Text = Resources.Remove,
                Margin = new Thickness(20, 0, 5, 0)
            });
            Delete.Header = menu;

            menuListBox.Items.Add(Delete);

            // 作成したコンテキストメニューをListBox1に設定
            _expander.ContextMenu = menuListBox;
            #endregion

            #region イベント
            checkBox.Click += (sender, e) =>
            {
                obj.ChangeIsEnabled((bool)((CheckBox)sender).IsChecked!).Execute();
            };

            #endregion

            #region Binding

            var isenablebinding = new Binding("IsEnabled")
            {
                Mode = BindingMode.OneWay,
                Source = obj
            };
            checkBox.SetBinding(ToggleButton.IsCheckedProperty, isenablebinding);

            var textbinding = new Binding("Name")
            {
                Mode = BindingMode.OneTime,
                Source = obj
            };
            textBlock.SetBinding(TextBlock.TextProperty, textbinding);

            var isExpandedbinding = new Binding("IsExpanded")
            {
                Mode = BindingMode.TwoWay,
                Source = obj
            };
            _expander.SetBinding(ExpandTree.IsExpandedProperty, isExpandedbinding);

            _expander.SetResourceReference(ExpandTree.HeaderColorProperty, "MaterialDesignBody");

            #endregion

            _expander.Content = stack;

            return (_expander, stack);
        }
        public static (ExpandTree, VirtualizingStackPanel) CreateTreeEffect(EffectElement effect)
        {
            var data = effect.Parent;

            var _expander = new ExpandTree() { HeaderHeight = 35d };

            var stack = new VirtualizingStackPanel() { Margin = new Thickness(32, 0, 0, 0) };
            VirtualizingPanel.SetIsVirtualizing(stack, true);
            VirtualizingPanel.SetVirtualizationMode(stack, VirtualizationMode.Recycling);

            #region Header

            var header = new VirtualizingStackPanel() { Orientation = Orientation.Horizontal };
            VirtualizingPanel.SetIsVirtualizing(header, true);
            VirtualizingPanel.SetVirtualizationMode(header, VirtualizationMode.Recycling);

            _expander.Header = header;

            var checkBox = new CheckBox()
            {
                Margin = new(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var upbutton = new Button()
            {
                Content = new PackIcon() { Kind = PackIconKind.ChevronUp },
                Margin = new Thickness(5, 0, 0, 0),
                Background = null,
                BorderBrush = null,
                VerticalAlignment = VerticalAlignment.Center
            };
            var downbutton = new Button()
            {
                Content = new PackIcon() { Kind = PackIconKind.ChevronDown },
                Margin = new Thickness(0, 0, 5, 0),
                Background = null,
                BorderBrush = null,
                VerticalAlignment = VerticalAlignment.Center
            };
            var textBlock = new TextBlock()
            {
                Margin = new(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
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
            menu.Children.Add(new PackIcon() { Kind = PackIconKind.Delete, Margin = new Thickness(5, 0, 5, 0) });
            menu.Children.Add(new TextBlock() { Text = Resources.Remove, Margin = new Thickness(20, 0, 5, 0) });
            Delete.Header = menu;

            menuListBox.Items.Add(Delete);

            // 作成したコンテキストメニューをListBox1に設定
            _expander.ContextMenu = menuListBox;
            #endregion

            #region イベント

            checkBox.Click += (sender, e) => effect.ChangeIsEnabled((bool)((CheckBox)sender).IsChecked!).Execute();

            upbutton.Click += (sender, e) => effect.BringForward().Execute();

            downbutton.Click += (sender, e) => effect.SendBackward().Execute();

            Delete.Click += (sender, e) => effect.Parent!.RemoveEffect(effect).Execute();

            #endregion

            #region Binding

            var isenablebinding = new Binding("IsEnabled")
            {
                Mode = BindingMode.OneWay,
                Source = effect
            };
            checkBox.SetBinding(ToggleButton.IsCheckedProperty, isenablebinding);

            var textbinding = new Binding("Name")
            {
                Mode = BindingMode.OneTime,
                Source = effect
            };
            textBlock.SetBinding(TextBlock.TextProperty, textbinding);

            var isExpandedbinding = new Binding("IsExpanded")
            {
                Mode = BindingMode.TwoWay,
                Source = effect
            };
            _expander.SetBinding(ExpandTree.IsExpandedProperty, isExpandedbinding);

            _expander.SetResourceReference(ExpandTree.HeaderColorProperty, "MaterialDesignBody");

            #endregion

            _expander.Content = stack;

            return (_expander, stack);
        }

        public record PropertyViewBuilder(Type PropertyType, Func<PropertyElement, UIElement> CreateFunc)
        {
            public static PropertyViewBuilder Create<T>(Func<T, UIElement> CreateFunc) where T : PropertyElement
            {
                return new(typeof(T), (p) => CreateFunc((T)p));
            }
        }

        public record KeyFrameViewBuilder(Type PropertyType, Func<IKeyFrameProperty, UIElement> CreateFunc)
        {
            public static KeyFrameViewBuilder Create<T>(Func<T, UIElement> CreateFunc) where T : IKeyFrameProperty
            {
                return new(typeof(T), (p) => CreateFunc((T)p));
            }
        }
    }
}
