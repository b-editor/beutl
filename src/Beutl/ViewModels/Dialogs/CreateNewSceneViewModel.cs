using Avalonia;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class CreateNewSceneViewModel
{
    private readonly Project? _proj;

    public CreateNewSceneViewModel()
    {
        _proj = ProjectService.Current.CurrentProject.Value;
        Location.Value = GetInitialLocation();
        Name.Value = GenSceneName(Location.Value);

        Name.SetValidateNotifyError(n =>
        {
            if (string.IsNullOrEmpty(n) || n.IndexOfAny(Path.GetInvalidFileNameChars()) > -1)
            {
                return Message.InvalidString;
            }
            else if (Directory.Exists(Path.Combine(Location.Value, n)))
            {
                return Message.ItAlreadyExists;
            }
            else
            {
                return null;
            }
        });
        Location.Subscribe(_ => Name.ForceValidate());
        Size.SetValidateNotifyError(s =>
        {
            if (s.Width <= 0 || s.Height <= 0)
            {
                return Message.ValueLessThanOrEqualToZero;
            }
            else
            {
                return null;
            }
        });

        CanCreate = Name.CombineLatest(Location, Size).Select(t =>
        {
            string name = t.First;
            string location = t.Second;
            PixelSize size = t.Third;

            if (location != null && name != null)
            {
                return !Directory.Exists(Path.Combine(location, name)) &&
                       size.Width > 0 &&
                       size.Height > 0;
            }
            else return false;
        }).ToReadOnlyReactivePropertySlim();
        Create = new ReactiveCommand(CanCreate);
        Create.Subscribe(() =>
        {
            var scene = new Scene(Size.Value.Width, Size.Value.Height, Name.Value);
            CoreSerializer.StoreToUri(scene, new Uri(new Uri("file://"), Path.Combine(Location.Value, Name.Value, $"{Name.Value}.{Constants.SceneFileExtension}")));

            _proj?.Items.Add(scene);

            EditorService.Current.ActivateTabItem(scene);
        });
    }

    public ReactiveProperty<PixelSize> Size { get; } = new(new PixelSize(1920, 1080));

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> Location { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanCreate { get; }

    public ReactiveCommand Create { get; }

    private string GetInitialLocation()
    {
        if (_proj != null)
        {
            return Path.GetDirectoryName(_proj.Uri!.LocalPath)!;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static string GenSceneName(string location)
    {
        const string name = "Scene";
        int n = 1;

        while (Directory.Exists(Path.Combine(location, name + n)))
        {
            n++;
        }

        return name + n;
    }
}
