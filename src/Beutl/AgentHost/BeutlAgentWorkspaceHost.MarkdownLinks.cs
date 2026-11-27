using System.Diagnostics;
using Avalonia.Threading;
using Beutl.Editor.Components.AgentTab;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.AgentHost;

internal sealed partial class BeutlAgentWorkspaceHost
{
    internal Func<ProcessStartInfo, Task<bool>> LaunchMarkdownFileManagerAsync { get; set; } = FileManagerLauncher.LaunchAsync;

    public async Task<bool> TryOpenMarkdownLinkAsync(
        IEditorContext editor, string projectPath, AgentMarkdownLink link, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        if (link.Kind == AgentMarkdownLinkKind.LocalFile && link.LocalPath is { } localPath)
        {
            var target = await Task.Run(() => AgentMarkdownLocalPath.Resolve(localPath, Path.GetDirectoryName(projectPath)), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (target is null) return false;
            await FileManagerLauncher.RevealAsync(editor.GetRequiredService<INotificationService>(),
                target.FullPath, target.IsDirectory, LaunchMarkdownFileManagerAsync, s_logger);
            return true;
        }

        return editor is EditViewModel source && await NavigateAsync(source, link, cancellationToken);
    }

    private sealed record MarkdownTarget(Scene Scene, CoreObject? Object = null, Element? Element = null);

    private static async Task<bool> NavigateAsync(EditViewModel source, AgentMarkdownLink link, CancellationToken cancellationToken)
    {
        if (source.IsDisposingOrDisposed || !source.IsEnabled.Value) return false;
        EditorService editors = source.EditorService;
        ProjectService? projects = editors.AppServices.GetService<ProjectService>();
        if (projects?.CurrentTransition is not null) return false;

        var active = editors.SelectedTabItem.Value?.Context.Value as EditViewModel;
        Scene activeScene = active is { IsDisposingOrDisposed: false } ? active.Scene : source.Scene;
        Project? project = source.Scene.FindHierarchicalParent<Project>() ?? projects?.CurrentProject.Value;
        var openScenes = editors.TabItems.Select(tab => tab.Context.Value).OfType<EditViewModel>()
            .Where(editor => !editor.IsDisposingOrDisposed).Select(editor => editor.Scene);
        var scenes = new[] { activeScene, source.Scene }.Concat(project?.Items.OfType<Scene>() ?? []).Concat(openScenes).Distinct();
        if (FindMarkdownTarget(link, activeScene, scenes) is not { Scene: var scene } target)
            return false;

        EditorTabItem? tab = editors.TryGetTabItem(scene, out var existingTab) ? existingTab
            : scene.Uri is { IsFile: true } ? editors.GetOrCreateBackgroundTabItem(scene) : null;
        if (tab?.Context.Value is not EditViewModel editor || editor.IsDisposingOrDisposed || !editor.IsEnabled.Value)
            return false;

        tab.IsSelected.Value = true;
        editors.SelectedTabItem.Value = tab;

        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        MediaTime start = scene.Start.Value;
        MediaTime end = start + scene.Duration.Value;
        Rational rate = editor.Player.GetFrameRate();
        MediaTime? seek = null;
        if (link.Time is { } windowTime)
        {
            // Links use the same scene-relative times as render_still.
            MediaTime offset = ClampTime(windowTime, MediaTime.Zero, scene.Duration.Value);
            seek = ClampToFrames(start + offset, start, end, rate);
        }
        else if (target.Object is { } obj && AgentEditFollower.KeyFrameTime(obj) is { } keyFrameTime)
        {
            seek = ClampToFrames(keyFrameTime, start, end, rate);
        }
        else if (target.Element is { } clip)
        {
            MediaTime first = ClampTime(clip.Range.Start, start, end);
            MediaTime last = ClampTime(clip.Range.End, first, end);
            MediaTime current = clock.CurrentTime.Value;
            if (current < first || current >= last)
                seek = first < last ? ClampToFrames(current, first, last, rate) : ClampToFrames(first, start, end, rate);
        }

        if (seek is { } time)
        {
            if (editor.Player.IsPlaying.Value)
            {
                await editor.Player.Pause();
                cancellationToken.ThrowIfCancellationRequested();
                if (editor.IsDisposingOrDisposed || !editor.IsEnabled.Value) return false;
            }

            if (target.Element is null)
                editor.SeekAndScroll(time);
            else
                clock.Seek(time);
        }

        if (target.Element is { } element)
        {
            if (!scene.Children.Contains(element)) return false;
            var selection = editor.GetRequiredService<IEditorSelection>();
            if (selection.SelectedElements.Count > 1 && selection.SelectedElements.Contains(element))
                selection.SelectedElements.Replace([element]);
            selection.Select(element, notifyIfSelected: true);
            editor.LocateOnTimeline(element.Range, element.ZIndex.Value, element);
            if (target.Object is { } ownedObject && !ReferenceEquals(ownedObject, element))
                editor.RevealElementProperty(element, ownedObject, link.PropertyName);
        }
        else if (target.Object is { } standalone && standalone is not Scene)
        {
            ObjectPropertyTabViewModel.Open(editor, standalone, provider: null);
        }

        editor.Player.QueuePreviewRender();
        return true;
    }

    private static MarkdownTarget? FindMarkdownTarget(AgentMarkdownLink link, Scene active, IEnumerable<Scene> scenes)
    {
        if (link.SceneId is { } sceneId)
        {
            Scene[] matches = scenes.Where(scene => scene.Id.Value == sceneId).Take(2).ToArray();
            if (matches.Length != 1) return null;
            scenes = matches;
        }

        if (link.Kind is AgentMarkdownLinkKind.Time or AgentMarkdownLinkKind.Scene)
            return new(link.SceneId.HasValue ? scenes.First() : active);

        foreach (Scene scene in scenes)
        {
            if (link.Kind == AgentMarkdownLinkKind.Element)
            {
                if (scene.Children.FirstOrDefault(item => item.Id.Value == link.TargetId) is { } element)
                    return new(scene, element, element);
                continue;
            }

            CoreObject? root = link.ElementId is { } elementId
                ? scene.Children.FirstOrDefault(item => item.Id.Value == elementId) : scene;
            if (root is null) continue;
            Element? owner = null;
            var searcher = new ObjectSearcher(root, (ancestors, item) =>
            {
                if (item is not CoreObject obj || obj.Id.Value != link.TargetId) return false;
                owner = obj as Element ?? (obj as IHierarchical)?.FindHierarchicalParent<Element>()
                    ?? ancestors.OfType<Element>().FirstOrDefault();
                return true;
            });
            if (searcher.Search() is CoreObject target)
                return new(scene, target, owner);
        }

        return null;
    }

    private static MediaTime ClampToFrames(MediaTime time, MediaTime start, MediaTime end, Rational rate)
    {
        if (end <= start) return start;
        var first = start.ToFrame(rate, MidpointRounding.ToPositiveInfinity);
        var last = (end - new MediaTime(1)).ToFrame(rate, MidpointRounding.ToNegativeInfinity);
        if (last < first) return start;
        var frame = ClampTime(time, start, end).ToFrame(rate, MidpointRounding.AwayFromZero);
        return MediaTime.FromFrames(Math.Clamp(frame, first, last), rate);
    }

    private static MediaTime ClampTime(MediaTime time, MediaTime min, MediaTime max)
        => max < min || time < min ? min : time > max ? max : time;
}
