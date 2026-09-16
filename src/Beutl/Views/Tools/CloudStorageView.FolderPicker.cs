using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Api.Clients;
using Beutl.Language;
using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Tools;

public sealed partial class CloudStorageView
{
    private async Task<StorageDestination?> PickStorageFolderAsync(CloudStorageViewModel vm, StorageActionContext context)
    {
        using var cancellation = new CancellationTokenSource();
        string? target = context.FolderId;
        string? parent = null;
        string? nextCursor = null;
        string? movingFolder = context.Items is [var item] && item.IsFolder ? item.Id : null;
        bool loading = false;
        int generation = 0;
        var choices = new ObservableCollection<StorageEntryResponse>();
        var location = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var progress = new ProgressBar { Height = 2, IsIndeterminate = true };
        var home = new Button { Content = Strings.Home };
        var up = new Button { Content = Strings.CloudStorageUp };
        var retry = new Button { Name = "StorageFolderRetry", Content = Strings.CloudStorageRetryLoadMore, IsVisible = false };
        (string? Destination, bool Append) failedLoad = default;
        var list = new ListBox
        {
            Name = "StorageFolderDestinations",
            ItemsSource = choices,
            Height = Math.Clamp((TopLevel.GetTopLevel(this)?.Bounds.Height ?? 580) - 300, 160, 280),
            ItemTemplate = new FuncDataTemplate<StorageEntryResponse>((folder, _) => new TextBlock
            {
                Text = folder?.Name,
                Margin = new Thickness(4),
                Opacity = folder?.Id == movingFolder ? 0.4 : 1,
            }),
        };
        var dialog = new FAContentDialog
        {
            Title = Strings.Move,
            PrimaryButtonText = Strings.CloudStorageMoveHere,
            CloseButtonText = Strings.Cancel,
            IsPrimaryButtonEnabled = false,
            DefaultButton = FAContentDialogButton.Primary,
            Content = new StackPanel
            {
                Width = 320,
                Spacing = 8,
                Children = { new TextBlock { Text = Strings.CloudStorageMoveDescription, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { home, up } }, location, progress, list, error, retry },
            },
        };

        async Task LoadAsync(string? destination, bool append = false)
        {
            if (loading || cancellation.IsCancellationRequested) return;
            loading = true;
            int version = append ? generation : ++generation;
            progress.IsVisible = true;
            home.IsEnabled = up.IsEnabled = list.IsEnabled = false;
            dialog.IsPrimaryButtonEnabled = false;
            error.Text = "";
            retry.IsVisible = false;
            if (!append)
            {
                choices.Clear();
                nextCursor = null;
            }
            try
            {
                string? cursor = append ? nextCursor : null;
                var response = await vm.GetFolderChoicesAsync(context, destination, cursor, cancellation.Token);
                if (response == null || cancellation.IsCancellationRequested || version != generation) return;
                target = response.ParentId;
                parent = response.Path.LastOrDefault()?.ParentId;
                location.Text = string.Join(" / ", new[] { Strings.CloudStorage }.Concat(response.Path.Select(x => x.Name)));
                foreach (var folder in response.Entries.Where(x => x.Kind == "folder"))
                    if (choices.All(x => x.Id != folder.Id)) choices.Add(folder);
                nextCursor = response.NextCursor != cursor ? response.NextCursor : null;
                dialog.IsPrimaryButtonEnabled = target != context.FolderId
                    && (movingFolder == null || target != movingFolder && response.Path.All(x => x.Id != movingFolder));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    error.Text = Strings.CloudStorageLoadFailed;
                    failedLoad = (destination, append);
                    retry.IsVisible = true;
                }
                Debug.WriteLine(ex);
            }
            finally
            {
                loading = false;
                progress.IsVisible = false;
                home.IsEnabled = target != null;
                up.IsEnabled = target != null;
                list.IsEnabled = true;
            }
        }

        async Task OpenSelectedAsync()
        {
            if (list.SelectedItem is StorageEntryResponse folder && folder.Id != movingFolder)
                await LoadAsync(folder.Id);
        }
        home.Click += async (_, _) => await LoadAsync(null);
        up.Click += async (_, _) => await LoadAsync(parent);
        retry.Click += async (_, _) => await LoadAsync(failedLoad.Destination, failedLoad.Append);
        list.DoubleTapped += async (_, _) => await OpenSelectedAsync();
        list.AddHandler(KeyDownEvent, async (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            args.Handled = true;
            await OpenSelectedAsync();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        list.TemplateApplied += (_, _) =>
        {
            if (list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
                scroll.ScrollChanged += async (_, _) =>
                {
                    if (!loading && !retry.IsVisible && nextCursor != null && scroll.Viewport.Height > 0
                        && scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y < scroll.Viewport.Height / 2)
                        await LoadAsync(target, append: true);
                };
        };
        var shown = ShowStorageDialogAsync(dialog);
        var initial = LoadAsync(target);
        var result = await shown;
        cancellation.Cancel();
        await initial;
        return result == FAContentDialogResult.Primary && vm.IsActionCurrent(context) ? new StorageDestination(target) : null;
    }
}
