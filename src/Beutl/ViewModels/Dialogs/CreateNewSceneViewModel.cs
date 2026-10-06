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
    private readonly EditorService _editorService;

    public CreateNewSceneViewModel(ProjectService projectService, EditorService editorService)
    {
        _editorService = editorService;
        _proj = projectService.CurrentProject.Value;
        Location.Value = GetInitialLocation();
        Name.Value = NewDocumentValidation.UniqueName(Location.Value, "Scene");

        Name.SetValidateNotifyError(n => NewDocumentValidation.ValidateName(n, Location.Value));
        Location.Subscribe(_ => Name.ForceValidate());
        Size.SetValidateNotifyError(NewDocumentValidation.ValidateSize);

        CanCreate = Name.CombineLatest(Location, Size)
            .Select(t => NewDocumentValidation.CanCreateAt(t.First, t.Second, t.Third))
            .ToReadOnlyReactivePropertySlim();
        Create = new AsyncReactiveCommand(CanCreate);
        Create.Subscribe(async () =>
        {
            // A worktree mutation, such as a branch switch or a project being deleted from disk, could
            // replace or remove the folder the scene goes to while it is written and its tab opens.
            using IDisposable? open = _editorService.TryBeginEditorFileOpen();
            if (open is null)
            {
                NotificationService.ShowWarning(Strings.CreateNewScene, MessageStrings.ProjectFilesBeingChanged);
                return;
            }

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
            _editorService.ActivateTabItem(scene);
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
}
