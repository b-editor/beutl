using Beutl.Agents;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.AgentTab;

internal static class AgentComposerLauncher
{
    public static bool IsAvailable(IEditorContext? editor)
        => editor is { IsEnabled.Value: true } && editor.GetService<IAgentWorkspaceHost>() is not null;

    public static Task SendElementsAsync(IEditorContext editor, IReadOnlyList<Element> elements)
        => SendAsync(editor, elements.Select(CreateElementMention).ToArray());

    public static Task SendObjectAsync(IEditorContext editor, CoreObject obj, Element? owner = null)
    {
        owner ??= (obj as IHierarchical)?.FindHierarchicalParent<Element>();
        Scene? scene = owner?.FindHierarchicalParent<Scene>() ?? editor.Object as Scene;
        Type type = obj.GetType();
        string name = obj.Name.Value;
        return SendAsync(editor, [new(AgentContextMentionKind.Object,
            string.IsNullOrWhiteSpace(name) ? TypeDisplayHelpers.GetLocalizedName(type) : name,
            obj.Id.Value.ToString("D"), SceneId: scene?.Id.Value.ToString("D"),
            ElementId: owner?.Id.Value.ToString("D"), TypeName: type.FullName ?? type.Name)]);
    }

    public static Task SendFrameAsync(IEditorContext editor) => SendAsync(editor, captureFrame: true);

    public static Task SendFileAsync(IEditorContext editor, string filePath) => SendAsync(editor, filePath: filePath);

    internal static AgentContextMention CreateElementMention(Element element)
    {
        string name = element.Name.Value;
        var range = element.Range;
        double start = range.Start.ToSeconds();
        double end = range.End.ToSeconds();
        return new(AgentContextMentionKind.Element, string.IsNullOrWhiteSpace(name) ? AgentStrings.MentionElement : name,
            element.Id.Value.ToString("D"), element.Uri is { IsFile: true } uri ? uri.LocalPath : null,
            FormattableString.Invariant($"{start:R}–{end:R} s · Layer {element.ZIndex.Value}"),
            SceneId: element.FindHierarchicalParent<Scene>()?.Id.Value.ToString("D"), StartSeconds: start, EndSeconds: end);
    }

    private static Task SendAsync(IEditorContext editor, IReadOnlyList<AgentContextMention>? mentions = null,
        bool captureFrame = false, string? filePath = null)
    {
        if (!IsAvailable(editor))
            return Task.CompletedTask;

        AgentTabViewModel? tab = editor.FindToolTab<AgentTabViewModel>(tab => tab.IsSelected.Value)
            ?? editor.FindToolTab<AgentTabViewModel>();
        bool created = tab is null;
        if (tab is null)
        {
            if (!AgentTabExtension.Instance.TryCreateContext(editor, out var context))
                return Task.CompletedTask;
            tab = (AgentTabViewModel)context;
        }
        if (!editor.OpenToolTab(tab))
        {
            if (created) tab.Dispose();
            return Task.CompletedTask;
        }

        tab.IsSelected.Value = true;
        return tab.PopulateComposerAsync(mentions, captureFrame, filePath);
    }
}
