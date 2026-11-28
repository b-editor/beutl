using Beutl.Api.Services;
using Beutl.Editor.Components.AiWorkspaceTab;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Components.AiWorkspaceTab.ViewModels;
using Beutl.Editor.Services.Captions;
using Beutl.Media.Decoding;
using Beutl.Services;
using Beutl.Services.AI;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.ViewModels;

public partial class MainViewModel : IAiWorkspaceHost
{
    internal AiImageGenerationDialogViewModel CreateAiImageGenerationToolViewModel(EditViewModel editViewModel)
        => HoldSessionWhileRunning(editViewModel, new AiImageGenerationDialogViewModel(
            AppServices.GetRequiredService<INotificationService>(),
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAiModelCatalogService>(),
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiImageGenerationService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            _beutlClients.GetResource<IAiJobClient>(),
            _beutlClients.GetResource<IAiJobKindRegistry>(),
            editViewModel,
            _aiRequestRecoveryContext), static page => page.IsGenerating);

    internal AiGenerativeModelCatalog CreateGenerativeModelCatalog()
        => new(
            _beutlClients.GetResource<IAiModelCatalogService>(),
            _beutlClients.GetResource<IAiEntitlementService>());

    internal Beutl.NodeGraph.Generative.IGenerativeNodeExecutor CreateGenerativeNodeExecutor(Beutl.ProjectSystem.Scene scene)
        => new AiGenerativeNodeExecutor(
            scene,
            CreateGenerativeModelCatalog(),
            _beutlClients.GetResource<IAiImageGenerationService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            CreateGenerativePromptLibrary(),
            _beutlClients.GetResource<IAiImageEditingService>(),
            _beutlClients.GetResource<IAiVideoService>(),
            _beutlClients.GetResource<IAiJobKindRegistry>(), AppServices.GetRequiredService<DecoderRegistry>(),
            _aiRequestRecoveryContext, _beutlClients.GetResource<IAiJobClient>())
        {
            // The project is read when the generation starts, so a scene moved meanwhile leases where it is then.
            BeginActivity = () => _editorService.WorkspaceFor(scene).Activity.Begin(SessionActivityKind.AiGeneration),
        };

    internal Beutl.NodeGraph.Generative.IGenerativePromptLibrary CreateGenerativePromptLibrary()
        => new AiGenerativePromptLibrary(PromptLibraryProvider.For(_aiRequestRecoveryContext));

    internal AiImageEditDialogViewModel CreateAiImageEditToolViewModel(EditViewModel editViewModel)
        => HoldSessionWhileRunning(editViewModel, new AiImageEditDialogViewModel(
            AppServices.GetRequiredService<INotificationService>(),
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAiModelCatalogService>(),
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiImageEditingService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            _beutlClients.GetResource<IAiJobClient>(),
            _beutlClients.GetResource<IAiJobKindRegistry>(),
            editViewModel,
            _aiRequestRecoveryContext), static page => page.IsEditing);

    internal AiSubtitleDialogViewModel CreateAiSubtitleToolViewModel(EditViewModel? editViewModel)
    {
        _captionCatalog.RefreshObjectTemplates(CaptionCatalog.FindObjectTemplates(_objectTemplateService));
        // The editor's fonts, so caption previews draw the project's bundled fonts.
        var renderServices = new SceneRenderServices(
            editViewModel?.GetService(typeof(Beutl.Media.FontManager)) as Beutl.Media.FontManager
                ?? AppServices.GetRequiredService<Beutl.Media.FontManager>(),
            AppServices.GetRequiredService<DecoderRegistry>(),
            Configuration.EditorConfig,
            Configuration.ProxyStoreConfig);
        return HoldSessionWhileRunning(editViewModel, new AiSubtitleDialogViewModel(AppServices.GetRequiredService<INotificationService>(),
            renderServices,
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAiModelCatalogService>(),
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiTranscriptionService>(),
            _beutlClients.GetResource<IAiCaptionTranslationService>(),
            _captionCatalog,
            CaptionDraftStoreProvider.Current,
            CreateCaptionDraftScopes(editViewModel),
            editViewModel, _aiRequestRecoveryContext), static page => page.IsSubtitleOperationActive);
    }

    private IObservable<CaptionDraftScope?> CreateCaptionDraftScopes(EditViewModel? editViewModel)
        => _beutlClients.AuthenticatedUser.Select(user =>
        {
            // The drafts belong to the editor's own project, which stays loaded in the background after a switch.
            Project? project = editViewModel?.Scene.FindHierarchicalParent<Project>();
            return user is null || project is null || editViewModel is null
                ? null
                : new CaptionDraftScope(user.Profile.Id, project.Id.Value, editViewModel.Scene.Id.Value);
        });

    internal AiVideoGenerationDialogViewModel CreateAiVideoGenerationToolViewModel(EditViewModel editViewModel, AiSourceVideoMode? sourceMode = null)
        => HoldSessionWhileRunning(editViewModel, new AiVideoGenerationDialogViewModel(
            AppServices.GetRequiredService<INotificationService>(),
            AppServices.GetRequiredService<DecoderRegistry>(),
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAiModelCatalogService>(),
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiVideoService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            _beutlClients.GetResource<IAiJobKindRegistry>(),
            _beutlClients.GetResource<IAiJobMonitor>(),
            _beutlClients.GetResource<IAiJobClient>(),
            editViewModel,
            _aiRequestRecoveryContext, sourceMode), static page => page.IsGenerating);

    // An AI page of an editor keeps the session of the editor's project busy while it generates, edits or runs a
    // subtitle job, wherever the page is: the AI tabs of one editor can be many, each with its own pages. The page
    // keeps the hold and ends it once its disposal has drained its operations, whatever the flag still reads.
    internal static TPage HoldSessionWhileRunning<TPage>(
        EditViewModel? editViewModel, TPage page, Func<TPage, IObservable<bool>> running)
        where TPage : IAiPageLifetime
    {
        if (editViewModel is not null)
            page.ReleaseWhenDrained(editViewModel.Workspace.Activity.HoldWhile(running(page), SessionActivityKind.AiGeneration));
        return page;
    }

    internal async void OpenAiJobCenter()
        => await OpenAiWorkspaceAsync(AiWorkspaceSection.Jobs);

    internal async Task<object?> OpenAiWorkspaceAsync(AiWorkspaceSection section, EditViewModel? target = null)
    {
        EditViewModel? editorContext = target ?? _editorService.SelectedTabItem.Value?.Context.Value as EditViewModel;
        if (editorContext is null
            || !_editorService.TabItems.Any(item => ReferenceEquals(item.Context.Value, editorContext)))
        {
            return null;
        }

        // Opening a page activates a tab already showing it or switches an existing AI tab to it.
        AiWorkspaceViewModel? workspace =
            editorContext.FindToolTab<AiWorkspaceViewModel>(tab => tab.SelectedSection.Value?.Id == section)
            ?? editorContext.FindToolTab<AiWorkspaceViewModel>();

        if (workspace is null)
        {
            workspace = CreateAiWorkspaceViewModel(editorContext);
            if (!await TryOpenNewAiWorkspaceAsync(
                    workspace,
                    () => editorContext.OpenToolTab(workspace)))
            {
                return null;
            }
        }
        else
        {
            if (!editorContext.OpenToolTab(workspace))
            {
                return null;
            }
        }

        return _editorService.TabItems.Any(item => ReferenceEquals(item.Context.Value, editorContext))
            ? workspace.Show(section)
            : null;
    }

    internal async Task<bool> PresentCaptionResultAsync(EditViewModel editor, AiCaptionHistoryResult result)
    {
        if (await OpenAiWorkspaceAsync(AiWorkspaceSection.Subtitles, editor)
            is not AiSubtitleDialogViewModel viewModel)
            return false;
        viewModel.LoadHistoryResult(result);
        return true;
    }

    internal static async Task<bool> TryOpenNewAiWorkspaceAsync(
        AiWorkspaceViewModel workspace,
        Func<bool> tryOpen)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(tryOpen);
        if (tryOpen())
            return true;

        await workspace.DisposeAsync();
        return false;
    }

    AiWorkspaceViewModel? IAiWorkspaceHost.CreateWorkspace(IEditorContext editorContext)
        => editorContext is EditViewModel editViewModel ? CreateAiWorkspaceViewModel(editViewModel) : null;

    internal AiWorkspaceViewModel CreateAiWorkspaceViewModel(EditViewModel editViewModel)
    {
        IAiEntitlementService entitlementService =
            _beutlClients.GetResource<IAiEntitlementService>();
        var workspace = new AiWorkspaceViewModel(
            editViewModel,
            section => CreateAiPage(section, editViewModel),
            entitlementService.Entitlements,
            _beutlClients.AuthenticatedUser,
            _aiPlanCoordinator,
            token => _beutlClients.SignInAsync(token),
            entitlementService);

        // A tab added while another is open is added to see something else, so it
        // starts on the first page no open tab is showing.
        if (FindUnshownSection(editViewModel) is { } section)
        {
            workspace.Show(section);
        }

        return workspace;
    }

    private static AiWorkspaceSection? FindUnshownSection(EditViewModel editViewModel)
    {
        AiWorkspaceSection[] shown = editViewModel.DockHost.Factory.EnumerateTools()
            .Select(tool => tool.ToolContext)
            .OfType<AiWorkspaceViewModel>()
            .Select(tab => tab.SelectedSection.Value?.Id)
            .OfType<AiWorkspaceSection>()
            .ToArray();

        return shown.Length == 0
            ? null
            : Enum.GetValues<AiWorkspaceSection>().Cast<AiWorkspaceSection?>()
                .FirstOrDefault(section => !shown.Contains(section!.Value));
    }

    private IAsyncDisposable CreateAiPage(AiWorkspaceSection section, EditViewModel editViewModel)
        => section switch
        {
            AiWorkspaceSection.ImageGeneration => CreateAiImageGenerationToolViewModel(editViewModel),
            AiWorkspaceSection.ImageEdit => CreateAiImageEditToolViewModel(editViewModel),
            AiWorkspaceSection.VideoGeneration => CreateAiVideoGenerationToolViewModel(editViewModel),
            AiWorkspaceSection.VideoEditing => new AiVideoEditingViewModel(mode => CreateAiVideoGenerationToolViewModel(editViewModel, mode)),
            AiWorkspaceSection.Subtitles => CreateAiSubtitleToolViewModel(editViewModel),
            AiWorkspaceSection.Jobs => CreateAiJobCenterViewModel(editViewModel),
            _ => throw new ArgumentOutOfRangeException(nameof(section)),
        };

    internal AiJobCenterViewModel CreateAiJobCenterViewModel(EditViewModel editViewModel)
        => new(
            editViewModel,
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            _beutlClients.GetResource<IAiJobClient>(),
            _beutlClients.GetResource<IAiJobMonitor>(),
            _beutlClients.GetResource<IAiJobKindRegistry>(),
            _aiJobResultHandlers,
            _aiRequestRecoveryContext,
            result => PresentCaptionResultAsync(editViewModel, result));
}
