using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Beutl.Controls;
using Beutl.NodeGraph.Generative;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

public sealed partial class GraphNodeViewModel
{
    public bool IsPromptTarget => GraphNode is IPromptLibraryTarget;

    public IReadOnlyList<GenerativePromptEntry> GetPromptEntries()
        => GraphNode is IPromptLibraryTarget target
            && EditorContext.GetService<IGenerativePromptLibrary>() is { } library
                ? library.GetEntries(target.PromptOperation)
                : [];

    public void ApplyPrompt(string prompt)
    {
        if (GraphNode is IPromptLibraryTarget { CanApplyPrompt: true } target)
        {
            target.ApplyPrompt(prompt);
            EditorContext.GetService<HistoryManager>()?.Commit(NodeGraphStrings.Generative_PromptLibrary);
        }
    }

    public void SavePromptTemplate(string name)
    {
        if (GraphNode is not IPromptLibraryTarget target
            || EditorContext.GetService<IGenerativePromptLibrary>() is not { } library)
        {
            return;
        }

        NodeGraphViewModel.GenerativeError.Value =
            library.SaveTemplate(target.PromptOperation, name.Trim(), target.ComposePrompt());
    }

    public void SelectGeneration(Guid id)
    {
        if (GraphNode is GenerativeNode node && node.ActiveGenerationId != id)
        {
            node.SelectGeneration(id);
            CommitGenerationEdit();
        }
    }

    public void ToggleActivePinned()
    {
        if (GraphNode is GenerativeNode { ActiveGeneration: { } active })
        {
            active.IsPinned = !active.IsPinned;
            CommitGenerationEdit();
        }
    }

    public void PruneGenerations()
    {
        if (GraphNode is GenerativeNode node && node.PruneGenerations() > 0)
            CommitGenerationEdit();
    }

    private void CommitGenerationEdit()
        => EditorContext.GetService<HistoryManager>()?.Commit(NodeGraphStrings.Generative_History);

    /// <summary>Generates several variations of this node at once, keeping them all to compare.</summary>
    public void GenerateVariations(int count)
    {
        if (GraphNode is GenerativeNode node)
            _ = NodeGraphViewModel.RunGenerativeAsync([node], force: true, variations: count);
    }

    /// <summary>Lays the node's kept results side by side and makes the chosen one active.</summary>
    public async Task CompareGenerationsAsync()
    {
        if (GraphNode is not GenerativeNode node || node.Generations.Count == 0)
            return;

        GenerationRecord[] records = [.. node.Generations];
        Beutl.Media.Bitmap?[] thumbnails = await Task.Run(() => records.Select(GenerativeNode.DecodeThumbnail).ToArray());
        var refs = new List<Beutl.Media.Source.Ref<Beutl.Media.Bitmap>>();
        try
        {
            ListBox list = BuildGenerationList(records, thumbnails, refs);

            // After the items exist: set before, the selection has nothing to land on.
            list.SelectedIndex = Array.IndexOf(records, node.ActiveGeneration);

            var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
            {
                Title = NodeGraphStrings.Generative_Compare.TrimEnd('…'),
                Content = list,
                PrimaryButtonText = NodeGraphStrings.Generative_Adopt,
                CloseButtonText = NodeGraphStrings.Generative_ConfirmCancel,
                DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
            };
            if (await ShowDialogAsync(dialog) != FluentAvalonia.UI.Controls.FAContentDialogResult.Primary
                || list.SelectedIndex < 0)
            {
                return;
            }

            GenerationRecord chosen = records[list.SelectedIndex];
            node.SelectGeneration(chosen.Id);
            node.ApplyRecordInputs(chosen);
            CommitGenerationEdit();
        }
        finally
        {
            foreach (var bitmapRef in refs)
                bitmapRef.Dispose();
        }
    }

    // Every bitmap reference handed to the list is added to refs, so the caller can release them.
    private static ListBox BuildGenerationList(
        GenerationRecord[] records,
        Beutl.Media.Bitmap?[] thumbnails,
        List<Beutl.Media.Source.Ref<Beutl.Media.Bitmap>> refs)
    {
        var list = new ListBox
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel()),
            MaxHeight = 480,
        };
        for (int i = 0; i < records.Length; i++)
        {
            var content = new StackPanel { Spacing = 4, Width = 200 };
            if (thumbnails[i] is { } bitmap)
            {
                var bitmapRef = Beutl.Media.Source.Ref<Beutl.Media.Bitmap>.Create(bitmap);
                refs.Add(bitmapRef);
                content.Children.Add(new BitmapView
                {
                    Source = bitmapRef,
                    Height = 150,
                    Stretch = Stretch.Uniform,
                });
            }

            string caption = records[i].Seed is int seed
                ? $"{records[i].CreatedAt.LocalDateTime:g} · {seed}"
                : $"{records[i].CreatedAt.LocalDateTime:g}";
            content.Children.Add(new TextBlock { Text = caption, TextTrimming = TextTrimming.CharacterEllipsis });
            list.Items.Add(new ListBoxItem { Content = content });
        }

        return list;
    }

    /// <summary>Generates this node again even when its inputs are unchanged.</summary>
    public void Regenerate()
    {
        if (GraphNode is GenerativeNode node)
            _ = NodeGraphViewModel.RunGenerativeAsync([node], force: true);
    }
}
