using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Beutl.Language;
using Beutl.Services;
using Beutl.Services.Tutorials;
using Beutl.ViewModels;

namespace Beutl.Views.Tutorial;

public partial class TutorialOverlay : UserControl
{
    private readonly List<Border> _highlightBorders = [];
    private readonly List<Control> _currentTargets = [];
    private Control? _primaryTarget;

    public TutorialOverlay()
    {
        InitializeComponent();
        IsVisible = false;
        OverlayBackdrop.PointerPressed += OnBackdropPointerPressed;
    }

    public void UpdateState(TutorialState? state)
    {
        if (state == null)
        {
            IsVisible = false;
            ClearHighlights();
            return;
        }

        IsVisible = true;
        TutorialStep step = state.CurrentStep;

        TipTitle.Text = step.Title;
        TipContent.Text = step.Content;
        StepCounter.Text = string.Format(TutorialStrings.TutorialStep, state.CurrentStepIndex + 1, state.TotalSteps);

        PreviousButton.IsVisible = !state.IsFirstStep;
        NextButton.IsVisible = !step.IsActionRequired || state.IsLastStep;
        NextButton.Content = state.IsLastStep ? TutorialStrings.TutorialFinish : TutorialStrings.TutorialNext;

        UpdateTargetHighlight(step);
    }

    private void UpdateTargetHighlight(TutorialStep step)
    {
        ResolveTargetElements(step);

        if (_currentTargets.Count > 0)
        {
            PositionHighlights();
            if (_primaryTarget != null)
            {
                PositionTip(_primaryTarget, step.PreferredPlacement);
            }
            else
            {
                PositionTip(_currentTargets[0], step.PreferredPlacement);
            }
        }
        else
        {
            ClearHighlights();
            // ターゲットがない場合は中央に表示
            TipContainer.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            TipContainer.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        }
    }

    private void ResolveTargetElements(TutorialStep step)
    {
        _currentTargets.Clear();
        _primaryTarget = null;

        if (step.TargetElements == null || step.TargetElements.Count == 0)
            return;

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        foreach (TargetElementDefinition definition in step.TargetElements)
        {
            Control? target = ResolveTargetElement(definition, topLevel);
            if (target != null)
            {
                _currentTargets.Add(target);
                if (definition.IsPrimary)
                {
                    _primaryTarget = target;
                }
            }
        }

        // フォールバック: IsPrimaryが設定されていない場合、最初の要素をprimaryとする
        if (_primaryTarget == null && _currentTargets.Count > 0)
        {
            _primaryTarget = _currentTargets[0];
        }
    }

    private Control? ResolveTargetElement(TargetElementDefinition definition, TopLevel topLevel)
    {
        if (definition.ElementResolver != null)
        {
            return definition.ElementResolver() as Control;
        }

        if (definition.ToolTabType != null)
        {
            return ResolveToolTabElement(definition.ToolTabType, topLevel);
        }

        if (definition.ElementName != null)
        {
            return topLevel.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(c => c.Name == definition.ElementName);
        }

        return null;
    }

    private static Control? ResolveToolTabElement(Type extensionType, TopLevel topLevel)
    {
        return topLevel.GetVisualDescendants()
            .OfType<ToolTabContent>()
            .FirstOrDefault(ttc =>
                ttc.DataContext is ToolTabViewModel vm &&
                extensionType.IsInstanceOfType(vm.Context.Extension));
    }

    private void PositionHighlights()
    {
        // 必要に応じてボーダーを追加
        EnsureBorderPool(_currentTargets.Count);

        var targetRects = new List<Rect>();

        for (int i = 0; i < _currentTargets.Count; i++)
        {
            Control target = _currentTargets[i];
            Border border = _highlightBorders[i];

            try
            {
                Point? pos = target.TranslatePoint(new Point(0, 0), this);
                if (pos.HasValue)
                {
                    border.IsVisible = true;
                    Canvas.SetLeft(border, pos.Value.X - 4);
                    Canvas.SetTop(border, pos.Value.Y - 4);
                    border.Width = target.Bounds.Width + 8;
                    border.Height = target.Bounds.Height + 8;

                    targetRects.Add(new Rect(
                        pos.Value.X - 4, pos.Value.Y - 4,
                        target.Bounds.Width + 8, target.Bounds.Height + 8));
                }
                else
                {
                    border.IsVisible = false;
                }
            }
            catch
            {
                border.IsVisible = false;
            }
        }

        // 残りのボーダーを非表示
        for (int i = _currentTargets.Count; i < _highlightBorders.Count; i++)
        {
            _highlightBorders[i].IsVisible = false;
        }

        // オーバーレイ背景にクリッピングを適用
        UpdateOverlayClip(targetRects);
    }

    private void EnsureBorderPool(int count)
    {
        while (_highlightBorders.Count < count)
        {
            var border = new Border
            {
                BorderBrush = Application.Current?.FindResource("AccentFillColorDefaultBrush") as IBrush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                IsVisible = false
            };
            _highlightBorders.Add(border);
            HighlightCanvas.Children.Add(border);
        }
    }

    private void UpdateOverlayClip(List<Rect> targetRects, bool dispatchUpdate = true)
    {
        // Boundsが0の場合はDispatcherで遅延させる（まだレイアウトが完了していない可能性があるため）
        if (Bounds.Width == 0 || Bounds.Height == 0)
        {
            if (dispatchUpdate)
            {
                Dispatcher.UIThread.Post(() => UpdateOverlayClip(targetRects, false), DispatcherPriority.Render);
            }

            return;
        }

        if (targetRects.Count == 0)
        {
            OverlayBackdrop.Clip = null;
            return;
        }

        Geometry result = new RectangleGeometry(new Rect(0, 0, Bounds.Width, Bounds.Height));

        foreach (Rect rect in targetRects)
        {
            var cutout = new RectangleGeometry(rect);
            result = new CombinedGeometry(GeometryCombineMode.Exclude, result, cutout);
        }

        OverlayBackdrop.Clip = result;
    }

    private void PositionTip(Control target, TutorialStepPlacement placement)
    {
        try
        {
            Point? pos = target.TranslatePoint(new Point(0, 0), this);
            if (!pos.HasValue)
                return;

            double targetLeft = pos.Value.X;
            double targetRight = pos.Value.X + target.Bounds.Width;
            double targetCenterX = pos.Value.X + target.Bounds.Width / 2;
            double targetCenterY = pos.Value.Y + target.Bounds.Height / 2;
            double targetBottom = pos.Value.Y + target.Bounds.Height;
            double targetTop = pos.Value.Y;

            TipContainer.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            TipContainer.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;

            double tipLeft = Math.Max(16, targetCenterX - 200);
            double tipTop;

            if (placement == TutorialStepPlacement.Top)
            {
                tipTop = targetTop - 16;
                TipContainer.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
                TipContainer.Margin = new Thickness(tipLeft, 0, 0, tipTop);
            }
            else if (placement == TutorialStepPlacement.Left)
            {
                tipLeft = Bounds.Width - (targetLeft - 16);
                tipTop = targetCenterY;
                TipContainer.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                TipContainer.Margin = new Thickness(0, tipTop, tipLeft, 0);
            }
            else if (placement == TutorialStepPlacement.Right)
            {
                tipLeft = targetRight + 16;
                tipTop = targetCenterY;
                TipContainer.Margin = new Thickness(tipLeft, tipTop, 0, 0);
            }
            else if (placement == TutorialStepPlacement.Bottom)
            {
                tipTop = targetBottom + 16;
                TipContainer.Margin = new Thickness(tipLeft, tipTop, 0, 0);
            }
        }
        catch
        {
            TipContainer.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            TipContainer.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            TipContainer.Margin = default;
        }
    }

    private void ClearHighlights()
    {
        foreach (Border border in _highlightBorders)
        {
            border.IsVisible = false;
        }

        OverlayBackdrop.Clip = null;
        TipContainer.Margin = default;
        _currentTargets.Clear();
        _primaryTarget = null;
    }

    private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 背景クリックで何もしない（イベントを消費してオーバーレイの下にクリックが通らないようにする）
        e.Handled = true;
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e)
    {
        TutorialService.Current?.CancelTutorial();
    }

    private void OnPreviousClick(object? sender, RoutedEventArgs e)
    {
        TutorialService.Current?.PreviousStep();
    }

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        TutorialState? state = TutorialService.Current.GetCurrentState();
        if (state?.IsLastStep == true)
        {
            TutorialService.Current?.CancelTutorial();
        }
        else
        {
            TutorialService.Current?.AdvanceStep();
        }
    }
}
