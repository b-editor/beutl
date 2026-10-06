using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings.Extensions;
using Setter = Avalonia.Styling.Setter;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class ElementView
{
    private sealed class _SelectBehavior : Behavior<ElementView>
    {
        private bool _pressedWithModifier;
        private Thickness _snapshot;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject == null) return;

            AssociatedObject.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            AssociatedObject.border.AddHandler(PointerPressedEvent, OnBorderPointerPressed);
            AssociatedObject.border.AddHandler(PointerReleasedEvent, OnBorderPointerReleased);
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject == null) return;

            AssociatedObject.RemoveHandler(PointerPressedEvent, OnPointerPressed);
            AssociatedObject.border.RemoveHandler(PointerPressedEvent, OnBorderPointerPressed);
            AssociatedObject.border.RemoveHandler(PointerReleasedEvent, OnBorderPointerReleased);
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is not { } obj) return;

            if (!obj.textBox.IsFocused)
            {
                obj.Focus();
            }
        }

        private void Select(ElementView obj, TimelineTabViewModel timeline)
        {
            var selection = timeline.EditorContext.GetRequiredService<IEditorSelection>();
            selection.SelectedObject.Value = obj.ViewModel.Model;

            timeline.ClearSelected();
            timeline.SelectElement(obj.ViewModel);
        }

        private bool IsSelected(ElementView obj, TimelineTabViewModel timeline)
        {
            var selection = timeline.EditorContext.GetRequiredService<IEditorSelection>();
            return selection.SelectedObject.Value == obj.ViewModel.Model
                || timeline.SelectedElements.Contains(obj.ViewModel);
        }

        private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is { _timeline.ViewModel: { } timelineVm } obj)
            {
                if (timelineVm.IsRazorMode.Value && e.GetCurrentPoint(obj.border).Properties.IsLeftButtonPressed)
                {
                    if (obj.ViewModel is { } elementVm && elementVm.IsEditable.Value)
                    {
                        PointerPoint pt = e.GetCurrentPoint(obj.border);
                        float scale = timelineVm.Options.Value.Scale;
                        TimeSpan clickedTime = elementVm.Model.Start + pt.Position.X.PixelToTimeSpan(scale);
                        elementVm.SplitAt(clickedTime);
                    }

                    e.Handled = true;
                    return;
                }

                PointerPoint point = e.GetCurrentPoint(obj.border);
                if (point.Properties.IsLeftButtonPressed)
                {
                    if (e.ClickCount == 2)
                    {
                        if (obj.ViewModel is { IsEditable.Value: true })
                        {
                            obj.textBlock.IsVisible = false;
                            obj.textBox.IsVisible = true;
                            obj.textBox.SelectAll();
                            obj.textBox.Focus();
                        }
                    }
                    else
                    {
                        if (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Alt)
                        {
                            // In a trim mode, a plain press on an already-selected clip starts a
                            // multi-selection Slip/Roll/Slide; re-selecting here would collapse
                            // the selection to the pressed clip before those behaviors (which run
                            // after this one) collect their targets. Pressing an unselected clip
                            // still re-selects as usual.
                            bool trimMode = timelineVm.IsSlipMode.Value
                                || timelineVm.IsRollMode.Value
                                || timelineVm.IsSlideMode.Value;
                            if (!trimMode || !timelineVm.SelectedElements.Contains(obj.ViewModel))
                            {
                                Select(obj, obj._timeline.ViewModel);
                            }
                            else
                            {
                                // Keep the multi-selection, but still point the single selection
                                // (property tab, keyframe scope) at the pressed clip.
                                timelineVm.EditorContext.GetRequiredService<IEditorSelection>()
                                    .SelectedObject.Value = obj.ViewModel.Model;
                            }
                        }
                        else
                        {
                            Thickness margin = obj.ViewModel.Margin.Value;
                            Thickness borderMargin = obj.ViewModel.BorderMargin.Value;
                            _snapshot = new Thickness(borderMargin.Left, margin.Top, 0, 0);
                            _pressedWithModifier = true;
                        }
                    }
                }
                else if (point.Properties.IsRightButtonPressed)
                {
                    if (!IsSelected(obj, obj._timeline.ViewModel))
                    {
                        Select(obj, obj._timeline.ViewModel);
                    }
                }
            }
        }

        private void OnBorderPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (AssociatedObject is { _timeline.ViewModel: not null } obj)
            {
                if (_pressedWithModifier)
                {
                    Thickness margin = obj.ViewModel.Margin.Value;
                    Thickness borderMargin = obj.ViewModel.BorderMargin.Value;
                    // ReSharper disable CompareOfFloatsByEqualityOperator
                    if (borderMargin.Left == _snapshot.Left
                        && margin.Top == _snapshot.Top)
                    {
                        obj.ViewModel.Timeline.SwitchSelectedElement(obj.ViewModel);
                    }
                    // ReSharper restore CompareOfFloatsByEqualityOperator

                    _pressedWithModifier = false;
                }
            }
        }
    }
}
