using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Platform.Storage.FileIO;

using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.FileSystemGlobbing.Internal;
using Microsoft.Extensions.FileSystemGlobbing.Internal.Patterns;

namespace Beutl.Controls;

[TemplatePart("PART_Button", typeof(Button))]
[TemplatePart("PART_SelectedFileDisplay", typeof(TextBlock))]
public class FileInputArea : ContentControl
{
    public static readonly StyledProperty<IStorageFile> SelectedFileProperty
        = AvaloniaProperty.Register<FileInputArea, IStorageFile>(
            nameof(SelectedFile),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<FilePickerOpenOptions> OpenOptionsProperty
        = AvaloniaProperty.Register<FileInputArea, FilePickerOpenOptions>(
            nameof(OpenOptions),
            validate: x => x?.AllowMultiple != true);

    public static readonly DirectProperty<FileInputArea, string> TextProperty
        = TextBlock.TextProperty.AddOwner<FileInputArea>(o => o.Text, (o, v) => o.Text = v, DefaultTextValue);

    private const string DefaultTextValue = "To open the file, drop it here or click here.";
    private static readonly FilePickerOpenOptions s_defaultOptions = new();
    private string _text = DefaultTextValue;
    private Button _button;
    private TextBlock _selectedFileDisplay;
    private List<IPatternContext> _patternContexts;
    private FileInfo _matchResult;

    public IStorageFile SelectedFile
    {
        get => GetValue(SelectedFileProperty);
        set => SetValue(SelectedFileProperty, value);
    }

    public FilePickerOpenOptions OpenOptions
    {
        get => GetValue(OpenOptionsProperty);
        set => SetValue(OpenOptionsProperty, value);
    }

    public string Text
    {
        get => _text;
        set => SetAndRaise(TextProperty, ref _text, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _button = e.NameScope.Get<Button>("PART_Button");
        _selectedFileDisplay = e.NameScope.Get<TextBlock>("PART_SelectedFileDisplay");

        _button.Click += OnButtonClick;

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        OnSelectedFileChanged();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.DragEffects != DragDropEffects.None
                && _matchResult != null)
            {
                SelectedFile = new BclStorageFile(_matchResult);
            }
        }
        finally
        {
            OnDragLeave(sender, e);
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        _matchResult = null;
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (_patternContexts != null
            && e.Data.GetFileNames() is { } files)
        {
            _matchResult = Match(_patternContexts, files);
            if (_matchResult != null)
            {
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }
    }

    private async void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (VisualRoot is TopLevel toplevel)
        {
            try
            {
                IsEnabled = false;
                IReadOnlyList<IStorageFile> items = await toplevel.StorageProvider.OpenFilePickerAsync(OpenOptions ?? s_defaultOptions);
                if (items.Count > 0)
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        IStorageFile item = items[i];
                        if (i == 0)
                        {
                            SelectedFile = items[0];
                        }
                        else
                        {
                            item.Dispose();
                        }
                    }
                }
                else
                {
                    SelectedFile = null;
                }
            }
            finally
            {
                IsEnabled = true;
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(SelectedFile))
        {
            OnSelectedFileChanged();
        }
        else if (change.Property.Name is nameof(OpenOptions))
        {
            OnOpenOptionsChanged();
        }
    }

    private void OnSelectedFileChanged()
    {
        if (_selectedFileDisplay != null)
        {
            if (SelectedFile == null)
            {
                _selectedFileDisplay.Text = null;
                _selectedFileDisplay.IsVisible = false;
            }
            else
            {
                _selectedFileDisplay.Text = SelectedFile.Name;
                _selectedFileDisplay.IsVisible = true;
            }
        }
    }

    private void OnOpenOptionsChanged()
    {
        if (OpenOptions != null)
        {
            _patternContexts = BuildPatternContexts(OpenOptions);
        }
        else
        {
            _patternContexts = null;
        }
    }

    private static FileInfo Match(List<IPatternContext> patternContexts, IEnumerable<string> files)
    {
        foreach (string file in files)
        {
            var fi = new FileInfo(file);
            var fiWrapper = new FileInfoWrapper(fi);
            var diWrapper = new DirectoryInfoWrapper(fi.Directory);

            foreach (IPatternContext item in patternContexts)
            {
                item.PushDirectory(diWrapper);
                if (item.Test(fiWrapper).IsSuccessful)
                {
                    item.PopDirectory();
                    return fi;
                }
                item.PopDirectory();
            }
        }

        return null;
    }

    private static List<IPatternContext> BuildPatternContexts(FilePickerOpenOptions options)
    {
        var builder = new PatternBuilder(StringComparison.OrdinalIgnoreCase);
        var list = new List<IPatternContext>();

        foreach (FilePickerFileType item in options.FileTypeFilter)
        {
            foreach (string patternStr in item.Patterns)
            {
                IPattern pattern = builder.Build(patternStr);

                list.Add(pattern.CreatePatternContextForInclude());
            }
        }

        return list;
    }
}
