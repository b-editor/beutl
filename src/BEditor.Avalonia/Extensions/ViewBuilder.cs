using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Properties;
using BEditor.ViewModels.Timelines;
using BEditor.Views.Properties;
using BEditor.Views.Timelines;

namespace BEditor.Extensions
{
    public static class ViewBuilder
    {
        public static readonly List<PropertyViewBuilder> PropertyViewBuilders = new();
        public static readonly EditingProperty<Timeline> TimelineProperty = EditingProperty.Register<Timeline, Scene>("GetTimeline");
        public static readonly EditingProperty<TimelineViewModel> TimelineViewModelProperty = EditingProperty.Register<TimelineViewModel, Scene>("GetTimelineViewModel");
        public static readonly EditingProperty<ClipView> ClipViewProperty = EditingProperty.Register<ClipView, ClipElement>("GetClipView");
        public static readonly EditingProperty<ClipPropertyView> ClipPropertyViewProperty = EditingProperty.Register<ClipPropertyView, ClipElement>("GetClipPropertyView");
        public static readonly EditingProperty<ClipViewModel> ClipViewModelProperty = EditingProperty.Register<ClipViewModel, ClipElement>("GetClipViewModel");
        public static readonly EditingProperty<Control> EffectElementViewProperty = EditingProperty.Register<Control, EffectElement>("GetPropertyView");
        public static readonly EditingProperty<Control> PropertyElementViewProperty = EditingProperty.Register<Control, PropertyElement>("GetPropertyView");

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
                //clip[EffectElementViewProperty] = new ClipPropertyView(clip);
            }
            return effect.GetValue(EffectElementViewProperty);
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
                }, effect);
            }
            return effect.GetValue(EffectElementViewProperty);
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
                Text = Strings.Remove,
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
        public static (Expander, VirtualizingStackPanel) CreateExpanderEffect(EffectElement effect)
        {
            var data = effect.Parent;

            var _expander = new Expander();

            var stack = new StackPanel { Margin = new Thickness(32, 0, 0, 0) };

            #region Header

            var header = new StackPanel { Orientation = Orientation.Horizontal };

            _expander.Header = header;

            var checkBox = new CheckBox
            {
                Margin = new(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var upbutton = new Button
            {
                Content = new PathIcon
                {
                    Data = Geometry.Parse("M4.21144 12.7328C3.92428 13.0313 3.93349 13.5061 4.232 13.7932C4.53052 14.0804 5.0053 14.0712 5.29246 13.7727L13.252 5.49844V24.2512C13.252 24.6655 13.5877 25.0012 14.002 25.0012C14.4162 25.0012 14.752 24.6655 14.752 24.2512V5.49942L22.7105 13.7727C22.9977 14.0712 23.4724 14.0804 23.771 13.7932C24.0695 13.5061 24.0787 13.0313 23.7915 12.7328L14.7222 3.30478C14.3287 2.8958 13.6742 2.8958 13.2808 3.30478L4.21144 12.7328Z")
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
                    Data = Geometry.Parse("M23.7915 15.2665C24.0787 14.968 24.0695 14.4932 23.771 14.2061C23.4724 13.9189 22.9977 13.9281 22.7105 14.2266L14.751 22.5009L14.751 3.74805C14.751 3.33383 14.4152 2.99805 14.001 2.99805C13.5868 2.99805 13.251 3.33383 13.251 3.74805L13.251 22.4999L5.29246 14.2266C5.00531 13.9281 4.53052 13.9189 4.232 14.2061C3.93349 14.4932 3.92428 14.968 4.21144 15.2665L13.2808 24.6945C13.6742 25.1035 14.3287 25.1035 14.7222 24.6945L23.7915 15.2665Z")
                },
                Margin = new Thickness(0, 0, 5, 0),
                Background = null,
                BorderBrush = null,
                VerticalAlignment = VerticalAlignment.Center
            };
            var textBlock = new TextBlock
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
            var menu = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            menu.Children.Add(new PathIcon
            {
                Kind = PackIconKind.Delete,
                Margin = new Thickness(5, 0, 5, 0)
            });
            menu.Children.Add(new TextBlock
            {
                Text = Strings.Remove,
                Margin = new Thickness(20, 0, 5, 0)
            });
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
