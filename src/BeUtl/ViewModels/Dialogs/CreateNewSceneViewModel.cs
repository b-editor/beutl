using Avalonia;

using Beutl.Framework;
using Beutl.Framework.Services;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class CreateNewSceneViewModel
{
    private readonly IWorkspace? _proj;

    public CreateNewSceneViewModel()
    {
        IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
        _proj = service.CurrentProject.Value;
        CanAddToCurrentProject = service.CurrentProject.Select(i => i != null).ToReadOnlyReactivePropertySlim();
        AddToCurrentProject = new(_proj != null);

        Location.Value = GetInitialLocation();
        Name.Value = GenSceneName(Location.Value);

        Name.SetValidateNotifyError(n =>
        {
            if (Directory.Exists(Path.Combine(Location.Value, n)))
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

            return !Directory.Exists(Path.Combine(location, name)) &&
                size.Width > 0 &&
                size.Height > 0;
        }).ToReadOnlyReactivePropertySlim();
        Create = new ReactiveCommand(CanCreate);
        Create.Subscribe(() =>
        {
            IWorkspaceItemContainer container = ServiceLocator.Current.GetRequiredService<IWorkspaceItemContainer>();
            EditorService editPage = ServiceLocator.Current.GetRequiredService<EditorService>();
            var scene = new Scene(Size.Value.Width, Size.Value.Height, Name.Value);
            container.Add(scene);
            scene.Save(Path.Combine(Location.Value, Name.Value, $"{Name.Value}.{Constants.SceneFileExtension}"));

            if (_proj != null && AddToCurrentProject.Value)
            {
                _proj.Items.Add(scene);
                editPage.ActivateTabItem(scene.FileName, TabOpenMode.FromProject);
            }
            else
            {
                editPage.ActivateTabItem(scene.FileName, TabOpenMode.YourSelf);
            }
        });
    }

    public ReactiveProperty<PixelSize> Size { get; } = new(new PixelSize(1920, 1080));

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> Location { get; } = new();

    public ReactivePropertySlim<bool> AddToCurrentProject { get; }

    public ReadOnlyReactivePropertySlim<bool> CanAddToCurrentProject { get; }

    public ReadOnlyReactivePropertySlim<bool> CanCreate { get; }

    public ReactiveCommand Create { get; }

    private string GetInitialLocation()
    {
        if (_proj != null)
        {
            return _proj.RootDirectory;
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
