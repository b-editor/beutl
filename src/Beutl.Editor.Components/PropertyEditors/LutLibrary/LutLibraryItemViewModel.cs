using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

internal sealed class LutLibraryItemViewModel : INotifyPropertyChanged
{
    private Bitmap? _thumbnail;
    private string? _error;
    private bool _isFavorite;

    public LutLibraryItemViewModel(LutLibraryFile file)
    {
        Path = file.Path;
        Name = file.Name;
        _error = file.Error;
        _isFavorite = file.IsFavorite;
        string folder = System.IO.Path.GetDirectoryName(file.Path) ?? string.Empty;
        string folderName = System.IO.Path.GetFileName(folder);
        FolderName = folderName.Length > 0 ? folderName : folder;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; }

    public string Name { get; }

    public string FolderName { get; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
                return;

            _isFavorite = value;
            Raise();
        }
    }

    public string? Error
    {
        get => _error;
        private set
        {
            if (_error == value)
                return;

            _error = value;
            Raise();
            Raise(nameof(IsError));
        }
    }

    public bool IsError => _error != null;

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (ReferenceEquals(_thumbnail, value))
                return;

            _thumbnail = value;
            Raise();
        }
    }

    // A file can turn unreadable between the scan and the thumbnail.
    public void SetError(string error) => Error = error;

    public void SetThumbnail(Bitmap? thumbnail) => Thumbnail = thumbnail;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
