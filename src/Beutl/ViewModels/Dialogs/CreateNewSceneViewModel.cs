using Avalonia;
using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class CreateNewSceneViewModel
{
    private readonly Project _proj;
    private readonly EditorService _editorService;

    public CreateNewSceneViewModel(ProjectService projectService, EditorService editorService)
    {
        _editorService = editorService;
        _proj = projectService.CurrentProject.Value
            ?? throw new InvalidOperationException("A scene can be created only in an open project.");
        Location.Value = Path.GetDirectoryName(_proj.Uri!.LocalPath)!;
        Name.Value = NewDocumentValidation.UniqueName(Location.Value, "Scene");

        Name.SetValidateNotifyError(n => NewDocumentValidation.ValidateName(n, Location.Value));
        Location.Subscribe(_ => Name.ForceValidate());
        Size.SetValidateNotifyError(NewDocumentValidation.ValidateSize);
        FrameRate.SetValidateNotifyError(NewDocumentValidation.ValidateFrameRate);
        SampleRate.SetValidateNotifyError(NewDocumentValidation.ValidatePositive);

        CanCreate = Name.CombineLatest(Location, Size, FrameRate, SampleRate)
            .Select(t =>
            {
                (string name, string location, PixelSize size, string framerate, int samplerate) = t;
                return NewDocumentValidation.CanCreateAt(name, location, size)
                    && NewDocumentValidation.ParseFrameRate(framerate) is { Numerator: > 0, Denominator: > 0 }
                    && samplerate > 0;
            })
            .ToReadOnlyReactivePropertySlim();
        Create = new AsyncReactiveCommand(CanCreate);
        Create.Subscribe(async () =>
        {
            // A worktree mutation, such as a branch switch or a project being deleted from disk, could
            // replace or remove the folder the scene goes to while it is written and its tab opens.
            using IDisposable? open = _editorService.TryBeginEditorFileOpen();
            if (open is null)
            {
                _editorService.AppServices.GetRequiredService<INotificationService>().ShowWarning(Strings.CreateNewScene, MessageStrings.ProjectFilesBeingChanged);
                return;
            }

            Scene scene;
            try
            {
                scene = new Scene(Size.Value.Width, Size.Value.Height, Name.Value)
                {
                    FrameRate = { Value = NewDocumentValidation.ParseFrameRate(FrameRate.Value)!.Value },
                    SampleRate = { Value = SampleRate.Value },
                    ChannelLayout = { Value = ChannelLayout.Value },
                };
                CoreSerializer.StoreToUri(scene,
                    UriHelper.CreateFromPath(Path.Combine(Location.Value, Name.Value,
                        $"{Name.Value}.{EditorConstants.SceneFileExtension}")));

                // Keep the source tab selected until the new editor can inherit its layout.
                using (_editorService.SuppressProjectItemActivation())
                {
                    ProjectPersistence.AddItemAndPersist(_proj, scene);
                }
            }
            catch (Exception ex)
            {
                // Surface a scene-write or persist failure. AddItemAndPersist has already rolled the
                // add back; awaited so Handle()'s API-error path runs instead of being dropped.
                await ex.Handle(_editorService.AppServices.GetRequiredService<INotificationService>());
                return;
            }

            // Activation is not part of persistence, so a failure here must not be reported as a
            // save failure — kept outside the try above.
            _editorService.ActivateNewScene(scene);
        });
    }

    public ReactiveProperty<PixelSize> Size { get; } = new(new PixelSize(1920, 1080));

    // A rate from FrameRates or another, such as 12 or 30000/1001.
    public ReactiveProperty<string> FrameRate { get; } = new("30");

    public IReadOnlyList<string> FrameRates => FrameRateText.Presets;

    public ReactiveProperty<int> SampleRate { get; } = new(44100);

    public ReactiveProperty<SpeakerLayout> ChannelLayout { get; } = new(SpeakerLayout.Stereo);

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> Location { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanCreate { get; }

    public AsyncReactiveCommand Create { get; }
}
