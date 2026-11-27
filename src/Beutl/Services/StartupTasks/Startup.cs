using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.ViewModels.ExtensionsPages;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;

namespace Beutl.Services.StartupTasks;

public sealed class Startup
{
    private readonly Dictionary<Type, Lazy<StartupTask>> _tasks = [];
    private readonly BeutlApiApplication _apiApp;
    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;

    public Startup(BeutlApiApplication apiApp, ProjectService projectService, EditorService editorService)
        : this(apiApp, projectService, editorService, editorService.AppServices.GetRequiredService<GlobalConfiguration>().LastStartedVersion)
    {
    }

    internal Startup(BeutlApiApplication apiApp, ProjectService projectService, EditorService editorService, string lastStartedVersion)
    {
        _apiApp = apiApp;
        _projectService = projectService;
        _editorService = editorService;
        IsAppUpgrade = NuGetVersion.TryParse(lastStartedVersion, out var previous) && NuGetVersion.Parse(BeutlApplication.Version) > previous;
        Initialize();
    }

    internal INotificationService Notifications => _editorService.AppServices.GetRequiredService<INotificationService>();

    internal GlobalConfiguration Configuration => _editorService.AppServices.GetRequiredService<GlobalConfiguration>();

    internal bool IsAppUpgrade { get; }

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

    internal PackageOperationHandler CreatePackageOperations() => new(_apiApp, _editorService, _projectService);

    public async Task WaitLoadingExtensions()
    {
        LoadInstalledExtensionTask t1 = GetTask<LoadInstalledExtensionTask>();
        LoadPrimitiveExtensionTask t2 = GetTask<LoadPrimitiveExtensionTask>();
        await Task.WhenAll(t1.Task, t2.Task).ConfigureAwait(false);

        LoadSideloadExtensionTask t3 = GetTask<LoadSideloadExtensionTask>();
        await t3.WaitForAcceptedLoadingAsync().ConfigureAwait(false);
    }

    public T GetTask<T>()
        where T : StartupTask
    {
        return (T)_tasks[typeof(T)].Value;
    }

    private void RegisterAll()
    {
        Register(() => new AuthenticationTask(_apiApp, Notifications));
        Register(() => new ResolvePackageDependenciesTask(
            _apiApp.GetResource<InstalledPackageRepository>(),
            _apiApp.GetResource<PackageInstaller>()));
        Register(() => new CrashRecoveryPromptTask(Configuration.ViewConfig));
        Register(() => new LoadInstalledExtensionTask(
            _apiApp.GetResource<PackageManager>(), this));
        Register(() => new LoadPrimitiveExtensionTask(
            _apiApp.GetResource<PackageManager>(),
            _editorService, _projectService));
        Register(() => new LoadSideloadExtensionTask(_apiApp.GetResource<PackageManager>(), this));
        Register(() => new AfterLoadingExtensionsTask(this));
        Register(() => new RestoreLastProjectTask(this, _projectService));
        Register(() => new CheckForUpdatesTask(_apiApp, _editorService.AppServices));
        Register(() => new CheckForPackageUpdatesTask(this, _apiApp.GetResource<PackageManager>(), _apiApp));
    }

    private void Register<T>(Func<T> factory)
        where T : StartupTask
    {
        _tasks.Add(typeof(T), new Lazy<StartupTask>(() => factory()));
    }
}
