using System.Reactive.Linq;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;

namespace Beutl.HeadlessUITests;

// The AI dialog view models over a test's API clients. Unless a test passes its own, each gets a fresh form
// recovery context and a plan coordinator over the clients' entitlements.
internal static class AiDialogTestFactory
{
    public static AiImageGenerationDialogViewModel CreateImageGenerationDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null,
        AiRequestRecoveryContext? context = null,
        IAiPlanCoordinator? planCoordinator = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            planCoordinator ?? CreatePlanCoordinator(clients),
            clients.GetResource<IAiImageGenerationService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            editor,
            context ?? AiRetryTestContext.CreateForm());

    public static AiImageEditDialogViewModel CreateImageEditDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null,
        AiRequestRecoveryContext? context = null,
        IAiPlanCoordinator? planCoordinator = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            planCoordinator ?? CreatePlanCoordinator(clients),
            clients.GetResource<IAiImageEditingService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            editor,
            context ?? AiRetryTestContext.CreateForm());

    public static AiVideoGenerationDialogViewModel CreateVideoGenerationDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null,
        AiRequestRecoveryContext? context = null,
        AiSourceVideoMode? sourceMode = null,
        IAiOperationAvailabilityService? availability = null,
        IAiPlanCoordinator? planCoordinator = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            availability ?? clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            planCoordinator ?? CreatePlanCoordinator(clients),
            clients.GetResource<IAiVideoService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            clients.GetResource<IAiJobKindRegistry>(),
            clients.GetResource<IAiJobMonitor>(),
            editor,
            context ?? AiRetryTestContext.CreateForm(), sourceMode);

    public static AiSubtitleDialogViewModel CreateSubtitleDialog(
        BeutlApiApplication clients,
        EditViewModel? editor = null,
        ICaptionDraftStore? draftStore = null,
        IObservable<CaptionDraftScope?>? draftScopes = null,
        IAiOperationAvailabilityService? availabilityService = null)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            availabilityService ?? clients.GetResource<IAiOperationAvailabilityService>(),
            clients.GetResource<IAiModelCatalogService>(),
            CreatePlanCoordinator(clients),
            clients.GetResource<IAiTranscriptionService>(),
            clients.GetResource<IAiCaptionTranslationService>(),
            CaptionCatalog.CreateDefault("Default"),
            draftStore ?? CaptionDraftStoreProvider.Current,
            draftScopes ?? Observable.Return<CaptionDraftScope?>(null),
            editor);

    private static IAiPlanCoordinator CreatePlanCoordinator(BeutlApiApplication clients)
        => new AiPlanCoordinator(clients.GetResource<IAiEntitlementService>());
}
