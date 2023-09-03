using Beutl.Api;
using Beutl.Api.Services;
using Beutl.ViewModels;

namespace Beutl.Services.StartupTasks;

public sealed class Startup
{
    private readonly Dictionary<Type, Func<StartupTask>> _tasks = new();
    private readonly BeutlApiApplication _apiApp;
    private readonly MainViewModel _viewModel;

    public Startup(BeutlApiApplication apiApp, MainViewModel viewModel)
    {
        _apiApp = apiApp;
        _viewModel = viewModel;
        RegisterAll();
    }

    public Task Run()
    {
        return Task.WhenAll(_tasks.Values.Select(v => v().Task));
    }

    public T GetTask<T>()
        where T : StartupTask
    {
        if (_tasks.TryGetValue(typeof(T), out Func<StartupTask>? func))
        {
            return (T)func();
        }

        foreach (KeyValuePair<Type, Func<StartupTask>> item in _tasks)
        {
            if (item.Key.IsAssignableTo(typeof(T)))
            {
                return (T)item.Value();
            }
        }

        throw new Exception("Task not found");
    }

    private void RegisterAll()
    {
        Register(() => new AuthenticationTask(_apiApp));
        Register(() => new LoadInstalledExtensionTask(GetTask<AuthenticationTask>(), _apiApp.GetResource<PackageManager>()));
        Register(() => new LoadPrimitiveExtensionTask());
        Register(() => new LoadSideloadExtensionTask(_apiApp.GetResource<PackageManager>()));
        Register(() => new AfterLoadingExtensionsTask(this, _viewModel));
        Register(() => new CheckForUpdatesTask());
    }

    private void Register<T>(Func<T> factory)
        where T : StartupTask
    {
        StartupTask? obj = null;
        _tasks.Add(typeof(T), () =>
        {
            obj ??= factory();
            return obj;
        });
    }
}
