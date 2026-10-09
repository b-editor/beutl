using System.Reactive.Disposables;
using System.Reactive.Linq;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Services.AI;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Reactive.Bindings;

namespace Beutl.Services.AI;

/// <summary>The application's side of the timeline's AI generations.</summary>
internal sealed class TimelineAiHost(
    MainViewModel main,
    EditViewModel editor,
    IObservable<AuthenticatedUser?> user,
    IAiEntitlementService entitlements,
    IAiOperationAvailabilityService availability) : ITimelineAiHost
{
    // Signed out comes first: without an account there is no plan to read. A signed-in
    // account whose plan has not arrived yet is still loading, and generations stay offered.
    public IObservable<TimelineAiAccess> Access { get; } = user
        .CombineLatest(entitlements.Entitlements, (account, snapshot) => account is null
            ? TimelineAiAccess.SignInRequired
            : snapshot is null
                ? TimelineAiAccess.Loading
                : snapshot.CanUseAi ? TimelineAiAccess.Ready : TimelineAiAccess.PlanRequired)
        .DistinctUntilChanged();

    public void OpenAiWorkspace() => main.OpenAiWorkspaceFor(editor);

    public void OpenSubtitles(Element element) => main.OpenAiSubtitleFor(editor, element.Id);

    public ITimelineAiUsageEstimate CreateUsageEstimate() => new UsageEstimate(entitlements, availability);

    public string GetResourceDirectory(Scene scene) => AiResultImporter.GetResourceDirectory(scene);

    private sealed class UsageEstimate : ITimelineAiUsageEstimate
    {
        private readonly CompositeDisposable _disposables = [];
        private readonly CancellationTokenSource _lifetime = new();
        private readonly AiOperationAvailabilityTracker _tracker;

        public UsageEstimate(IAiEntitlementService entitlements, IAiOperationAvailabilityService availability)
        {
            _tracker = new AiOperationAvailabilityTracker(availability, _lifetime.Token).DisposeWith(_disposables);
            var usage = new AiUsageViewModel(entitlements.Entitlements).DisposeWith(_disposables);
            var estimate = new AiUsageEstimateViewModel(usage, _tracker.State).DisposeWith(_disposables);
            CanAfford = estimate.CanAfford;
            Explanation = estimate.Explanation;
        }

        public IObservable<bool> CanAfford { get; }

        public IObservable<string> Explanation { get; }

        public void Check(string operationId, string? modelId, int? durationSeconds)
        {
            AiModelId? model = string.IsNullOrWhiteSpace(modelId) ? null : new AiModelId(modelId);
            var operation = new AiOperationId(operationId);
            _tracker.Check(durationSeconds is { } seconds && seconds > 0
                ? new AiOperationAvailabilityRequest.Video(operation, seconds, model)
                : new AiOperationAvailabilityRequest.Fixed(operation, model));
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            _disposables.Dispose();
            _lifetime.Dispose();
        }
    }
}
