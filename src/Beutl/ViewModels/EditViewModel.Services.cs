using System.Diagnostics.CodeAnalysis;
using Beutl.Editor;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Services.AI;
using Beutl.Editor.VersionControl;
using Beutl.Media.Proxy;
using Beutl.Models;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Beutl.Services;
using Reactive.Bindings;
using Dispatcher = Avalonia.Threading.Dispatcher;

namespace Beutl.ViewModels;

public partial class EditViewModel
{
    private readonly EditorServiceRegistry _services;
    private IGenerativeNodeExecutor? _generativeNodeExecutor;
    private IGenerativeModelCatalog? _generativeModelCatalog;
    private IGenerativePromptLibrary? _generativePromptLibrary;

    public object? GetService(Type serviceType) => _services.GetService(serviceType);

    // For tests: every type the registry answers.
    internal IReadOnlyCollection<Type> ServiceTypes => _services.Types;

    private EditorServiceRegistry CreateServices() => new EditorServiceRegistry()
        .Add<IEditorFileUsage>(() => EditorService)
        .Add<Beutl.Editor.Components.WebBrowserTab.IBrowserSettingsHost>(() => EditorService.BrowserSettingsHost)
        .Add<Beutl.Editor.Components.FileBrowserTab.FileBrowserStorageProviderRegistry>(() => EditorService.StorageProviders)
        .Add<Beutl.Editor.Components.AiWorkspaceTab.IAiWorkspaceHost>(() => EditorService.AiWorkspaceHost)
        .Add<Scene>(() => Scene)
        .Add<EditViewModel>(() => this)
        .Add<IEditorContext>(() => this)
        .Add<ISavableEditorContext>(() => this)
        .Add<IUndoRedoEditorContext>(() => this)
        .Add<IAiJobResultEditorContext>(() => this)
        .Add<IPreviewRenderQuality>(() => this)
        .Add<IFrameCacheInvalidator>(() => this)
        .Add<IHistoryNavigator>(() => this)
        .Add<HistoryManager>(() => HistoryManager)
        .Add<IProjectVersionControlService>(() => EditorService.ProjectVersionControlService.Value)
        .Add<IReadOnlyReactiveProperty<IProjectVersionControlService?>>(() => EditorService.ProjectVersionControlService)
        .Add<IProjectVersionControlCoordinator>(() => EditorService.ProjectVersionControlCoordinator)
        .Add<IProjectFileWriteAdmission>(() => new ProjectFileWriteAdmission(EditorService))
        .Add<ITimelineOptionsProvider>(() => _timelineOptionsProvider)
        .Add<IEditorClock>(() => _editorClock)
        .Add<IEditorSelection>(() => _editorSelection)
        .Add<IElementAdder>(() => _elementAdder)
        .Add<IClipboardGateway>(() => _clipboardGateway)
        .Add<IPreviewPlayer>(() => Player)
        .Add<Beutl.Editor.Components.Helpers.IFullScaleFrameRenderer>(() => Player)
        .Add<Beutl.Editor.Components.Helpers.IToolDockHost>(() => DockHost)
        .Add<Beutl.Editor.Components.OutputTab.IOutputRunHost>(() => EditorService)
        .Add<Beutl.Editor.Components.OutputTab.IOutputPresetStore>(() => OutputPresetService.Instance)
        .Add<Beutl.Editor.Components.MissingMediaTab.IMediaRepairHost>(() => this)
        .Add<PathEditorViewModel>(() => Player?.PathEditor)
        .Add<FrameCacheManager>(() => FrameCacheManager.Value)
        .Add<IBufferStatus>(() => BufferStatus)
        .Add<Beutl.Api.Services.ExtensionProvider>(() => ExtensionProvider)
        // The swap-stable facades, so a cached reference survives a store-root or cap change.
        .Add<IProxyStore>(() => ProxyMediaServices.Current?.StoreFacade)
        .Add<IProxyResolver>(() => ProxyMediaServices.Current?.ResolverFacade)
        .Add<IProxyJobQueue>(() => ProxyMediaServices.Current?.QueueFacade)
        .Add<ProxyEvictionService>(() => ProxyMediaServices.Current?.EvictionService)
        .Add<IProxyStoreCapInfo>(() => ProxyMediaServices.Current?.CapInfoFacade)
        // The API clients live on the main window's view model, as for the AI tool tabs.
        .Add<IGenerativePromptLibrary>(() => _generativePromptLibrary
            ??= TryGetMainViewModel(out MainViewModel? main) ? main.CreateGenerativePromptLibrary() : null)
        .Add<IGenerativeModelCatalog>(() => _generativeModelCatalog
            ??= TryGetMainViewModel(out MainViewModel? main) ? main.CreateGenerativeModelCatalog() : null)
        .Add<IGenerativeNodeExecutor>(() => _generativeNodeExecutor
            ??= TryGetMainViewModel(out MainViewModel? main) ? main.CreateGenerativeNodeExecutor(Scene) : null)
        .AddOwned<ISceneTimeRangeService>(() => new SceneTimeRangeService(HistoryManager))
        .AddOwned<IElementResizeService>(() => new ElementResizeService(HistoryManager))
        .AddOwned<IElementSlipService>(() => new ElementSlipService(HistoryManager))
        .AddOwned<IElementDuplicateService>(() => new ElementDuplicateService(HistoryManager))
        .AddOwned<IElementMoveService>(() => new ElementMoveService(
            HistoryManager, (IElementDuplicateService)GetService(typeof(IElementDuplicateService))!))
        .AddOwned<IElementGapService>(() => new ElementGapService(HistoryManager))
        .AddOwned<IElementClipboardService>(() => new ProjectFileWriteClipboardService(
            EditorService,
            new ElementClipboardService(
                HistoryManager,
                _clipboardGateway,
                (IElementDuplicateService)GetService(typeof(IElementDuplicateService))!,
                static () => ElementAccentColorGenerator.GenerateColor(typeof(Beutl.Graphics.SourceImage).FullName!),
                _elementAdder)))
        .AddOwned<IElementStructureService>(() => new ElementStructureService(HistoryManager))
        .AddOwned<IElementAttributeService>(() => new ElementAttributeService(HistoryManager))
        .AddOwned<ITransitionEditorService>(() => new Beutl.Editor.Components.TransitionTab.TransitionEditorService(this))
        .AddOwned<IElementNudgeService>(() => _elementNudgeService = CreateNudgeService())
        .AddOwned<ILayerMoveService>(() => new LayerMoveService(HistoryManager))
        .AddOwned<ILayerAttributeService>(() => new LayerAttributeService(HistoryManager))
        .AddOwned<ISceneSettingsService>(() => new SceneSettingsService(HistoryManager))
        .AddOwned<IKeyFrameClipboardService>(() => new KeyFrameClipboardService(HistoryManager))
        .AddOwned<INodeGraphMutationService>(() => new NodeGraphMutationService(HistoryManager))
        .AddOwned<IElementObjectService>(() => new ElementObjectService(HistoryManager))
        .AddOwned<IPropertyEditorFactory>(() => new Beutl.Editor.Components.PropertyEditors.Services.PropertyEditorFactoryAdapter(ExtensionProvider))
        .AddOwned<IPropertiesEditorFactory>(() => new Beutl.Editor.Components.PropertyEditors.Services.PropertiesEditorFactoryImpl(ExtensionProvider));

    private static bool TryGetMainViewModel([NotNullWhen(true)] out MainViewModel? main)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime
            && lifetime.MainWindow?.DataContext is MainViewModel found)
        {
            main = found;
            return true;
        }

        main = null;
        return false;
    }

    private ElementNudgeService CreateNudgeService()
    {
        // The debounce timer fires off the UI thread; post the commit back so it serializes
        // with other editing ops.
        var nudge = new ElementNudgeService(HistoryManager, action => Dispatcher.UIThread.Post(action));
        // Drain pending nudges before Undo / Redo / JumpTo so they don't merge into the next
        // history transaction.
        HistoryManager.BeforeMutation
            .Subscribe(_ => nudge.Flush())
            .DisposeWith(_disposables);
        return nudge;
    }
}
