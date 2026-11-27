using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors;
using Beutl.Editor.Services.AI;
using Beutl.Editor.VersionControl;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Models;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
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
        .Add<FileThumbnailService>(() => EditorService.AppServices.GetRequiredService<FileThumbnailService>())
        .Add<DecoderRegistry>(() => EditorService.AppServices.GetRequiredService<DecoderRegistry>())
        .Add<Beutl.Media.FontManager>(() => _fonts)
        .Add<INotificationService>(() => EditorService.AppServices.GetRequiredService<INotificationService>())
        .Add<GlobalConfiguration>(() => EditorService.AppServices.GetRequiredService<GlobalConfiguration>())
        .Add<LibraryService>(() => EditorService.AppServices.GetRequiredService<LibraryService>())
        .Add<ObjectTemplateService>(() => EditorService.AppServices.GetRequiredService<ObjectTemplateService>())
        .Add<Beutl.Services.Tutorials.ITutorialService>(() => EditorService.AppServices.GetRequiredService<Beutl.Services.Tutorials.ITutorialService>())
        .Add<Beutl.Controls.PropertyEditors.ColorPaletteService>(() => EditorService.AppServices.GetRequiredService<Beutl.Controls.PropertyEditors.ColorPaletteService>())
        .Add<Beutl.Api.BeutlApiApplication>(() => EditorService.ApiApplication)
        .Add<IEditorFileUsage>(() => EditorService)
        .Add<Beutl.Editor.Components.WebBrowserTab.IBrowserSettingsHost>(() => EditorService.BrowserSettingsHost)
        .Add<Beutl.Editor.Components.FileBrowserTab.FileBrowserStorageProviderRegistry>(() => EditorService.StorageProviders)
        .Add<Beutl.Editor.Components.FileBrowserTab.IProjectAssetHost>(() => EditorService.ProjectAssets)
        .Add<Beutl.Editor.Components.AiWorkspaceTab.IAiWorkspaceHost>(() => EditorService.AiWorkspaceHost)
        .Add<Beutl.Editor.Components.AgentTab.IAgentWorkspaceHost>(() => EditorService.AgentWorkspaceHost)
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
        .Add<EditedElements>(() => _unsavedElements)
        .Add<AutoSaveService>(() => _autoSaveService)
        .Add<IProjectVersionControlService>(() => EditorService.ProjectVersionControlService.Value)
        .Add<IReadOnlyReactiveProperty<IProjectVersionControlService?>>(() => EditorService.ProjectVersionControlService)
        .Add<IProjectVersionControlCoordinator>(() => EditorService.ProjectVersionControlCoordinator)
        .Add<IProjectFileWriteAdmission>(() => new ProjectFileWriteAdmission(EditorService))
        .Add<ITimelineOptionsProvider>(() => _timelineOptionsProvider)
        .Add<IEditorClock>(() => _editorClock)
        .Add<IEditorSelection>(() => _editorSelection)
        .Add<IEffectComparisonSelection>(() => Player)
        .AddOwned<IPropertyLinkSelection>(() => new PropertyLinkSelectionService())
        .Add<IElementAdder>(() => _elementAdder)
        .Add<IClipboardGateway>(() => _clipboardGateway)
        .Add<IPreviewPlayer>(() => Player)
        .Add<Beutl.Editor.Components.Helpers.IFullScaleFrameRenderer>(() => Player)
        .Add<Beutl.Editor.Components.Helpers.IPreviewEyedropper>(() => Player)
        .Add<Beutl.Editor.Components.Helpers.IPreviewPuppetPinEditor>(() => Player)
        .Add<Beutl.Editor.Components.Helpers.IToolDockHost>(() => DockHost)
        .Add<Beutl.Editor.Components.OutputTab.IOutputRunHost>(() => EditorService)
        .Add<Beutl.Editor.Components.OutputTab.IOutputPresetStore>(() => OutputPresetService.Instance)
        .Add<Beutl.Editor.Components.MissingMediaTab.IMediaRepairHost>(() => this)
        .Add<Beutl.Editor.Components.UndisplayableDataTab.IUndisplayableDataNavigator>(() => this)
        .Add<PathEditorViewModel>(() => Player?.PathEditor)
        .Add<FrameCacheManager>(() => FrameCacheManager.Value)
        .Add<IBufferStatus>(() => BufferStatus)
        .Add<Beutl.Api.Services.ExtensionProvider>(() => ExtensionProvider)
        // The lease-backed view, for editor components that offer work to extensions.
        .Add<IExtensionProvider>(() => ExtensionProvider)
        // The swap-stable facades, so a cached reference survives a store-root or cap change.
        .Add<IProxyStore>(() => EditorService.AppServices.GetService<ProxyMediaServices>()?.StoreFacade)
        .Add<IProxyResolver>(() => EditorService.AppServices.GetService<ProxyMediaServices>()?.ResolverFacade)
        .Add<IProxyJobQueue>(() => EditorService.AppServices.GetService<ProxyMediaServices>()?.QueueFacade)
        .Add<ProxyEvictionService>(() => EditorService.AppServices.GetService<ProxyMediaServices>()?.EvictionService)
        .Add<IProxyStoreCapInfo>(() => EditorService.AppServices.GetService<ProxyMediaServices>()?.CapInfoFacade)
        // The API clients live on the main window's view model, as for the AI tool tabs.
        .Add<IGenerativePromptLibrary>(() => _generativePromptLibrary
            ??= MainViewModel.TryGetCurrent(out MainViewModel? main) ? main.CreateGenerativePromptLibrary() : null)
        .Add<IGenerativeModelCatalog>(() => _generativeModelCatalog
            ??= MainViewModel.TryGetCurrent(out MainViewModel? main) ? main.CreateGenerativeModelCatalog() : null)
        .Add<IGenerativeNodeExecutor>(() => _generativeNodeExecutor
            ??= MainViewModel.TryGetCurrent(out MainViewModel? main) ? main.CreateGenerativeNodeExecutor(Scene) : null)
        .AddOwned<ISceneTimeRangeService>(() => new SceneTimeRangeService(HistoryManager))
        .AddOwned<IElementResizeService>(() => new ElementResizeService(HistoryManager, this.GetRequiredService<GlobalConfiguration>().EditorConfig, this.GetRequiredService<DecoderRegistry>()))
        .AddOwned<IElementSlipService>(() => new ElementSlipService(HistoryManager, this.GetRequiredService<DecoderRegistry>()))
        .AddOwned<IElementDuplicateService>(() => new ElementDuplicateService(HistoryManager, this.GetRequiredService<GlobalConfiguration>().EditorConfig))
        .AddOwned<IElementMoveService>(() => new ElementMoveService(
            HistoryManager, this.GetRequiredService<GlobalConfiguration>().EditorConfig, (IElementDuplicateService)GetService(typeof(IElementDuplicateService))!))
        .AddOwned<IElementGapService>(() => new ElementGapService(HistoryManager, this.GetRequiredService<GlobalConfiguration>().EditorConfig))
        .AddOwned<SceneFileImporter>(() => new SceneFileImporter(
            _elementAdder, this.GetRequiredService<DecoderRegistry>(), this.GetRequiredService<ObjectTemplateService>(),
            this.GetRequiredService<INotificationService>(), _elementAdder.HasFileSourceOverride, editor: this))
        .AddOwned<IElementClipboardService>(() => new ProjectFileWriteClipboardService(
            EditorService,
            new ElementClipboardService(
                HistoryManager, this.GetRequiredService<GlobalConfiguration>().EditorConfig,
                _clipboardGateway,
                (IElementDuplicateService)GetService(typeof(IElementDuplicateService))!,
                static () => ElementAccentColorGenerator.GenerateColor(typeof(Beutl.Graphics.SourceImage).FullName!),
                this.GetRequiredService<SceneFileImporter>(),
                new CaptionTemplateChooser().ChooseForPasteAsync), _autoSaveCancellation.Token))
        .AddOwned<IElementStructureService>(() => new ElementStructureService(HistoryManager, this.GetRequiredService<GlobalConfiguration>().EditorConfig, this.GetRequiredService<DecoderRegistry>()))
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

    private ElementNudgeService CreateNudgeService()
    {
        // The debounce timer fires off the UI thread; post the commit back so it serializes
        // with other editing ops.
        var nudge = new ElementNudgeService(HistoryManager, this.GetRequiredService<GlobalConfiguration>().EditorConfig, action => Dispatcher.UIThread.Post(action));
        // Drain pending nudges before Undo / Redo / JumpTo so they don't merge into the next
        // history transaction.
        HistoryManager.BeforeMutation
            .Subscribe(_ => nudge.Flush())
            .DisposeWith(_disposables);
        return nudge;
    }
}
