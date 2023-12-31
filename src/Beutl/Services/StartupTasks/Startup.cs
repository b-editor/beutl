using Beutl.Api;
using Beutl.Api.Services;
using Beutl.ViewModels;

namespace Beutl.Services.StartupTasks;

public sealed class Startup
{
    private readonly Dictionary<Type, Lazy<StartupTask>> _tasks = [];
    private readonly BeutlApiApplication _apiApp;

    public Startup(BeutlApiApplication apiApp)
    {
        _apiApp = apiApp;
        Initialize();
    }

    private async void Initialize()
    {
        using (Activity? activity = Telemetry.StartActivity("Startup"))
        {
            RegisterAll();
            await WaitAll().ConfigureAwait(false);
        }
    }

    public async Task WaitAll()
    {
        await Task.WhenAll(_tasks.Values.Select(v => v.Value.Task)).ConfigureAwait(false);
    }

    public async Task WaitLoadingExtensions()
    {
        LoadInstalledExtensionTask t1 = GetTask<LoadInstalledExtensionTask>();
        LoadPrimitiveExtensionTask t2 = GetTask<LoadPrimitiveExtensionTask>();
        LoadSideloadExtensionTask t3 = GetTask<LoadSideloadExtensionTask>();
        await Task.WhenAll(t1.Task, t2.Task, t3.Task).ConfigureAwait(false);
    }

    public T GetTask<T>()
        where T : StartupTask
    {
        if (_tasks.TryGetValue(typeof(T), out Lazy<StartupTask>? lazy))
        {
            return (T)lazy.Value;
        }

        foreach (KeyValuePair<Type, Lazy<StartupTask>> item in _tasks)
        {
            if (item.Key.IsAssignableTo(typeof(T)))
            {
                return (T)item.Value.Value;
            }
        }

        throw new Exception("Task not found");
    }

    private void RegisterAll()
    {
        Register(() => new AuthenticationTask(_apiApp));
        Register(() => new LoadInstalledExtensionTask(_apiApp.GetResource<PackageManager>()));
        Register(() => new LoadPrimitiveExtensionTask(_apiApp.GetResource<PackageManager>()));
        Register(() => new LoadSideloadExtensionTask(_apiApp.GetResource<PackageManager>()));
        Register(() => new AfterLoadingExtensionsTask(this));
        Register(() => new CheckForUpdatesTask(_apiApp));
    }

    private void Register<T>(Func<T> factory)
        where T : StartupTask
    {
        _tasks.Add(typeof(T), new Lazy<StartupTask>(() => factory()));
    }
}
