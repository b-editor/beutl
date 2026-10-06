using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.NodeGraph.Nodes.Group;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

public partial class GraphNodeView
{
    private void OpenNodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphNodeViewModel { GraphNode: GroupNode groupNode }
            && this.FindAncestorOfType<NodeGraphTabView>()?.DataContext is NodeGraphTabViewModel tabViewModel)
        {
            tabViewModel.NavigateTo(groupNode.Group);
        }
    }

    // Rebuilt each time the menu opens: the history changes with every generation and undo.
    private void NodeMenuOpening(object? sender, EventArgs e)
    {
        BuildPromptLibraryMenu();
        Variations2Item.Text = string.Format(NodeGraphStrings.Generative_VariationCount, 2);
        Variations4Item.Text = string.Format(NodeGraphStrings.Generative_VariationCount, 4);
        CompareItem.IsEnabled = DataContext is GraphNodeViewModel { GraphNode: Beutl.NodeGraph.Generative.GenerativeNode { Generations.Count: > 1 } };
        if (DataContext is not GraphNodeViewModel { GraphNode: Beutl.NodeGraph.Generative.GenerativeNode node } viewModel)
            return;

        var items = new List<object>();
        Beutl.NodeGraph.Generative.GenerationRecord? active = node.ActiveGeneration;
        foreach (Beutl.NodeGraph.Generative.GenerationRecord record in node.Generations.Reverse())
        {
            Guid id = record.Id;
            string text = $"{record.CreatedAt.LocalDateTime:g}  {record.Summary}";
            if (record.IsPinned)
                text += $"  ({NodeGraphStrings.Generative_Pinned})";
            var item = new FAToggleMenuFlyoutItem { Text = text, IsChecked = ReferenceEquals(record, active) };
            item.Click += (_, _) => viewModel.SelectGeneration(id);
            items.Add(item);
        }

        if (items.Count == 0)
            items.Add(new FAMenuFlyoutItem { Text = NodeGraphStrings.Generative_NoHistory, IsEnabled = false });

        items.Add(new FAMenuFlyoutSeparator());
        var pin = new FAMenuFlyoutItem
        {
            Text = active?.IsPinned == true ? NodeGraphStrings.Generative_Unpin : NodeGraphStrings.Generative_Pin,
            IsEnabled = active is not null,
        };
        pin.Click += (_, _) => viewModel.ToggleActivePinned();
        items.Add(pin);
        var prune = new FAMenuFlyoutItem
        {
            Text = NodeGraphStrings.Generative_PruneHistory,
            IsEnabled = node.Generations.Any(record => !record.IsPinned && !ReferenceEquals(record, active)),
        };
        prune.Click += (_, _) => viewModel.PruneGenerations();
        items.Add(prune);

        GenerationHistoryMenu.Items.Clear();
        foreach (object item in items)
            GenerationHistoryMenu.Items.Add(item);
    }

    private void BuildPromptLibraryMenu()
    {
        if (DataContext is not GraphNodeViewModel { GraphNode: Beutl.NodeGraph.Generative.IPromptLibraryTarget target } viewModel)
            return;

        IReadOnlyList<Beutl.NodeGraph.Generative.GenerativePromptEntry> entries = viewModel.GetPromptEntries();
        var items = new List<object>();
        AddSection(Strings.AiPromptTemplates, Strings.AiPromptTemplatesEmpty, entries.Where(entry => entry.IsTemplate));
        items.Add(new FAMenuFlyoutSeparator());
        AddSection(Strings.AiPromptHistory, Strings.AiPromptHistoryEmpty, entries.Where(entry => !entry.IsTemplate));
        items.Add(new FAMenuFlyoutSeparator());
        var save = new FAMenuFlyoutItem
        {
            Text = NodeGraphStrings.Generative_SaveTemplate,
            IsEnabled = target.ComposePrompt().Length > 0,
        };
        save.Click += (_, _) =>
        {
            var flyout = new RenameFlyout { Text = string.Empty };
            flyout.Confirmed += (_, name) =>
            {
                if (!string.IsNullOrWhiteSpace(name))
                    viewModel.SavePromptTemplate(name);
            };
            flyout.ShowAt(handle);
        };
        items.Add(save);

        PromptLibraryMenu.Items.Clear();
        foreach (object item in items)
            PromptLibraryMenu.Items.Add(item);

        void AddSection(string header, string empty, IEnumerable<Beutl.NodeGraph.Generative.GenerativePromptEntry> section)
        {
            items.Add(new FAMenuFlyoutItem { Text = header, IsEnabled = false });
            int before = items.Count;
            foreach (Beutl.NodeGraph.Generative.GenerativePromptEntry entry in section)
            {
                string prompt = entry.Prompt;
                var item = new FAMenuFlyoutItem
                {
                    Text = entry.IsPinned ? $"{entry.Name}  ({NodeGraphStrings.Generative_Pinned})" : entry.Name,
                    IsEnabled = target.CanApplyPrompt,
                };
                ToolTip.SetTip(item, prompt);
                item.Click += (_, _) => viewModel.ApplyPrompt(prompt);
                items.Add(item);
            }

            if (items.Count == before)
                items.Add(new FAMenuFlyoutItem { Text = empty, IsEnabled = false });
        }
    }

    private void Variations2Click(object? sender, RoutedEventArgs e)
        => (DataContext as GraphNodeViewModel)?.GenerateVariations(2);

    private void Variations4Click(object? sender, RoutedEventArgs e)
        => (DataContext as GraphNodeViewModel)?.GenerateVariations(4);

    private void CompareClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphNodeViewModel viewModel)
            _ = viewModel.CompareGenerationsAsync();
    }

    private void SaveTemplateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphNodeViewModel viewModel)
            return;

        var flyout = new RenameFlyout { Text = viewModel.NodeName.Value };
        flyout.Confirmed += (_, name) =>
        {
            if (!string.IsNullOrWhiteSpace(name))
                viewModel.SaveAsTemplate(name);
        };
        flyout.ShowAt(handle);
    }

    private void RegenerateClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as GraphNodeViewModel)?.Regenerate();
    }

    private void RenameClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphNodeViewModel viewModel)
        {
            var flyout = new RenameFlyout()
            {
                Text = viewModel.GraphNode.Name
            };

            flyout.Confirmed += OnNameConfirmed;

            flyout.ShowAt(handle);
        }
    }

    private void OnNameConfirmed(object? sender, string? e)
    {
        if (sender is RenameFlyout flyout
            && DataContext is GraphNodeViewModel viewModel)
        {
            flyout.Confirmed -= OnNameConfirmed;
            viewModel.UpdateName(e);
        }
    }
}
