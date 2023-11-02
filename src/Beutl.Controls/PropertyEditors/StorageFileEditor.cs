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

    public static readonly DirectProperty<StorageFileEditor, FileInfo> ValueProperty =
        AvaloniaProperty.RegisterDirect<StorageFileEditor, FileInfo>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    private FileInfo _value;
    private FileInfo _oldValue;
    private string _oldText;

    public StorageFileEditor()
    {
        OpenOptions = new FilePickerOpenOptions();
    }

    public FilePickerOpenOptions OpenOptions
    {
        get => GetValue(OpenOptionsProperty);
        set => SetValue(OpenOptionsProperty, value);
    }

    public FileInfo Value
    {
        get => _value;
        set
        {
            if (SetAndRaise(ValueProperty, ref _value, value))
            {
                Text = value.FullName;
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

    private async void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (VisualRoot is TopLevel { StorageProvider: { } storage })
        {
            IReadOnlyList<IStorageFile> result = await storage.OpenFilePickerAsync(OpenOptions);
            if (result is [var file] && file.TryGetLocalPath() is string localPath)
            {
                FileInfo oldValue = Value;
                Value = new FileInfo(localPath);
                RaiseEvent(new PropertyEditorValueChangedEventArgs<FileInfo>(Value, oldValue, ValueConfirmedEvent));
            }
        }
    }

    protected override void OnTextBoxGotFocus(GotFocusEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(InnerTextBox))
        {
            _oldText = Text;
            _oldValue = Value;
        }
    }

    protected override void OnTextBoxLostFocus(RoutedEventArgs e)
    {
        if (!DataValidationErrors.GetHasErrors(InnerTextBox))
        {
            Value = GetStorageFile(Text);
            if (Text != _oldText)
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<FileInfo>(Value, _oldValue, ValueConfirmedEvent));
            }
        }
    }

    protected override void OnTextBoxTextChanged(string newValue, string oldValue)
    {
        UpdateErrors();
    }

    private static bool FileExists(string value)
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

    private static FileInfo GetStorageFile(string value)
    {
        if (File.Exists(value))
        {
            return new FileInfo(value);
        }

        return null;
    }

    private void UpdateErrors()
    {
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
