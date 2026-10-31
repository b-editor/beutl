using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class TimelineTabView
{
    private readonly List<ViewportElement> _viewportElements = [];
    private readonly Stack<ElementView> _recycledElementViews = [];
    private readonly List<CompactElementLayer> _compactLayers = [];
    private bool _viewportUpdateQueued;
    private bool _compactPaintQueued;
    private ViewportElement? _activeCompactElement;

    // Geometry is cached independently of controls, including drag previews. Testing 20,000 small
    // rectangles is cheap; constructing, binding and laying out 20,000 controls is not. Both axes
    // matter: vertical-only virtualization still realizes every clip on a densely populated row.
    internal void InvalidateElementViewport()
    {
        if (_viewportUpdateQueued || ViewModel is null) return;
        _viewportUpdateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _viewportUpdateQueued = false;
            UpdateElementViewport();
        }, DispatcherPriority.Render);
    }

    private void AddElement(int index, ElementViewModel model)
    {
        _viewportElements.Insert(index, new ViewportElement(model, InvalidateElementViewport, InvalidateCompactPaint));
        InvalidateElementViewport();
    }

    private void RemoveElement(int index, ElementViewModel model)
    {
        ViewportElement entry = _viewportElements[index];
        _viewportElements.RemoveAt(index);
        entry.Dispose();
        RecycleElement(entry);
        if (ReferenceEquals(_activeCompactElement, entry)) _activeCompactElement = null;
        RemoveScope(entry);
        InvalidateElementViewport();
    }

    private void ClearElementViewport()
    {
        _activeCompactElement = null;
        foreach (ViewportElement entry in _viewportElements)
        {
            entry.Dispose();
            RecycleElement(entry);
            RemoveScope(entry);
        }
        _viewportElements.Clear();
        foreach (CompactElementLayer layer in _compactLayers) TimelinePanel.Children.Remove(layer);
        _compactLayers.Clear();
    }

    private void RecycleElement(ViewportElement entry)
    {
        if (entry.View is not { } view) return;
        TimelinePanel.Children.Remove(view);
        view.DataContext = null;
        entry.View = null;
        // A bounded pool avoids rebuilding the XAML/context menus during ordinary scrolling.
        if (_recycledElementViews.Count < 256) _recycledElementViews.Push(view);
    }

    private void RemoveScope(ViewportElement entry)
    {
        if (entry.Scope is not { } scope) return;
        TimelinePanel.Children.Remove(scope);
        scope.DataContext = null;
        entry.Scope = null;
    }

    private void InvalidateCompactPaint()
    {
        if (_compactPaintQueued) return;
        _compactPaintQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _compactPaintQueued = false;
            foreach (CompactElementLayer layer in _compactLayers) layer.InvalidateVisual();
        }, DispatcherPriority.Render);
    }

    private ElementView? RealizeCompactElement(ViewportElement entry)
    {
        if (ViewModel is null || !entry.ShowView) return null;
        _activeCompactElement = entry;
        UpdateElementViewport();
        TimelinePanel.UpdateLayout();
        return entry.View;
    }

    private void OnElementPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_activeCompactElement is { } active && !active.Bounds.Contains(e.GetPosition(TimelinePanel)))
        {
            _activeCompactElement = null;
            InvalidateElementViewport();
        }
    }

    private void OnElementPointerExited(object? sender, PointerEventArgs e)
    {
        _activeCompactElement = null;
        InvalidateElementViewport();
    }

    private void UpdateElementViewport()
    {
        if (ViewModel is null || ContentScroll.Viewport.Width <= 0 || ContentScroll.Viewport.Height <= 0) return;

        var visibleViewport = new Rect(ContentScroll.Offset.X, ContentScroll.Offset.Y,
            ContentScroll.Viewport.Width, ContentScroll.Viewport.Height);
        Rect viewport = visibleViewport.Inflate(new Thickness(200, 50));

        foreach (ViewportElement entry in _viewportElements)
        {
            entry.ShowView = entry.Bounds.Intersects(viewport) || entry.View?.KeepRealized == true;
            entry.ShowDetails = entry.ShowView && (entry.Bounds.Width >= ElementViewModel.MinimumThumbnailWidth
                || ReferenceEquals(entry, _activeCompactElement) || entry.View?.KeepRealized == true);
            entry.ShowScope = entry.ScopeBounds.Height > 0 && entry.ScopeBounds.Intersects(viewport);
            if (!entry.ShowDetails) RecycleElement(entry);
            if (!entry.ShowView || entry.ShowDetails) entry.ClearRename();
            if (!entry.ShowScope) RemoveScope(entry);
        }

        // Keep scene order even when scrolling backwards or inserting clips. Overlapping clips
        // with the same ZIndex must keep the same paint/hit-test order after recycling.
        int childIndex = TimelinePanel.Children.IndexOf(overlay) + 1; // Keep the XAML decorations first.
        int layerIndex = 0;
        List<ViewportElement>? compactRun = null;
        foreach (ViewportElement entry in _viewportElements)
        {
            if (entry.ShowDetails)
            {
                FlushCompactRun();
                if (entry.View is null)
                {
                    entry.View = _recycledElementViews.TryPop(out ElementView? recycled)
                        ? recycled : new ElementView();
                    entry.View.DataContext = entry.Model;
                    entry.View.ZIndex = entry.Model.IsSelected.Value ? 5 : 0;
                }
                entry.View.UseLayoutRounding = entry.Bounds.Width >= ElementViewModel.MinimumThumbnailWidth;
                Place(entry.View, childIndex++);
            }
            else if (entry.ShowView)
            {
                (compactRun ??= []).Add(entry);
                entry.Rename ??= () =>
                {
                    if (RealizeCompactElement(entry) is { }) entry.Model.RenameRequested();
                };
                entry.Model.RenameRequested = entry.Rename;
            }
            if (entry.ShowScope)
            {
                FlushCompactRun();
                entry.Scope ??= new ElementScopeView { DataContext = entry.Model.Scope };
                Place(entry.Scope, childIndex++);
            }
        }
        FlushCompactRun();
        while (_compactLayers.Count > layerIndex)
        {
            TimelinePanel.Children.Remove(_compactLayers[^1]);
            _compactLayers.RemoveAt(_compactLayers.Count - 1);
        }

        void FlushCompactRun()
        {
            if (compactRun is null) return;
            for (int selection = 0; selection < 2; selection++)
            {
                if (layerIndex == _compactLayers.Count)
                    _compactLayers.Add(new CompactElementLayer(selection == 1, RealizeCompactElement));
                CompactElementLayer layer = _compactLayers[layerIndex++];
                layer.Update(compactRun, visibleViewport);
                Place(layer, childIndex++);
            }
            compactRun = null;
        }
    }

    private void Place(Control control, int index)
    {
        var children = TimelinePanel.Children;
        if (index < children.Count && ReferenceEquals(children[index], control)) return;
        // Layers of runs that scrolled away stay until the end of the pass and can sit before a
        // retained view. Move it rather than detaching it, which would end its drag or menu.
        int current = children.IndexOf(control);
        if (current >= 0) children.Move(current, index);
        else children.Insert(index, control);
    }

    internal sealed class ViewportElement : IDisposable
    {
        private readonly CompositeDisposable _subscriptions = [];

        public ViewportElement(ElementViewModel model, Action invalidate, Action invalidatePaint)
        {
            Model = model;
            void Update()
            {
                Bounds = new Rect(model.BorderMargin.Value.Left, model.Margin.Value.Top,
                    Math.Max(0, model.Width.Value), FrameNumberHelper.LayerHeight);
                ScopeBounds = new Rect(model.Scope.Margin.Value.Left, model.Scope.Margin.Value.Top,
                    Math.Max(0, model.Scope.Width.Value), Math.Max(0, model.Scope.Height.Value));
                invalidate();
            }

            model.Margin.Subscribe(_ => Update()).DisposeWith(_subscriptions);
            model.BorderMargin.Subscribe(_ => Update()).DisposeWith(_subscriptions);
            model.Width.Subscribe(_ => Update()).DisposeWith(_subscriptions);
            model.Scope.Margin.Subscribe(_ => Update()).DisposeWith(_subscriptions);
            model.Scope.Width.Subscribe(_ => Update()).DisposeWith(_subscriptions);
            model.Scope.Height.Subscribe(_ => Update()).DisposeWith(_subscriptions);
            model.Color.Subscribe(color =>
            {
                FillBrush = new ImmutableSolidColorBrush(color);
                invalidatePaint();
            }).DisposeWith(_subscriptions);
            model.RestBorderColor.Subscribe(color =>
            {
                RestBorderPen = new Pen(new ImmutableSolidColorBrush(color));
                invalidatePaint();
            }).DisposeWith(_subscriptions);
            model.IsSelected.Subscribe(_ => invalidatePaint()).DisposeWith(_subscriptions);
            model.IsEnabled.Subscribe(_ => invalidatePaint()).DisposeWith(_subscriptions);
            model.IsEditable.Subscribe(_ => invalidatePaint()).DisposeWith(_subscriptions);
        }

        public ElementViewModel Model { get; }
        public Rect Bounds { get; private set; }
        public Rect ScopeBounds { get; private set; }
        public ElementView? View { get; set; }
        public IBrush FillBrush { get; private set; } = Brushes.Transparent;
        public IPen RestBorderPen { get; private set; } = new Pen(Brushes.Transparent);
        public Action? Rename { get; set; }
        public ElementScopeView? Scope { get; set; }
        public bool ShowView { get; set; }
        public bool ShowDetails { get; set; }
        public bool ShowScope { get; set; }
        public void ClearRename()
        {
            if (Rename is not null && ReferenceEquals(Model.RenameRequested, Rename))
                Model.RenameRequested = () => { };
        }

        public void Dispose()
        {
            ClearRename();
            _subscriptions.Dispose();
        }
    }
}
