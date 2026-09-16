using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Beutl.Language;
using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;
using FluentIcon = FluentIcons.Avalonia.Fluent.FluentIcon;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Views.Tools;

internal sealed record StorageDestination(string? Id);

public sealed partial class CloudStorageView
{
    private ContextMenu? _storageMenu;
    private FAContentDialog? _storageDialog;
    internal ContextMenu? StorageMenu => _storageMenu;
    internal FAContentDialog? StorageDialog => _storageDialog;
    internal Func<string, string, bool, Task<string?>>? NamePrompt { get; set; }
    internal Func<string, Task<bool>>? DeletePrompt { get; set; }
    internal Func<StorageActionContext, Task<StorageDestination?>>? DestinationPrompt { get; set; }
    internal Func<Uri, Task>? UriLauncher { get; set; }

    private void CloseStorageInteractions()
    {
        _storageMenu?.Close();
        _storageMenu = null;
        _storageDialog?.Hide();
        _storageDialog = null;
    }

    private void OnStorageContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not CloudStorageViewModel vm) return;
        bool pointer = e.TryGetPosition(StorageItems, out _);
        var container = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        var clicked = container?.DataContext as CloudStorageItem;
        if (!pointer && clicked == null) clicked = StorageItems.SelectedItem as CloudStorageItem;
        CloudStorageItem[] targets = [];
        if (clicked != null)
        {
            if (clicked.IsFolder || StorageItems.SelectedItems?.Contains(clicked) != true)
                StorageItems.SelectedItem = clicked;
            targets = clicked.IsFolder ? [clicked] : StorageItems.SelectedItems!.OfType<CloudStorageItem>().Where(x => !x.IsFolder).ToArray();
        }
        else if (pointer) StorageItems.SelectedItem = null;
        var context = vm.CaptureActionContext(targets);
        e.Handled = true;
        if (context == null) return;
        CancelPrefetchIntent();
        _storageMenu?.Close();
        var menu = new ContextMenu { Name = "StorageContextMenu" };
        int lastGroup = -1;
        void Add(string id, string label, Icon icon, int group)
        {
            if (lastGroup != -1 && lastGroup != group) menu.Items.Add(new Separator());
            lastGroup = group;
            var item = new MenuItem { Name = $"StorageAction_{id}", Header = label, Icon = new FluentIcon { Icon = icon, FontSize = 16 } };
            if (id == "delete") item.Classes.Add("storage-destructive");
            item.Click += async (_, _) => await ExecuteStorageActionAsync(id, context);
            menu.Items.Add(item);
        }
        if (targets.Length == 0)
        {
            Add("createFolder", Strings.NewFolder, Icon.FolderAdd, 0);
            Add("refresh", Strings.Refresh, Icon.ArrowClockwise, 1);
        }
        else if (targets is [var folder] && folder.IsFolder)
        {
            if (folder.Can("open")) Add("open", Strings.Open, Icon.FolderOpen, 0);
            if (folder.Can("rename")) Add("rename", Strings.Rename, Icon.Rename, 1);
            if (folder.Can("move")) Add("move", Strings.Move, Icon.FolderArrowRight, 1);
            if (folder.Can("delete")) Add("delete", Strings.Delete, Icon.Delete, 3);
        }
        else
        {
            var single = targets.Length == 1 ? targets[0] : null;
            if (single?.Can("open") == true) Add("open", Strings.Open, Icon.Open, 0);
            if (single?.Can("download") == true) Add("download", Strings.CloudStorageDownload, Icon.ArrowDownload, 0);
            if (single?.Can("copyLink") == true) Add("copyLink", Strings.CloudStorageCopyLink, Icon.Link, 0);
            if (single?.Can("rename") == true) Add("rename", Strings.Rename, Icon.Rename, 1);
            if (targets.All(x => x.Can("move"))) Add("move", Strings.Move, Icon.FolderArrowRight, 1);
            if (single?.Can("details") == true) Add("details", Strings.Details, Icon.Info, 1);
            if (targets.All(x => x.Can("setPublic"))) Add("setPublic", Strings.CloudStorageSetPublic, Icon.Globe, 2);
            if (targets.All(x => x.Can("setPrivate"))) Add("setPrivate", Strings.CloudStorageSetPrivate, Icon.LockClosed, 2);
            if (targets.Any(x => x.Can("delete"))) Add("delete", Strings.Delete, Icon.Delete, 3);
            if (targets.All(x => x.Entry?.Visibility == "DEDICATED"))
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem { IsEnabled = false, Header = new TextBlock { Text = Strings.CloudStorageDedicatedHint, Width = 230, TextWrapping = TextWrapping.Wrap } });
            }
        }
        _storageMenu = menu;
        menu.Placement = pointer ? PlacementMode.Pointer : PlacementMode.Bottom;
        menu.PlacementTarget = (Control?)container ?? StorageItems;
        menu.Open(StorageItems);
    }

    internal async Task ExecuteStorageActionAsync(string action, StorageActionContext context)
    {
        _storageMenu?.Close();
        if (DataContext is not CloudStorageViewModel vm || !vm.IsActionCurrent(context)) return;
        int affected = action == "move" ? context.Items.Length : context.Items.Count(x => x.Can(action));
        if (affected > 200 && action is "delete" or "move" or "setPublic" or "setPrivate")
        {
            vm.ActionError.Value = Strings.CloudStorageSelectionLimit;
            return;
        }
        try
        {
            var single = context.Items.Length == 1 ? context.Items[0] : null;
            switch (action)
            {
                case "open" when single is { IsFolder: true } && single.Can("open"):
                    await vm.OpenFolderAsync(single);
                    break;
                case "open" when single?.Can("open") == true:
                    if (await vm.GetContentUriAsync(context) is { } uri)
                    {
                        if (UriLauncher != null) await UriLauncher(uri);
                        else Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
                    }
                    break;
                case "copyLink" when single?.Can("copyLink") == true:
                    if (await vm.GetContentUriAsync(context, publicOnly: true) is { } link && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                        await clipboard.SetTextAsync(link.AbsoluteUri);
                    break;
                case "download" when single?.Can("download") == true:
                    await SaveStorageFileAsync(vm, context, single);
                    break;
                case "details" when single?.Can("details") == true:
                    vm.DetailsItem.Value = single;
                    break;
                case "createFolder":
                    if (await PromptStorageNameAsync(Strings.NewFolder, "", false) is { } folderName)
                        await vm.CreateFolderAsync(context, folderName);
                    break;
                case "rename" when single?.Can("rename") == true:
                    if (await PromptStorageNameAsync(Strings.Rename, single.Name, !single.IsFolder) is { } name)
                        await vm.RenameAsync(context, name);
                    break;
                case "move":
                    var destination = DestinationPrompt != null ? await DestinationPrompt(context) : await PickStorageFolderAsync(vm, context);
                    if (destination != null) await vm.MoveAsync(context, destination.Id);
                    break;
                case "setPublic": await vm.SetVisibilityAsync(context, true); break;
                case "setPrivate": await vm.SetVisibilityAsync(context, false); break;
                case "delete":
                    string message;
                    if (single is { IsFolder: true })
                    {
                        if (await vm.GetFolderDetailsAsync(context, single.Id) is not { } summary) return;
                        message = string.Format(Strings.CloudStorageDeleteFolder, single.Name, summary.FolderCount, summary.FileCount);
                    }
                    else
                    {
                        int count = context.Items.Count(x => x.Can("delete"));
                        if (count == 0) return;
                        message = string.Format(Strings.CloudStorageDeleteFiles, count);
                        if (count < context.Items.Length) message += Environment.NewLine + string.Format(Strings.CloudStorageDeleteSkipped, context.Items.Length - count);
                    }
                    bool confirmed = DeletePrompt != null ? await DeletePrompt(message) : await ConfirmStorageDeleteAsync(message);
                    if (confirmed) await vm.DeleteAsync(context);
                    break;
                case "refresh": await Task.WhenAll(vm.LoadAsync(), vm.LoadUsageAsync()); break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (vm.IsActionCurrent(context)) vm.ReportActionError(ex); }
    }

    private async Task<FAContentDialogResult> ShowStorageDialogAsync(FAContentDialog dialog)
    {
        if (!_attached || TopLevel.GetTopLevel(this) is not { } owner) return FAContentDialogResult.None;
        _storageDialog = dialog;
        try { return await dialog.ShowAsync(owner); }
        finally { if (ReferenceEquals(_storageDialog, dialog)) _storageDialog = null; }
    }

    private async Task<string?> PromptStorageNameAsync(string title, string initialName, bool selectStem)
    {
        if (NamePrompt != null) return await NamePrompt(title, initialName, selectStem);
        var input = new TextBox { Name = "StorageNameInput", Text = initialName, MaxLength = 255, MinWidth = 240 };
        var validation = new TextBlock { Text = Strings.CloudStorageInvalidName, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var dialog = new FAContentDialog
        {
            Title = title,
            PrimaryButtonText = Strings.OK,
            CloseButtonText = Strings.Cancel,
            DefaultButton = FAContentDialogButton.Primary,
            Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = Strings.Name }, input, validation } },
        };
        using var subscription = input.GetObservable(TextBox.TextProperty).Subscribe(text =>
        {
            bool valid = CloudStorageViewModel.IsValidName(text ?? "");
            dialog.IsPrimaryButtonEnabled = valid && (initialName.Length == 0 || text?.Trim() != initialName.Trim());
            validation.IsVisible = !string.IsNullOrEmpty(text) && !valid;
        });
        dialog.Opened += (_, _) =>
        {
            input.Focus();
            input.SelectionStart = 0;
            int dot = selectStem ? initialName.LastIndexOf('.') : -1;
            input.SelectionEnd = dot > 0 ? dot : initialName.Length;
        };
        return await ShowStorageDialogAsync(dialog) == FAContentDialogResult.Primary ? input.Text?.Trim() : null;
    }

    private async Task<bool> ConfirmStorageDeleteAsync(string message) => await ShowStorageDialogAsync(new FAContentDialog
    {
        Title = Strings.Delete,
        Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
        PrimaryButtonText = Strings.Delete,
        CloseButtonText = Strings.Cancel,
        DefaultButton = FAContentDialogButton.Close,
    }) == FAContentDialogResult.Primary;

    private async Task SaveStorageFileAsync(CloudStorageViewModel vm, StorageActionContext context, CloudStorageItem item)
    {
        if (TopLevel.GetTopLevel(this) is not { } owner) return;
        string safeName = string.Concat(item.Name.Select(c => c < 32 || c is '/' or '\\' || Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        using var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.CloudStorageDownload,
            SuggestedFileName = safeName,
            ShowOverwritePrompt = true,
        });
        if (file == null || !vm.IsActionCurrent(context)) return;
        string? local = file.TryGetLocalPath();
        string temporary = Path.Combine(local != null ? Path.GetDirectoryName(local)! : Path.GetTempPath(), $".beutl-download-{Guid.NewGuid():N}.tmp");
        using var cancellation = new CancellationTokenSource();
        var dialog = new FAContentDialog
        {
            Title = Strings.CloudStorageDownloading,
            CloseButtonText = Strings.Cancel,
            Content = new StackPanel { Spacing = 12, Children = { new TextBlock { Text = item.Name, TextWrapping = TextWrapping.Wrap }, new ProgressBar { IsIndeterminate = true } } },
        };
        bool finished = false;
        dialog.Closed += (_, _) => { if (!finished) cancellation.Cancel(); };
        var shown = ShowStorageDialogAsync(dialog);
        try
        {
            bool downloaded;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                downloaded = await vm.DownloadAsync(context, output, cancellation.Token);
            if (!downloaded || cancellation.IsCancellationRequested) return;
            if (local != null) File.Move(temporary, local, overwrite: true);
            else
            {
                await using var input = File.OpenRead(temporary);
                await using var output = await file.OpenWriteAsync();
                await input.CopyToAsync(output, cancellation.Token);
            }
        }
        finally
        {
            finished = true;
            dialog.Hide();
            await shown;
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void OnStorageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is CloudStorageViewModel { DetailsItem.Value: not null } vm)
            vm.DetailsItem.Value = StorageItems.SelectedItems is { Count: 1 } selected
                && selected[0] is CloudStorageItem { IsFolder: false } item ? item : null;
    }

    private void OnCloseStorageDetails(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CloudStorageViewModel vm) vm.DetailsItem.Value = null;
    }
}
