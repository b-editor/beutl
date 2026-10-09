using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Beutl.Controls.PropertyEditors;

public class StorageFileEditor : StringEditor
{
    public static readonly StyledProperty<FilePickerOpenOptions> OpenOptionsProperty =
        AvaloniaProperty.Register<StorageFileEditor, FilePickerOpenOptions>(nameof(OpenOptions));

    public static readonly DirectProperty<StorageFileEditor, FileInfo?> ValueProperty =
        AvaloniaProperty.RegisterDirect<StorageFileEditor, FileInfo?>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    private FileInfo? _value;
    private FileInfo? _oldValue;
    private string _oldText = string.Empty;

    public StorageFileEditor()
    {
        OpenOptions = new FilePickerOpenOptions();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public FilePickerOpenOptions OpenOptions
    {
        get => GetValue(OpenOptionsProperty);
        set => SetValue(OpenOptionsProperty, value);
    }

    public FileInfo? Value
    {
        get => _value;
        set
        {
            if (SetAndRaise(ValueProperty, ref _value, value))
            {
                Text = value?.FullName ?? string.Empty;
            }
        }
    }

    protected override Type StyleKeyOverride => typeof(StorageFileEditor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        Button button = e.NameScope.Get<Button>("PART_Button");
        button.Click += OnButtonClick;

        UpdateErrors();
    }

    private async void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is TopLevel { StorageProvider: { } storage })
        {
            IReadOnlyList<IStorageFile> result = await storage.OpenFilePickerAsync(OpenOptions);
            if (result is [var file] && file.TryGetLocalPath() is string localPath)
            {
                ConfirmValue(new FileInfo(localPath));
            }
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool canDrop = GetDroppedFile(e.DataTransfer) != null;
        e.DragEffects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        InnerTextBox?.Classes.Set("dragover", canDrop);
        e.Handled = e.DataTransfer.Contains(DataFormat.File);
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        InnerTextBox?.Classes.Set("dragover", false);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        InnerTextBox?.Classes.Set("dragover", false);
        if (!e.DataTransfer.Contains(DataFormat.File)) return;

        e.Handled = true;
        if (GetDroppedFile(e.DataTransfer) is { } file)
        {
            e.DragEffects = DragDropEffects.Copy;
            ConfirmValue(file);
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private FileInfo? GetDroppedFile(IDataTransfer data)
    {
        if (IsReadOnly || !IsEffectivelyEnabled) return null;

        IReadOnlyList<FilePickerFileType>? filters = OpenOptions.FileTypeFilter;
        foreach (IStorageItem item in data.TryGetFiles() ?? [])
        {
            if (item is not IStorageFile
                || item.TryGetLocalPath() is not { } path
                || !File.Exists(path))
                continue;

            string name = Path.GetFileName(path);
            if (filters is not { Count: > 0 }
                || filters.Any(type => FilePickerFileTypeMatcher.Matches(type, name)))
            {
                return new FileInfo(path);
            }
        }

        return null;
    }

    private void ConfirmValue(FileInfo file)
    {
        FileInfo? oldValue = Value;
        Value = file;
        _oldValue = Value;
        _oldText = Text;
        RaiseEvent(new PropertyEditorValueChangedEventArgs<FileInfo?>(Value, oldValue, ValueConfirmedEvent));
    }

    protected override void OnTextBoxGotFocus(FocusChangedEventArgs e)
    {
        if (InnerTextBox == null) return;
        if (!DataValidationErrors.GetHasErrors(InnerTextBox))
        {
            _oldText = Text;
            _oldValue = Value;
        }
    }

    protected override void OnTextBoxLostFocus(RoutedEventArgs e)
    {
        if (InnerTextBox == null) return;
        if (!DataValidationErrors.GetHasErrors(InnerTextBox))
        {
            Value = GetStorageFile(Text);
            if (Text != _oldText)
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<FileInfo?>(Value, _oldValue, ValueConfirmedEvent));
            }
        }
    }

    protected override void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        UpdateErrors();
    }

    private static bool FileExists(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        if (File.Exists(value))
        {
            return true;
        }

        return false;
    }

    private static FileInfo? GetStorageFile(string value)
    {
        if (File.Exists(value))
        {
            return new FileInfo(value);
        }

        return null;
    }

    private void UpdateErrors()
    {
        if (InnerTextBox == null) return;
        if (FileExists(InnerTextBox.Text))
        {
            DataValidationErrors.ClearErrors(InnerTextBox);
        }
        else
        {
            DataValidationErrors.SetErrors(InnerTextBox, DataValidationMessages.FileDoesNotExist);
        }
    }
}
