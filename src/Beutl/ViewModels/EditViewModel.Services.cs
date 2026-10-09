using System.Diagnostics.CodeAnalysis;
using Beutl.Editor;
using Beutl.Editor.VersionControl;
using Beutl.Media.Proxy;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Reactive.Bindings;
using Dispatcher = Avalonia.Threading.Dispatcher;

namespace Beutl.ViewModels;

public partial class EditViewModel
{
    private Beutl.NodeGraph.Generative.IGenerativeNodeExecutor? _generativeNodeExecutor;
    private Beutl.NodeGraph.Generative.IGenerativeModelCatalog? _generativeModelCatalog;
    private Beutl.NodeGraph.Generative.IGenerativePromptLibrary? _generativePromptLibrary;
    private Beutl.Editor.Components.TimelineTab.Generative.TimelineGenerationService? _timelineGeneration;
    private Beutl.Editor.Services.AI.ITimelineAiHost? _timelineAiHost;

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Beutl.Editor.Services.IEditorFileUsage))
            return EditorService;
        if (serviceType == typeof(Beutl.Editor.Components.WebBrowserTab.IBrowserSettingsHost))
            return EditorService.BrowserSettingsHost;
        if (serviceType == typeof(Beutl.Editor.Components.FileBrowserTab.FileBrowserStorageProviderRegistry))
            return EditorService.StorageProviders;
        if (serviceType == typeof(Scene))
            return Scene;

        if (serviceType.IsAssignableTo(typeof(IEditorContext)))
            return this;

        if (serviceType == typeof(HistoryManager))
            return HistoryManager;

        if (serviceType == typeof(IProjectVersionControlService))
            return EditorService.ProjectVersionControlService.Value;

        if (serviceType == typeof(IReadOnlyReactiveProperty<IProjectVersionControlService?>))
            return EditorService.ProjectVersionControlService;

        if (serviceType == typeof(IProjectVersionControlCoordinator))
            return EditorService.ProjectVersionControlCoordinator;

        if (serviceType == typeof(IProjectFileWriteAdmission))
            return new ProjectFileWriteAdmission(EditorService);

        if (serviceType.IsAssignableTo(typeof(ITimelineOptionsProvider)))
            return _timelineOptionsProvider;

        if (serviceType.IsAssignableTo(typeof(IEditorClock)))
            return _editorClock;

        if (serviceType.IsAssignableTo(typeof(IEditorSelection)))
            return _editorSelection;

        if (serviceType.IsAssignableTo(typeof(IElementAdder)))
            return _elementAdder;

        if (serviceType.IsAssignableTo(typeof(ISceneTimeRangeService)))
            return _sceneTimeRangeService ??= new SceneTimeRangeService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementResizeService)))
            return _elementResizeService ??= new ElementResizeService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementSlipService)))
            return _elementSlipService ??= new ElementSlipService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementDuplicateService)))
            return _elementDuplicateService ??= new ElementDuplicateService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementMoveService)))
            return _elementMoveService ??= new ElementMoveService(
                HistoryManager,
                (IElementDuplicateService)GetService(typeof(IElementDuplicateService))!);

        if (serviceType.IsAssignableTo(typeof(IElementGapService)))
            return _elementGapService ??= new ElementGapService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IClipboardGateway)))
            return _clipboardGateway;

        if (serviceType.IsAssignableTo(typeof(IElementClipboardService)))
            return _elementClipboardService ??= _clipboardGateway is null
                ? null!
                : new ProjectFileWriteClipboardService(
                    EditorService,
                    new ElementClipboardService(
                        HistoryManager,
                        _clipboardGateway,
                        (IElementDuplicateService)GetService(typeof(IElementDuplicateService))!,
                        static () => Beutl.Editor.Components.Helpers.ColorGenerator.GenerateColor(
                            typeof(Beutl.Graphics.SourceImage).FullName!),
                        _elementAdder));

        if (serviceType.IsAssignableTo(typeof(IElementStructureService)))
            return _elementStructureService ??= new ElementStructureService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementAttributeService)))
            return _elementAttributeService ??= new ElementAttributeService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementNudgeService)))
            return _elementNudgeService ??= CreateNudgeService();

        if (serviceType.IsAssignableTo(typeof(ILayerMoveService)))
            return _layerMoveService ??= new LayerMoveService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(ILayerAttributeService)))
            return _layerAttributeService ??= new LayerAttributeService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(ISceneSettingsService)))
            return _sceneSettingsService ??= new SceneSettingsService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IKeyFrameClipboardService)))
            return _keyFrameClipboardService ??= new KeyFrameClipboardService(HistoryManager);

        if (serviceType == typeof(Beutl.NodeGraph.Generative.IGenerativePromptLibrary))
        {
            if (_generativePromptLibrary is null
                && TryGetMainViewModel(out MainViewModel? main))
            {
                _generativePromptLibrary = main.CreateGenerativePromptLibrary();
            }

            return _generativePromptLibrary;
        }

        if (serviceType == typeof(Beutl.NodeGraph.Generative.IGenerativeModelCatalog))
        {
            if (_generativeModelCatalog is null
                && TryGetMainViewModel(out MainViewModel? main))
            {
                _generativeModelCatalog = main.CreateGenerativeModelCatalog();
            }

            return _generativeModelCatalog;
        }

        if (serviceType == typeof(Beutl.NodeGraph.Generative.IGenerativeNodeExecutor))
        {
            // The API clients live on the main window's view model, as for the AI tool tabs.
            if (_generativeNodeExecutor is null
                && TryGetMainViewModel(out MainViewModel? main))
            {
                _generativeNodeExecutor = main.CreateGenerativeNodeExecutor(Scene);
            }

            return _generativeNodeExecutor;
        }

        if (serviceType == typeof(Beutl.Editor.Components.TimelineTab.Generative.TimelineGenerationService))
        {
            // Runs on the same executor as the AI nodes, so it carries the same billing safeguards.
            if (_timelineGeneration is null && !_disposed)
            {
                _timelineGeneration = new Beutl.Editor.Components.TimelineTab.Generative.TimelineGenerationService(
                    Scene,
                    HistoryManager,
                    _elementAdder,
                    () => GetService(typeof(Beutl.NodeGraph.Generative.IGenerativeNodeExecutor))
                        as Beutl.NodeGraph.Generative.IGenerativeNodeExecutor,
                    () => Beutl.Services.AI.AiResultImporter.GetResourceDirectory(Scene));
            }

            return _timelineGeneration;
        }

        if (serviceType == typeof(Beutl.Editor.Services.AI.ITimelineAiHost))
        {
            if (_timelineAiHost is null
                && TryGetMainViewModel(out MainViewModel? main))
            {
                _timelineAiHost = main.CreateTimelineAiHost(this);
            }

            return _timelineAiHost;
        }

        if (serviceType.IsAssignableTo(typeof(INodeGraphMutationService)))
            return _nodeGraphMutationService ??= new NodeGraphMutationService(HistoryManager);

        if (serviceType.IsAssignableTo(typeof(IElementObjectService)))
            return _elementObjectService ??= new ElementObjectService(HistoryManager);

        if (serviceType == typeof(PlayerViewModel) || serviceType.IsAssignableTo(typeof(IPreviewPlayer)))
            return Player;

        if (serviceType == typeof(IPreviewRenderQuality))
            return this;

        if (serviceType == typeof(FrameCacheManager))
            return FrameCacheManager.Value;

        if (serviceType.IsAssignableTo(typeof(IBufferStatus)))
            return BufferStatus;

        if (serviceType == typeof(Beutl.Api.Services.ExtensionProvider))
            return ExtensionProvider;

        if (serviceType.IsAssignableTo(typeof(IPropertyEditorFactory)))
            return _propertyEditorFactory ??= new Services.Adapters.PropertyEditorFactoryAdapter(ExtensionProvider);

        if (serviceType.IsAssignableTo(typeof(IPropertiesEditorFactory)))
            return _propertiesEditorFactory ??= new Services.Adapters.PropertiesEditorFactoryImpl(ExtensionProvider);

        // Hand out the swap-stable facades so a cached reference survives a store-root / cap change; the
        // concrete ProxyEvictionService request is the exception (no facade for the concrete type).
        if (serviceType.IsAssignableTo(typeof(IProxyStore)))
            return ProxyMediaServices.Current?.StoreFacade;

        if (serviceType.IsAssignableTo(typeof(IProxyResolver)))
            return ProxyMediaServices.Current?.ResolverFacade;

        if (serviceType.IsAssignableTo(typeof(IProxyJobQueue)))
            return ProxyMediaServices.Current?.QueueFacade;

        if (serviceType == typeof(ProxyEvictionService))
            return ProxyMediaServices.Current?.EvictionService;

        if (serviceType.IsAssignableTo(typeof(IProxyStoreCapInfo)))
            return ProxyMediaServices.Current?.CapInfoFacade;

        return null;
    }

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
