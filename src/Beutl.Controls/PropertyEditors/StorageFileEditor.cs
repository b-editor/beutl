using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Platform.Storage.FileIO;
using Avalonia.Styling;

namespace Beutl.Controls.PropertyEditors;

public class StorageFileEditor : StringEditor, IStyleable
{
    public static readonly StyledProperty<FilePickerOpenOptions> OpenOptionsProperty =
        AvaloniaProperty.Register<StorageFileEditor, FilePickerOpenOptions>(nameof(OpenOptions));

    public static readonly DirectProperty<StorageFileEditor, IStorageFile> ValueProperty =
        AvaloniaProperty.RegisterDirect<StorageFileEditor, IStorageFile>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    private IStorageFile _value;
    private IStorageFile _oldValue;
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

    public IStorageFile Value
    {
        get => _value;
        set
        {
            string text = value?.Path?.LocalPath ?? "";
            if (SetAndRaise(ValueProperty, ref _value, value))
            {
                Text = text;
            }
        }
    }

    Type IStyleable.StyleKey => typeof(StorageFileEditor);

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
            if (result is [var file])
            {
                IStorageFile oldValue = Value;
                Value = file;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<IStorageFile>(file, oldValue, ValueChangedEvent));
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
                RaiseEvent(new PropertyEditorValueChangedEventArgs<IStorageFile>(Value, _oldValue, ValueChangedEvent));
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

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri uri)
            && uri.IsFile)
        {
            return File.Exists(uri.LocalPath);
        }

        return false;
    }

    private static IStorageFile GetStorageFile(string value)
    {
        if (File.Exists(value))
        {
            return new BclStorageFile(value);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri uri)
            && uri.IsFile)
        {
            return new BclStorageFile(uri.LocalPath);
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
