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
        using var picker = new StorageFolderPicker(this, vm, context);
        return await picker.ShowAsync();
    }

    // The folder chooser a move opens. One instance serves one call: its fields are that dialog's
    // controls and how far its listing has got.
    private sealed class StorageFolderPicker : IDisposable
    {
        private readonly CloudStorageView _owner;
        private readonly CloudStorageViewModel _vm;
        private readonly StorageActionContext _context;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly string? _movingFolder;
        private readonly ObservableCollection<StorageEntryResponse> _choices = new();
        private readonly TextBlock _location = new() { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };
        private readonly ProgressBar _progress = new() { Height = 2, IsIndeterminate = true };
        private readonly Button _home = new() { Content = Strings.Home };
        private readonly Button _up = new() { Content = Strings.CloudStorageUp };
        private readonly Button _retry = new() { Name = "StorageFolderRetry", Content = Strings.CloudStorageRetryLoadMore, IsVisible = false };
        private readonly ListBox _list;
        private readonly FAContentDialog _dialog;
        private string? _target;
        private string? _parent;
        private string? _nextCursor;
        private bool _loading;
        private int _generation;
        private (string? Destination, bool Append) _failedLoad;

        public StorageFolderPicker(CloudStorageView owner, CloudStorageViewModel vm, StorageActionContext context)
        {
            _owner = owner;
            _vm = vm;
            _context = context;
            _target = context.FolderId;
            _movingFolder = context.Items is [var item] && item.IsFolder ? item.Id : null;
            _list = new ListBox
            {
                Name = "StorageFolderDestinations",
                ItemsSource = _choices,
                Height = Math.Clamp((TopLevel.GetTopLevel(owner)?.Bounds.Height ?? 580) - 300, 160, 280),
                ItemTemplate = new FuncDataTemplate<StorageEntryResponse>((folder, _) => new TextBlock
                {
                    Text = folder?.Name,
                    Margin = new Thickness(4),
                    Opacity = folder?.Id == _movingFolder ? 0.4 : 1,
                }),
            };
            _dialog = new FAContentDialog
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
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _home, _up } }, _location, _progress, _list, _error, _retry },
                },
            };

            _home.Click += async (_, _) => await LoadAsync(null);
            _up.Click += async (_, _) => await LoadAsync(_parent);
            _retry.Click += async (_, _) => await LoadAsync(_failedLoad.Destination, _failedLoad.Append);
            _list.DoubleTapped += async (_, _) => await OpenSelectedAsync();
            _list.AddHandler(KeyDownEvent, async (_, args) =>
            {
                if (args.Key != Key.Enter) return;
                args.Handled = true;
                await OpenSelectedAsync();
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _list.TemplateApplied += (_, _) =>
            {
                if (_list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
                    scroll.ScrollChanged += async (_, _) => await OnScrollChangedAsync(scroll);
            };
        }

        public async Task<StorageDestination?> ShowAsync()
        {
            var shown = _owner.ShowStorageDialogAsync(_dialog);
            var initial = LoadAsync(_target);
            var result = await shown;
            _cancellation.Cancel();
            await initial;
            return result == FAContentDialogResult.Primary && _vm.IsActionCurrent(_context) ? new StorageDestination(_target) : null;
        }

        public void Dispose() => _cancellation.Dispose();

        private void SetLocation(StorageResponse response)
        {
            _target = response.ParentId;
            _parent = response.Path.LastOrDefault()?.ParentId;
            _location.Text = string.Join(" / ", new[] { Strings.CloudStorage }.Concat(response.Path.Select(x => x.Name)));
        }

        private async Task LoadAsync(string? destination, bool append = false)
        {
            if (_loading || _cancellation.IsCancellationRequested) return;
            _loading = true;
            int version = append ? _generation : ++_generation;
            _progress.IsVisible = true;
            _home.IsEnabled = _up.IsEnabled = _list.IsEnabled = false;
            _error.Text = "";
            _retry.IsVisible = false;
            if (!append)
            {
                // Optional child pages leave the already validated destination usable.
                _dialog.IsPrimaryButtonEnabled = false;
                _choices.Clear();
                _nextCursor = null;
            }
            try
            {
                string? cursor = append ? _nextCursor : null;
                var response = await _vm.GetFolderChoicesAsync(_context, destination, cursor, _cancellation.Token);
                if (response == null || _cancellation.IsCancellationRequested || version != _generation) return;
                if (append && response.ParentId != destination)
                {
                    // The old cursor and children belong to a different folder. Restart
                    // from the resolved location, including when the reload needs a retry.
                    destination = response.ParentId;
                    append = false;
                    version = ++_generation;
                    cursor = _nextCursor = null;
                    _choices.Clear();
                    _dialog.IsPrimaryButtonEnabled = false;
                    SetLocation(response);
                    response = await _vm.GetFolderChoicesAsync(_context, destination, null, _cancellation.Token);
                    if (response == null || _cancellation.IsCancellationRequested || version != _generation) return;
                }
                SetLocation(response);
                foreach (var folder in response.Entries.Where(x => x.Kind == "folder"))
                    if (_choices.All(x => x.Id != folder.Id)) _choices.Add(folder);
                _nextCursor = response.NextCursor != cursor ? response.NextCursor : null;
                _dialog.IsPrimaryButtonEnabled = _target != _context.FolderId
                    && (_movingFolder == null || _target != _movingFolder && response.Path.All(x => x.Id != _movingFolder));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!_cancellation.IsCancellationRequested)
                {
                    _error.Text = Strings.CloudStorageLoadFailed;
                    _failedLoad = (destination, append);
                    _retry.IsVisible = true;
                }
                Debug.WriteLine(ex);
            }
            finally
            {
                _loading = false;
                _progress.IsVisible = false;
                _home.IsEnabled = _target != null;
                _up.IsEnabled = _target != null;
                _list.IsEnabled = true;
            }
        }

        private async Task OpenSelectedAsync()
        {
            if (_list.SelectedItem is StorageEntryResponse folder && folder.Id != _movingFolder)
                await LoadAsync(folder.Id);
        }

        // The next page loads once the list is scrolled within half a screen of its end.
        private async Task OnScrollChangedAsync(ScrollViewer scroll)
        {
            if (!_loading && !_retry.IsVisible && _nextCursor != null && scroll.Viewport.Height > 0
                && scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y < scroll.Viewport.Height / 2)
                await LoadAsync(_target, append: true);
        }
    }
}
