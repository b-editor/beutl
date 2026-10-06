using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Beutl.AgentHost;
using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.Services.AI;
using Beutl.Editor.Services.Captions;
using Beutl.Editor.VersionControl;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Services.PrimitiveImpls;
using Beutl.Services.StartupTasks;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.ExtensionsPages;
using Beutl.ViewModels.Tools;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Logging;
using NuGet.Packaging.Core;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MainViewModel
{
    internal AiImageGenerationDialogViewModel CreateAiImageGenerationToolViewModel(EditViewModel editViewModel)
        => new(
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAiModelCatalogService>(),
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiImageGenerationService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            editViewModel,
            _aiRequestRecoveryContext);

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
            _beutlClients.GetResource<IAiJobKindRegistry>());

    internal Beutl.NodeGraph.Generative.IGenerativePromptLibrary CreateGenerativePromptLibrary()
        => new AiGenerativePromptLibrary(PromptLibraryProvider.For(_aiRequestRecoveryContext));

    internal AiImageEditDialogViewModel CreateAiImageEditToolViewModel(EditViewModel editViewModel)
        => new(
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAiModelCatalogService>(),
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiImageEditingService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            editViewModel,
            _aiRequestRecoveryContext);

    internal AiSubtitleDialogViewModel CreateAiSubtitleToolViewModel(EditViewModel? editViewModel)
    {
        _captionCatalog.RefreshObjectTemplates(
            Beutl.Editor.Services.ObjectTemplateService.Instance.FindByBaseType(
                typeof(Beutl.Graphics.Drawable)));
        return new AiSubtitleDialogViewModel(
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAiModelCatalogService>(),
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiTranscriptionService>(),
            _beutlClients.GetResource<IAiCaptionTranslationService>(),
            _captionCatalog,
            CaptionDraftStoreProvider.Current,
            CreateCaptionDraftScopes(editViewModel),
            editViewModel);
    }

    private IObservable<CaptionDraftScope?> CreateCaptionDraftScopes(EditViewModel? editViewModel)
        => _beutlClients.AuthenticatedUser.Select(user =>
        {
            Project? project = BeutlApplication.Current.Project;
            return user is null || project is null || editViewModel is null
                ? null
                : new CaptionDraftScope(user.Profile.Id, project.Id, editViewModel.Scene.Id);
        });

    internal AiVideoGenerationDialogViewModel CreateAiVideoGenerationToolViewModel(EditViewModel editViewModel, AiSourceVideoMode? sourceMode = null)
        => new(
            _beutlClients.GetResource<IAiEntitlementService>(),
            _beutlClients.GetResource<IAiOperationAvailabilityService>(),
            _beutlClients.GetResource<IAiModelCatalogService>(),
            _aiPlanCoordinator,
            _beutlClients.GetResource<IAiVideoService>(),
            _beutlClients.GetResource<IAuthenticatedContentService>(),
            _beutlClients.GetResource<IAiJobKindRegistry>(),
            _beutlClients.GetResource<IAiJobMonitor>(),
            editViewModel,
            _aiRequestRecoveryContext, sourceMode);

    internal async void OpenAiJobCenter()
        => await OpenAiWorkspaceAsync(AiWorkspaceSection.Jobs);

    internal async void OpenAiImageGeneration()
        => await OpenAiWorkspaceAsync(AiWorkspaceSection.ImageGeneration);

    internal async void OpenAiImageEdit()
        => await OpenAiWorkspaceAsync(AiWorkspaceSection.ImageEdit);

    internal async void OpenAiSubtitle(AiCaptionHistoryResult? historyResult = null)
    {
        if (await OpenAiWorkspaceAsync(AiWorkspaceSection.Subtitles) is AiSubtitleDialogViewModel viewModel
            && historyResult is not null)
        {
            viewModel.LoadHistoryResult(historyResult);
        }
    }

    internal async void OpenAiVideoEditing()
        => await OpenAiWorkspaceAsync(AiWorkspaceSection.VideoEditing);

    internal async void OpenAiVideoGeneration()
        => await OpenAiWorkspaceAsync(AiWorkspaceSection.VideoGeneration);

    private async Task<object?> OpenAiWorkspaceAsync(AiWorkspaceSection section, EditViewModel? target = null)
    {
        EditViewModel? editorContext = target ?? _editorService.SelectedTabItem.Value?.Context.Value as EditViewModel;
        if (editorContext is null
            || !_editorService.TabItems.Any(item => ReferenceEquals(item.Context.Value, editorContext)))
        {
            return null;
        }

        // A tab already on that page is the one the person means. Otherwise an open
        // AI tab is turned to it, because the menu is a request to see something,
        // not a request for another tab; the tab strip's own button adds those.
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
            result => PresentCaptionResultAsync(editViewModel, result));
}
