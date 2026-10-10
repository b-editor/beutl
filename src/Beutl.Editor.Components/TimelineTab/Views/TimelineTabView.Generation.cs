using System.Reactive.Disposables;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Generative;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services.AI;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.Views;

public partial class TimelineTabView
{
    private TimelineAiPopupFlyout? _generationFlyout;

    private void AttachGeneration(TimelineTabViewModel viewModel)
    {
        TimelinePanel.Children.AddRange(viewModel.Placeholders.Select(placeholder =>
            new GenerationPlaceholderView { DataContext = placeholder }));
        viewModel.Placeholders.TrackCollectionChanged(
                placeholder => TimelinePanel.Children.Add(new GenerationPlaceholderView { DataContext = placeholder }),
                RemovePlaceholder,
                () => { })
            .DisposeWith(_disposables);
        viewModel.GenerationPopupRequested += OnGenerationPopupRequested;
        Disposable.Create(() =>
            {
                viewModel.GenerationPopupRequested -= OnGenerationPopupRequested;
                _generationFlyout?.Hide();
            })
            .DisposeWith(_disposables);
    }

    private void RemovePlaceholder(GenerationPlaceholderViewModel placeholder)
    {
        for (int i = TimelinePanel.Children.Count - 1; i >= 0; i--)
        {
            if (TimelinePanel.Children[i] is GenerationPlaceholderView { DataContext: var context }
                && ReferenceEquals(context, placeholder))
            {
                TimelinePanel.Children.RemoveAt(i);
            }
        }
    }

    // Posted, so a context menu that asked for it has closed and the placeholder exists to
    // anchor to.
    private void OnGenerationPopupRequested(object? sender, TimelineGenerationJob job)
        => Dispatcher.UIThread.Post(() => ShowGenerationPopup(job), DispatcherPriority.Background);

    private void ShowGenerationPopup(TimelineGenerationJob job)
    {
        if (ViewModel is not { GenerationService: { } service } viewModel || !service.Jobs.Contains(job))
            return;

        Control? anchor = TimelinePanel.Children.FirstOrDefault(child =>
            child is GenerationPlaceholderView { DataContext: GenerationPlaceholderViewModel placeholder }
            && ReferenceEquals(placeholder.Job, job));
        if (anchor is null)
            return;

        _generationFlyout?.Hide();
        Scene scene = viewModel.Scene;
        var popup = new TimelineAiPopupViewModel(
            job,
            service,
            viewModel.EditorContext.GetService<IGenerativeModelCatalog>(),
            viewModel.EditorContext.GetService<ITimelineAiHost>(),
            scene.FrameSize,
            scene.FindHierarchicalParent<Project>().GetFrameRate())
        {
            PickImageFile = PickImageFileAsync,
        };
        var flyout = new TimelineAiPopupFlyout(popup)
        {
            Placement = PlacementMode.TopEdgeAlignedLeft,
        };
        popup.CloseRequested += (_, _) => flyout.Hide();
        flyout.Closed += (_, _) =>
        {
            popup.Dispose();
            if (ReferenceEquals(_generationFlyout, flyout))
                _generationFlyout = null;
            // Nothing was asked for; the placeholder goes with the popup.
            if (service.Jobs.Contains(job) && job.State.Value == TimelineGenerationState.Draft)
                service.Remove(job);
        };
        _generationFlyout = flyout;
        anchor.BringIntoView();
        flyout.ShowAt(anchor);
    }

    private void UpdateGenerateHereMenuItem()
    {
        bool available = ViewModel?.IsGenerationAvailable == true;
        GenerateHereMenuItem.IsVisible = available;
        GenerateHereMenuItem.IsEnabled = available && ViewModel!.FindGenerationGapAtPointer() is not null;
    }

    private void GenerateHere_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel?.FindGenerationGapAtPointer() is { } gap)
            _ = ViewModel.StartGapGenerationAsync(gap);
    }

    private async Task<string?> PickImageFileAsync(CancellationToken cancellationToken)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return null;
        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        // The popup may have closed while the dialog was open; then nothing waits for the file.
        return files.Count == 1 && !cancellationToken.IsCancellationRequested
            ? files[0].TryGetLocalPath()
            : null;
    }
}
