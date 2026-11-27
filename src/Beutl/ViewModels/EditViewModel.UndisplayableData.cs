using Avalonia.Threading;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.SceneSettingsTab;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.UndisplayableDataTab;
using Beutl.Editor.Components.UndisplayableDataTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.ViewModels;

public partial class EditViewModel : IUndisplayableDataNavigator
{
    internal void OpenUndisplayableData()
    {
        if (IsDisposingOrDisposed) return;
        if (FindToolTab<UndisplayableDataViewModel>() is { } existing)
        {
            existing.Refresh();
            OpenToolTab(existing);
        }
        else
        {
            DockHost.OpenToolTabFromExtension(UndisplayableDataTabExtension.Instance, target: null);
        }
    }

    private void ActivateAndOpenUndisplayableData()
    {
        if (IsDisposingOrDisposed) return;
        if (Scene.Uri is not null) EditorService.ActivateTabItem(Scene);
        OpenUndisplayableData();
    }

    void IUndisplayableDataNavigator.Locate(UndisplayableDataEntry entry)
    {
        if (IsDisposingOrDisposed || entry.Scene is not { } scene) return;
        if (ReferenceEquals(scene, Scene))
        {
            LocateInScene(entry);
        }
        else if (scene.Uri is not null)
        {
            EditorService.ActivateTabItem(scene);
            if (EditorService.TryGetTabItem(scene, out var tab) && tab.Context.Value is EditViewModel editor)
                editor.LocateInScene(entry);
        }
    }

    private void LocateInScene(UndisplayableDataEntry entry)
    {
        if (IsDisposingOrDisposed || !IsEnabled.Value) return;
        IEditorClock clock = this.GetRequiredService<IEditorClock>();
        if (entry.Element is not { } element)
        {
            if (entry.Marker is { } marker && Scene.Markers.Contains(marker))
            {
                MediaTime time = marker.Time.Value;
                if (!Player.IsPlaying.Value) clock.Seek(time);
                LocateOnTimeline(new TimeRange(time, new MediaTime(1)), null);
            }
            else if (entry.Layer is { } layer && Scene.Layers.Contains(layer))
            {
                LocateOnTimeline(new TimeRange(clock.CurrentTime.Value, new MediaTime(1)), layer.ZIndex.Value);
            }
            else if (FindToolTab<SceneSettingsTabViewModel>() is { } settings)
            {
                OpenToolTab(settings);
            }
            else
            {
                DockHost.OpenToolTabFromExtension(SceneSettingsTabExtension.Instance, target: null);
            }
            return;
        }

        if (!Scene.Children.Contains(element)) return;
        this.GetRequiredService<IEditorSelection>().Select(element, notifyIfSelected: true);

        if (!Player.IsPlaying.Value)
        {
            MediaTime current = clock.CurrentTime.Value;
            if (entry.Animation is { } animation && entry.KeyFrame is { } keyFrame)
            {
                clock.Seek(animation.ToScene(keyFrame.KeyTime.Value));
            }
            else if (current < element.Range.Start || current >= element.Range.End)
            {
                MediaTime last = (element.Range.End - new MediaTime(1))
                    .RoundToFrame(Player.GetFrameRate(), MidpointRounding.ToNegativeInfinity);
                clock.Seek(current < element.Range.Start || last < element.Range.Start ? element.Range.Start : last);
            }
        }

        LocateOnTimeline(element.Range, element.ZIndex.Value, element);
        Player.QueuePreviewRender();

        if (entry.TransitionEdge is { } edge)
        {
            this.GetRequiredService<ITransitionEditorService>().Edit(element, edge);
        }
        else if (entry.Animation is { } keyAnimation && entry.KeyFrame is not null)
        {
            RevealProperties(entry, element);
            GraphEditorTabViewModel graphEditor = GraphEditorTabViewModel.FindReusable(this, element, keyAnimation)
                                                   ?? new GraphEditorTabViewModel(this);
            graphEditor.Element.Value = element;
            graphEditor.Select(keyAnimation);
            if (!OpenToolTab(graphEditor)) graphEditor.Dispose();
        }
        else if (entry.RootGraph is { } root && entry.ContainingGraph is { } containing)
        {
            NodeGraphTabViewModel nodeGraph = ToolTabReuse.Find<NodeGraphTabViewModel>(
                this, tab => tab.Model.Value == root, tab => tab.Model.Value is null, retargetAnyOpen: true)
                ?? new NodeGraphTabViewModel(this);
            nodeGraph.Model.Value = root;
            nodeGraph.NavigateTo(containing);
            if (entry.GraphNode is { } node) nodeGraph.NodeGraph.Value?.RevealNode(node);
            if (!OpenToolTab(nodeGraph)) nodeGraph.Dispose();
        }
        else
        {
            RevealProperties(entry, element);
        }
    }

    private void RevealProperties(UndisplayableDataEntry entry, Element element)
    {
        if ((entry.PropertyOwner ?? entry.TargetObject) is { } target)
            RevealElementProperty(element, target, entry.PropertyName);
    }

    internal void RevealElementProperty(Element element, CoreObject target, string? propertyName)
    {
        // Selecting the element also opens or retargets its property tab, including when it was closed.
        if (FindToolTab<ElementPropertyTabViewModel>(tab => tab.Element.Value == element) is not { } properties)
            return;

        OpenToolTab(properties);
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsDisposingOrDisposed && properties.Element.Value == element)
                properties.Reveal(target, propertyName);
        }, DispatcherPriority.Loaded);
    }

    internal void LocateOnTimeline(TimeRange range, int? layer, Element? element = null)
    {
        if (FindToolTab<TimelineTabViewModel>() is { } timeline)
            OpenToolTab(timeline);
        else
            DockHost.OpenToolTabFromExtension(TimelineTabExtension.Instance, target: null);
        // A scene or a timeline that was just opened needs a view before it can scroll and flash.
        Dispatcher.UIThread.Post(() =>
        {
            if (IsDisposingOrDisposed || FindToolTab<TimelineTabViewModel>() is not { } target) return;
            target.PendingLocate.Value = (range, layer, element);
        }, DispatcherPriority.Loaded);
    }
}
