using Avalonia.Controls;
using Avalonia.Layout;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Generative;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

/// <summary>One line of the confirmation shown before AI nodes bill anything.</summary>
public sealed record GenerativeRunConfirmationItem(
    string NodeName,
    string Description,
    bool? IsAvailable,
    bool WillRun,
    int Count = 1);

/// <summary>Builds what the confirmation lists from a run plan.</summary>
internal static class GenerativeRunConfirmation
{
    public static async Task<IReadOnlyList<GenerativeRunConfirmationItem>> DescribeAsync(
        GenerativeRunPlan plan,
        IGenerativeCostEstimator? estimator,
        CancellationToken cancellationToken)
    {
        var items = new List<GenerativeRunConfirmationItem>();
        foreach (GenerativePlanItem item in plan.Items)
        {
            string name = NameOf(item.Node);
            if (item.Problem is { } problem)
            {
                items.Add(new(name, string.Format(NodeGraphStrings.Generative_WillNotRun, problem), null, false));
                continue;
            }

            if (item.Request is not { } request || estimator is null)
            {
                items.Add(new(name, NodeGraphStrings.Generative_AfterUpstream, null, true, item.Count));
                continue;
            }

            GenerativeCostEstimate estimate = await estimator.EstimateAsync(request, cancellationToken);
            string description = string.Join(
                " · ",
                new[] { estimate.Model, estimate.Detail }.Where(part => !string.IsNullOrEmpty(part)));
            items.Add(new(name, description, estimate.IsAvailable, true, item.Count));
        }

        return items;
    }

    public static FAContentDialog CreateDialog(IReadOnlyList<GenerativeRunConfirmationItem> items)
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 360 };
        int billed = items.Where(item => item.WillRun).Sum(item => item.Count);
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(NodeGraphStrings.Generative_ConfirmIntro, billed),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        foreach (GenerativeRunConfirmationItem item in items)
        {
            var row = new StackPanel { Spacing = 2, Margin = new Avalonia.Thickness(8, 0, 0, 0) };
            row.Children.Add(new TextBlock
            {
                Text = item.Count > 1 ? $"{item.NodeName} × {item.Count}" : item.NodeName,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
            });
            if (item.Description.Length > 0)
            {
                row.Children.Add(new TextBlock
                {
                    Text = item.Description,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Opacity = 0.8,
                });
            }

            if (item.WillRun && AvailabilityText(item.IsAvailable) is { } availability)
                row.Children.Add(new TextBlock { Text = availability, Opacity = 0.8 });
            panel.Children.Add(row);
        }

        if (items.Any(item => item.IsAvailable == false))
        {
            panel.Children.Add(new TextBlock
            {
                Text = NodeGraphStrings.Generative_SomeUnavailable,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
        }

        return new FAContentDialog
        {
            Title = NodeGraphStrings.Generative_ConfirmTitle,
            Content = new ScrollViewer { Content = panel, MaxHeight = 420, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
            PrimaryButtonText = NodeGraphStrings.Generative_ConfirmRun,
            CloseButtonText = NodeGraphStrings.Generative_ConfirmCancel,
            DefaultButton = FAContentDialogButton.Primary,
        };
    }

    private static string? AvailabilityText(bool? available) => available switch
    {
        true => NodeGraphStrings.Generative_Available,
        false => NodeGraphStrings.Generative_Unavailable,
        null => NodeGraphStrings.Generative_AvailabilityUnknown,
    };

    private static string NameOf(GraphNode node)
        => string.IsNullOrWhiteSpace(node.Name)
            ? GraphNodeRegistry.FindItem(node.GetType())?.DisplayName ?? node.GetType().Name
            : node.Name;
}
