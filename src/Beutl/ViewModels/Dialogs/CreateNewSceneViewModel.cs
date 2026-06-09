using Avalonia;
using Beutl.Editor;
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
                return MessageStrings.InvalidString;
            }
            else if (Directory.Exists(Path.Combine(Location.Value, n)))
            {
                return MessageStrings.AlreadyExists;
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
                return MessageStrings.ValueLessThanOrEqualToZero;
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
        Create = new AsyncReactiveCommand(CanCreate);
        Create.Subscribe(async () =>
        {
            Scene scene;
            try
            {
                scene = new Scene(Size.Value.Width, Size.Value.Height, Name.Value);
                CoreSerializer.StoreToUri(scene,
                    UriHelper.CreateFromPath(Path.Combine(Location.Value, Name.Value,
                        $"{Name.Value}.{EditorConstants.SceneFileExtension}")));

                if (_proj != null)
                {
                    ProjectPersistence.AddItemAndPersist(_proj, scene);
                }
            }
            catch (Exception ex)
            {
                // Surface a scene-write or persist failure. AddItemAndPersist has already rolled the
                // add back; awaited so Handle()'s API-error path runs instead of being dropped.
                await ex.Handle();
                return;
            }

            // Activation is not part of persistence, so a failure here must not be reported as a
            // save failure — kept outside the try above.
            EditorService.Current.ActivateTabItem(scene);
        });
    }

    public ReactiveProperty<PixelSize> Size { get; } = new(new PixelSize(1920, 1080));

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> Location { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanCreate { get; }

    public AsyncReactiveCommand Create { get; }

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
