using System.Reactive.Linq;

using Avalonia;

using BeUtl.Framework.Services;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using S = BeUtl.Language.StringResources;

namespace BeUtl.ViewModels.Dialogs;

public sealed class CreateNewProjectViewModel
{
    public CreateNewProjectViewModel()
    {
        Location.Value = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Name.Value = GenProjectName(Location.Value);

        Name.SetValidateNotifyError(n =>
        {
            if (Directory.Exists(Path.Combine(Location.Value, n)))
            {
                return S.Warning.ItAlreadyExists;
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
                return S.Warning.ValueLessThanOrEqualToZero;
            }
            else
            {
                return null;
            }
        });
        FrameRate.SetValidateNotifyError(n =>
        {
            if (n <= 0)
            {
                return S.Warning.ValueLessThanOrEqualToZero;
            }
            else
            {
                return null;
            }
        });
        SampleRate.SetValidateNotifyError(n =>
        {
            if (n <= 0)
            {
                return S.Warning.ValueLessThanOrEqualToZero;
            }
            else
            {
                return null;
            }
        });

        CanCreate = Name.CombineLatest(Location, Size, FrameRate, SampleRate)
            .Select(t =>
            {
                (string name, string location, PixelSize size, int framerate, int samplerate) = t;

                return !Directory.Exists(Path.Combine(location, name)) &&
                    size.Width > 0 &&
                    size.Height > 0 &&
                    framerate > 0 &&
                    samplerate > 0;
            })
            .ToReadOnlyReactivePropertySlim();
        Create = new ReactiveCommand(CanCreate);
        Create.Subscribe(() =>
        {
            IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();

            service.CreateProject(
                Size.Value.Width, Size.Value.Height,
                FrameRate.Value, SampleRate.Value,
                Name.Value,
                Location.Value);
        });
    }

    public ReactiveProperty<PixelSize> Size { get; } = new(new PixelSize(1920, 1080));

    public ReactiveProperty<int> FrameRate { get; } = new(30);

    public ReactiveProperty<int> SampleRate { get; } = new(44100);

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> Location { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanCreate { get; }

    public ReactiveCommand Create { get; }

    private static string GenProjectName(string location)
    {
        const string name = "Project";
        int n = 1;

        while (Directory.Exists(Path.Combine(location, name + n)))
        {
            n++;
        }

        return name + n;
    }
}
