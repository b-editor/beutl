using System.Collections.Immutable;
using System.Text.Json;
using Beutl.Language;

namespace Beutl.Api.Services;

internal static class BuiltInAiJobKinds
{
    public static IReadOnlyList<AiJobKindDescriptor> Create(
        IAiImageGenerationService images,
        IAiVideoService videos,
        IAiEntitlementService entitlements,
        IAiOperationAvailabilityService availability,
        IAiModelCatalogService models,
        AiRetryAttemptContext retryContext)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(videos);
        ArgumentNullException.ThrowIfNull(entitlements);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(retryContext);

        var statuses = new AiJobStatusMap(
        [
            KeyValuePair.Create(AiJobStatuses.Queued, new AiJobStatusSemantics(false, true)),
            KeyValuePair.Create(AiJobStatuses.Running, new AiJobStatusSemantics(false, true)),
            KeyValuePair.Create(AiJobStatuses.Finalizing, new AiJobStatusSemantics(false, true)),
            KeyValuePair.Create(
                AiJobStatuses.Succeeded,
                new AiJobStatusSemantics(true, false, AiJobOutcomes.Succeeded)),
            KeyValuePair.Create(
                AiJobStatuses.Failed,
                new AiJobStatusSemantics(true, false, AiJobOutcomes.Failed)),
            KeyValuePair.Create(
                AiJobStatuses.Canceled,
                new AiJobStatusSemantics(true, false, AiJobOutcomes.Canceled)),
        ]);
        return
        [
            new AiJobKindDescriptor(
                AiJobKinds.Image,
                statuses)
            {
                RetryHandler = new AiImageJobRetryHandler(images, entitlements, availability, models, retryContext),
            },
            new AiJobKindDescriptor(
                AiJobKinds.ImageEdit,
                statuses),
            new AiJobKindDescriptor(
                AiJobKinds.Transcription,
                statuses),
            new AiJobKindDescriptor(
                AiJobKinds.CaptionTranslation,
                statuses),
            new AiJobKindDescriptor(
                AiJobKinds.Video,
                statuses)
            {
                RefreshHandler = new AiVideoJobRefreshHandler(videos),
                RetryHandler = new AiVideoJobRetryHandler(videos, entitlements, availability, models, retryContext),
            },
        ];
    }
}

internal static class AiJobInputParameters
{
    public static string? GetString(AiJob job, string propertyName)
    {
        if (job.InputParameters is not { ValueKind: JsonValueKind.Object } input
            || !input.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return NormalizeText(value.GetString());
    }

    public static int? GetInt32(AiJob job, string propertyName)
    {
        if (job.InputParameters is not { ValueKind: JsonValueKind.Object } input
            || !input.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int result))
        {
            return null;
        }

        return result;
    }

    public static bool? GetBoolean(AiJob job, string propertyName)
    {
        if (job.InputParameters is not { ValueKind: JsonValueKind.Object } input
            || !input.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    public static bool Has(AiJob job, string propertyName)
        => job.InputParameters is { ValueKind: JsonValueKind.Object } input
           && input.TryGetProperty(propertyName, out JsonElement value)
           && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal abstract class MeteredAiJobRetryHandler(
    AiOperationId operation,
    IAiEntitlementService entitlementService,
    IAiOperationAvailabilityService availabilityService,
    IAiModelCatalogService modelCatalogService,
    AiRetryAttemptContext retryContext) : IAiJobRetryHandler
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task>> _inFlight = [];

    protected async Task RunRetrySingleFlightAsync(
        AiJob job,
        CancellationToken cancellationToken,
        Func<string, bool, Task> operation,
        AiRetryAttempt attempt)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AiAuthenticatedRequestIdentity authenticated = retryContext.GetRequiredIdentity();
        if (!StringComparer.Ordinal.Equals(authenticated.AccountId, attempt.AccountId))
            throw new AiRetryAttemptRejectedException();
        string identity = CanonicalIdentity(job, authenticated.AccountId);
        string flightIdentity = $"{identity}:{attempt.Token}";
        var flight = new Lazy<Task>(
            () => ExecuteRetryAsync(job, authenticated, attempt, operation),
            LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<Task> selected = _inFlight.GetOrAdd(flightIdentity, flight);
        _ = selected.Value.ContinueWith(
            _ => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task>>(flightIdentity, selected)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            // Cancellation only stops this caller waiting. The paid operation is
            // deliberately executed with CancellationToken.None.
            await selected.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            if (selected.IsValueCreated && selected.Value.IsCompleted)
                _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task>>(flightIdentity, selected));
        }
    }

    private async Task ExecuteRetryAsync(
        AiJob job,
        AiAuthenticatedRequestIdentity authenticated,
        AiRetryAttempt attempt,
        Func<string, bool, Task> operation)
    {
        string key;
        bool isRepeat;
        long generation = 0;
        if (!retryContext.Store.TryConsumeAttempt(
                attempt,
                job,
                authenticated.AccountId,
                out key,
                out isRepeat))
        {
            throw new AiRetryAttemptRejectedException();
        }

        if (attempt.Kind == AiRetryAttemptKind.Recovery || isRepeat)
            generation = attempt.Generation;
        else
            generation = attempt.Generation + 1;

        using IDisposable authenticatedScope = retryContext.Enter(authenticated);
        try
        {
            await operation(key, isRepeat);
            retryContext.Store.TryRetire(
                job,
                authenticated.AccountId,
                key,
                generation,
                attempt.Token);
        }
        catch (Exception ex) when (IsDefinitive(ex))
        {
            retryContext.Store.TryRetire(
                job,
                authenticated.AccountId,
                key,
                generation,
                attempt.Token);
            throw;
        }
        catch
        {
            // A timeout/transport failure leaves the exact key available for a
            // subsequent confirmation. It must never be replaced by a fresh
            // key after an ambiguous provider response.
            retryContext.Store.TryRelease(
                job,
                authenticated.AccountId,
                key,
                generation,
                attempt.Token);
            throw;
        }
    }

    protected static string CanonicalIdentity(AiJob job, string accountId)
        => FileAiRetryKeyStore.CanonicalIdentity(job, accountId);

    protected static bool IsDefinitive(Exception exception)
        => exception is AiPlanRequiredException
            or AiUsageLimitExceededException
            or AiJobLimitReachedException
            or AiModelUnavailableException
            or AiModelDoesNotSupportRequestException
            or AiFileTooLargeException
            or AiProviderErrorException
            or AiRequestWasDeletedException;

    public virtual bool CanRetry(AiJob job, AiJobStatusSemantics status)
        => status.Outcome == AiJobOutcomes.Failed
            && job.CanRetry
            && HasCanonicalPrompt(job);

    private static bool HasCanonicalPrompt(AiJob job)
    {
        if (job.InputParameters is not { ValueKind: JsonValueKind.Object } input
            || !input.TryGetProperty("prompt", out JsonElement prompt)
            || prompt.ValueKind != JsonValueKind.String)
            return false;
        string? value = prompt.GetString();
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= AiRequestLimits.MaxPromptLength
            && string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }

    public async ValueTask<AiJobRetryPreflight> GetPreflightAsync(
        AiJob job,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            AiAuthenticatedRequestIdentity authenticated = retryContext.GetRequiredIdentity();
            // Preflight is deliberately resource-free. Reading the durable entry
            // identifies a request that may already have been paid for, but does
            // not create a pending confirmation or reserve a lease.
            if (retryContext.Store.TryGet(job, authenticated.AccountId, out _))
            {
                return new AiJobRetryPreflight(
                    IsAvailable: false,
                    CanSubmit: true,
                    Strings.AiResultUnavailable);
            }

            return await GetNewPurchasePreflightAsync(job, cancellationToken);
        }
        catch (AiRetryAttemptRejectedException ex)
        {
            throw new AiJobRetryPreparationRejectedException(ex);
        }
        catch (AiRetryStoreUnavailableException ex)
        {
            throw new AiJobRetryPreparationUnavailableException(ex);
        }
    }

    public async ValueTask<AiJobRetryPreparationResult> PrepareAsync(
        AiJob job,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRetryInputs(job);
            AiAuthenticatedRequestIdentity authenticated = retryContext.GetRequiredIdentity();

            // A durable entry means the request was already materialized in an
            // earlier process. It must bypass today's plan, balance, model, and
            // availability checks: those checks cannot make a paid request safer.
            bool isRecovery = retryContext.Store.TryGet(
                job,
                authenticated.AccountId,
                out _);
            if (!isRecovery)
            {
                AiJobRetryPreflight preflight = await GetNewPurchasePreflightAsync(
                    job,
                    cancellationToken);
                if (!preflight.CanSubmit)
                    return AiJobRetryPreparationResult.Blocked(preflight.Explanation);
            }

            // The durable confirmation token is created only after all checks for a
            // new purchase have passed. A recovery token is created from the exact
            // existing key and payload.
            AiRetryAttempt attempt = retryContext.Store.PrepareAttempt(
                job,
                authenticated.AccountId);
            return AiJobRetryPreparationResult.Ready(new RetryPreparation(this, job, attempt));
        }
        catch (AiRetryAttemptRejectedException ex)
        {
            throw new AiJobRetryPreparationRejectedException(ex);
        }
        catch (AiRetryStoreUnavailableException ex)
        {
            throw new AiJobRetryPreparationUnavailableException(ex);
        }
    }

    private async ValueTask<AiJobRetryPreflight> GetNewPurchasePreflightAsync(
        AiJob job,
        CancellationToken cancellationToken)
    {
        AiEntitlements? entitlements = await entitlementService.RefreshAsync(cancellationToken);
        AiJobRetryPreflight result;
        if (entitlements is null)
        {
            result = new AiJobRetryPreflight(false, false, Strings.AiPricingUnavailable);
        }
        else if (!entitlements.CanUseAi)
        {
            result = new AiJobRetryPreflight(true, false, Strings.AiProRequired);
        }
        else if (!await IsModelStillOfferedAsync(job, cancellationToken))
        {
            // The balance is not the problem, so saying it is would send the
            // user to buy credits that would not help.
            result = new AiJobRetryPreflight(true, false, Strings.AiModelUnavailable);
        }
        else if (entitlements.Availability.GetState(operation) == AiOperationAvailabilityState.Unavailable
                 || !await availabilityService.CheckAsync(
                     CreateAvailabilityRequest(job),
                     cancellationToken))
        {
            result = new AiJobRetryPreflight(true, false, Strings.AiEstimatedUsageInsufficient);
        }
        else
        {
            string explanation = entitlements.Balance.MonthlyUsage.IsExhausted
                ? Strings.AiEstimatedUsageTopUp
                : Strings.AiEstimatedUsageMonthly;
            result = new AiJobRetryPreflight(true, true, explanation);
        }

        return result;
    }

    protected abstract AiOperationAvailabilityRequest CreateAvailabilityRequest(AiJob job);

    protected abstract Task DispatchAsync(
        AiJob job,
        string idempotencyKey,
        bool isRepeat);

    protected virtual void ValidateRetryInputs(AiJob job)
    {
    }

    // A rerun repeats the model the job ran on, so a model that has since been
    // withdrawn cannot be repeated. Falling back to the operation's default
    // would quietly produce something else and charge the default's price for
    // it; the server refuses this too.
    private async Task<bool> IsModelStillOfferedAsync(
        AiJob job,
        CancellationToken cancellationToken)
    {
        if (job.Model is not { Value.Length: > 0 } model)
            return true;

        AiModelCatalog catalog = await modelCatalogService.GetAsync(cancellationToken);
        ImmutableArray<AiModelOption> models = catalog.ModelsFor(operation);
        // A catalog that could not be fetched says nothing about any model, and
        // the server has the last word regardless.
        if (models.IsDefaultOrEmpty)
            return true;

        return models.Any(option => option.Id == model);
    }

    private sealed class RetryPreparation(
        MeteredAiJobRetryHandler owner,
        AiJob job,
        AiRetryAttempt attempt) : IAiJobRetryPreparation
    {
        private int _state;

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
                throw new AiJobRetryPreparationRejectedException();

            try
            {
                try
                {
                    await owner.RunRetrySingleFlightAsync(
                        job,
                        cancellationToken,
                        (key, isRepeat) => owner.DispatchAsync(job, key, isRepeat),
                        attempt);
                }
                catch (AiRetryAttemptRejectedException ex)
                {
                    throw new AiJobRetryPreparationRejectedException(ex);
                }
                catch (AiRetryStoreUnavailableException ex)
                {
                    throw new AiJobRetryPreparationUnavailableException(ex);
                }
            }
            finally
            {
                // The pending confirmation token is consumed by the store
                // before dispatch. Dispose is therefore a no-op after consume,
                // while a pre-consume cancellation still abandons it.
                // Keep the attempt alive for another same-key confirmation
                // after an ambiguous response; the store's release path clears
                // the in-flight owner while retaining the exact key.
                attempt.Dispose();
                Volatile.Write(ref _state, 2);
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
                attempt.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class AiImageJobRetryHandler(
    IAiImageGenerationService images,
    IAiEntitlementService entitlementService,
    IAiOperationAvailabilityService availabilityService,
    IAiModelCatalogService modelCatalogService,
    AiRetryAttemptContext retryContext)
    : MeteredAiJobRetryHandler(
        AiOperations.ImageGeneration,
        entitlementService,
        availabilityService,
        modelCatalogService,
        retryContext)
{
    protected override AiOperationAvailabilityRequest CreateAvailabilityRequest(AiJob job)
        => new AiOperationAvailabilityRequest.Fixed(AiOperations.ImageGeneration, job.Model);

    protected override void ValidateRetryInputs(AiJob job)
    {
        // A generation guided by reference pictures cannot be repeated: the
        // pictures themselves were never retained, so this would produce
        // something else at full price. The server refuses it too. Both names
        // are checked: a job recorded while only one picture was allowed
        // carries "reference", and one recorded since carries "references".
        if (AiJobInputParameters.Has(job, "reference")
            || AiJobInputParameters.Has(job, "references"))
        {
            throw new InvalidOperationException(
                "An image generated from a reference image cannot be retried.");
        }
    }

    protected override async Task DispatchAsync(
        AiJob job,
        string idempotencyKey,
        bool isRepeat)
    {
        string prompt = AiJobInputParameters.GetString(job, "prompt")
            ?? throw new InvalidOperationException("The retained image prompt is missing.");
        await images.GenerateAsync(
            new AiImageGenerationRequest(
                prompt,
                new AiImageAspectRatioId(ResolveAspectRatio(job)),
                background: ResolveBackground(job),
                seed: AiJobInputParameters.GetInt32(job, "seed"),
                model: job.Model,
                idempotencyKey: idempotencyKey),
            CancellationToken.None);
    }

    private static AiImageBackgroundId ResolveBackground(AiJob job)
    {
        string? background = AiJobInputParameters.GetString(job, "background");
        return string.IsNullOrWhiteSpace(background)
            ? default
            : new AiImageBackgroundId(background);
    }

    // Jobs recorded before the endpoint spoke ratios carry the fixed size they
    // were asked for. Mapping it back is what keeps a repeat the same shape.
    private static string ResolveAspectRatio(AiJob job)
    {
        string? aspectRatio = AiJobInputParameters.GetString(job, "aspectRatio");
        if (aspectRatio is not null)
            return aspectRatio;

        return AiJobInputParameters.GetString(job, "size") switch
        {
            "1024x1536" => "2:3",
            "1536x1024" => "3:2",
            _ => "1:1",
        };
    }
}

internal sealed class AiVideoJobRetryHandler(
    IAiVideoService videos,
    IAiEntitlementService entitlementService,
    IAiOperationAvailabilityService availabilityService,
    IAiModelCatalogService modelCatalogService,
    AiRetryAttemptContext retryContext)
    : MeteredAiJobRetryHandler(
        AiOperations.VideoGeneration,
        entitlementService,
        availabilityService,
        modelCatalogService,
        retryContext)
{
    private static readonly HashSet<string> s_allowedProperties =
        ["prompt", "durationSeconds", "resolution", "aspectRatio", "generateAudio", "seed"];

    public override bool CanRetry(AiJob job, AiJobStatusSemantics status)
        => base.CanRetry(job, status)
            && TryParseReplayInput(job, out _);

    protected override AiOperationAvailabilityRequest CreateAvailabilityRequest(AiJob job)
        => new AiOperationAvailabilityRequest.Video(ParseReplayInput(job).DurationSeconds, job.Model);

    protected override void ValidateRetryInputs(AiJob job)
    {
        _ = ParseReplayInput(job);
    }

    protected override async Task DispatchAsync(
        AiJob job,
        string idempotencyKey,
        bool isRepeat)
    {
        VideoReplayInput input = ParseReplayInput(job);
        await videos.CreateAsync(
            new AiVideoGenerationRequest(
                input.Prompt,
                input.DurationSeconds,
                new AiVideoResolutionId(input.Resolution),
                new AiVideoAspectRatioId(input.AspectRatio),
                generateAudio: input.GenerateAudio,
                seed: input.Seed,
                model: job.Model,
                idempotencyKey: idempotencyKey),
            CancellationToken.None);
    }

    private static VideoReplayInput ParseReplayInput(AiJob job)
    {
        if (!TryParseReplayInput(job, out VideoReplayInput input))
            throw new InvalidOperationException("The retained video request cannot be replayed exactly.");
        return input;
    }

    private static bool TryParseReplayInput(AiJob job, out VideoReplayInput result)
    {
        result = default;
        if (job.InputParameters is not { ValueKind: JsonValueKind.Object } input)
            return false;
        foreach (JsonProperty property in input.EnumerateObject())
        {
            if (!s_allowedProperties.Contains(property.Name))
                return false;
        }
        if (!input.TryGetProperty("prompt", out JsonElement prompt)
            || prompt.ValueKind != JsonValueKind.String
            || prompt.GetString() is not { } promptValue
            || string.IsNullOrWhiteSpace(promptValue)
            || promptValue.Length > AiRequestLimits.MaxPromptLength
            || !string.Equals(promptValue, promptValue.Trim(), StringComparison.Ordinal))
            return false;

        int durationSeconds = 4;
        if (input.TryGetProperty("durationSeconds", out JsonElement duration))
        {
            if (duration.ValueKind != JsonValueKind.Number
                || !duration.TryGetInt32(out durationSeconds)
                || durationSeconds < AiRequestLimits.MinVideoDurationSeconds
                || durationSeconds > AiRequestLimits.MaxVideoDurationSeconds)
                return false;
        }

        string resolution = "720p";
        if (input.TryGetProperty("resolution", out JsonElement resolutionElement))
        {
            string? parsedResolution = resolutionElement.ValueKind == JsonValueKind.String
                ? resolutionElement.GetString()
                : null;
            if (parsedResolution is not ("480p" or "720p" or "1080p" or "2K"))
                return false;
            resolution = parsedResolution;
        }

        string aspectRatio = "16:9";
        if (input.TryGetProperty("aspectRatio", out JsonElement aspectElement))
        {
            string? parsedAspect = aspectElement.ValueKind == JsonValueKind.String
                ? aspectElement.GetString()
                : null;
            if (parsedAspect is not ("1:1" or "16:9" or "9:16" or "4:3" or "3:4"))
                return false;
            aspectRatio = parsedAspect;
        }

        bool generateAudio = true;
        if (input.TryGetProperty("generateAudio", out JsonElement audio))
        {
            if (audio.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;
            generateAudio = audio.GetBoolean();
        }

        int? seed = null;
        if (input.TryGetProperty("seed", out JsonElement seedElement))
        {
            if (seedElement.ValueKind != JsonValueKind.Number
                || !seedElement.TryGetInt32(out int seedValue)
                || seedValue < AiRequestLimits.MinSeed
                || seedValue > AiRequestLimits.MaxSeed)
                return false;
            seed = seedValue;
        }

        result = new VideoReplayInput(
            promptValue,
            durationSeconds,
            resolution,
            aspectRatio,
            generateAudio,
            seed);
        return true;
    }

    private readonly record struct VideoReplayInput(
        string Prompt,
        int DurationSeconds,
        string Resolution,
        string AspectRatio,
        bool GenerateAudio,
        int? Seed);
}

internal sealed class AiVideoJobRefreshHandler(IAiVideoService videos) : IAiJobRefreshHandler
{
    public async Task RefreshAsync(AiJob job, CancellationToken cancellationToken)
        => await videos.GetAsync(job.Id, cancellationToken);
}
