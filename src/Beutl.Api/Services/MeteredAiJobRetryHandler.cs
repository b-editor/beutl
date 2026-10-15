using System.Collections.Immutable;
using System.Text.Json;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Api.Services;

internal abstract class MeteredAiJobRetryHandler(
    AiOperationId operation,
    IAiEntitlementService entitlementService,
    IAiOperationAvailabilityService availabilityService,
    IAiModelCatalogService modelCatalogService,
    AiRetryAttemptContext retryContext) : IAiJobRetryHandler
{
    // The store consumes a confirmation once, so a second run of the same attempt is rejected.
    protected async Task RunRetryAsync(
        AiJob job,
        CancellationToken cancellationToken,
        Func<string, bool, Task> operation,
        AiRetryAttempt attempt)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AiAuthenticatedRequestIdentity authenticated = retryContext.GetRequiredIdentity();
        if (!StringComparer.Ordinal.Equals(authenticated.AccountId, attempt.AccountId))
            throw new AiRetryAttemptRejectedException();
        Task retry = ExecuteRetryAsync(job, authenticated, attempt, operation);
        try
        {
            // Cancellation only stops this caller waiting. The paid operation is
            // deliberately executed with CancellationToken.None.
            await retry.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller may stop waiting, but the operation still owns the paid request
            // and extension resources. Keep this task alive until it drains so the
            // preparation lease cannot be released underneath it.
            try
            {
                await retry.ConfigureAwait(false);
            }
            catch
            {
                // Preserve the caller's cancellation outcome after the underlying
                // operation has been observed and allowed to release its resources.
            }

            throw;
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
        long generation;
        if (!retryContext.Store.TryConsumeAttempt(
                attempt,
                job,
                authenticated.AccountId,
                out key,
                out isRepeat))
        {
            throw new AiRetryAttemptRejectedException();
        }

        if (attempt.Kind == AiRetryAttemptKind.Recovery)
            generation = attempt.Generation;
        else
            generation = attempt.Generation + 1;

        using IDisposable authenticatedScope = retryContext.Enter(authenticated);
        try
        {
            await operation(key, isRepeat);
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
        try
        {
            retryContext.Store.TryRetire(job, authenticated.AccountId, key, generation, attempt.Token);
        }
        catch (AiRetryStoreUnavailableException ex)
        {
            try { retryContext.Store.TryRelease(job, authenticated.AccountId, key, generation, attempt.Token); }
            catch (AiRetryStoreUnavailableException) { }
            Log.CreateLogger(typeof(BuiltInAiJobKinds)).LogWarning(ex, "The AI retry succeeded, but its recovery key could not be retired.");
        }
    }

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
        => status.IsTerminal
            && status.Outcome is { } outcome
            && (outcome == AiJobOutcomes.Failed || outcome == AiJobOutcomes.Succeeded)
            && job.CanRetry
            && HasCanonicalPrompt(job);

    private static bool HasCanonicalPrompt(AiJob job)
    {
        if (job.InputParameters is not { ValueKind: JsonValueKind.Object } input
            || !input.TryGetProperty("prompt", out JsonElement prompt)
            || prompt.ValueKind != JsonValueKind.String)
            return false;
        return AiReplayInputValidation.IsCanonicalPrompt(prompt.GetString());
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
        catch (Exception ex) when (ex is AiRetryAttemptRejectedException or AiRetryStoreUnavailableException)
        {
            throw ToPreparationException(ex);
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
        catch (Exception ex) when (ex is AiRetryAttemptRejectedException or AiRetryStoreUnavailableException)
        {
            throw ToPreparationException(ex);
        }
    }

    // Store refusals surface to callers as the public preparation exceptions.
    private static AiException ToPreparationException(Exception exception)
        => exception is AiRetryAttemptRejectedException
            ? new AiJobRetryPreparationRejectedException(exception)
            : new AiJobRetryPreparationUnavailableException(exception);

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
        else
        {
            AiModelCatalog catalog = await modelCatalogService.GetAsync(cancellationToken);
            if (!IsModelStillOffered(catalog, job))
            {
                // The balance is not the problem, so saying it is would send the
                // user to buy credits that would not help.
                result = new AiJobRetryPreflight(true, false, Strings.AiModelUnavailable);
            }
            else if (!IsCurrentRequestSupported(catalog, job))
            {
                result = new AiJobRetryPreflight(
                    true,
                    false,
                    Strings.AiModelDoesNotSupportRequest);
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

    /// <summary>
    /// Checks the exact retained request against the latest model capability
    /// snapshot. Implementations remain permissive when the snapshot has no
    /// assertion for this request (for example, an older server that publishes
    /// no model data).
    /// </summary>
    protected virtual bool IsCurrentRequestSupported(AiModelCatalog catalog, AiJob job)
        => true;

    // A rerun repeats the model the job ran on, so a model that has since been
    // withdrawn cannot be repeated. Falling back to the operation's default
    // would quietly produce something else and charge the default's price for
    // it; the server refuses this too.
    private bool IsModelStillOffered(AiModelCatalog catalog, AiJob job)
    {
        if (job.Model is not { Value.Length: > 0 } model)
            return !catalog.OffersNoModel(operation);

        ImmutableArray<AiModelOption> models = catalog.ModelsFor(operation);
        // A catalog that could not be fetched says nothing about any model, and
        // the server has the last word regardless.
        if (models.IsDefaultOrEmpty)
            return !catalog.OffersNoModel(operation);

        return models.Any(option => option.Id == model);
    }

    protected AiModelOption? ResolveModel(AiModelCatalog catalog, AiJob job)
    {
        ImmutableArray<AiModelOption> models = catalog.ModelsFor(operation);
        if (models.IsDefaultOrEmpty)
            return null;
        if (job.Model is { Value.Length: > 0 } model)
            return models.FirstOrDefault(option => option.Id == model);
        return catalog.DefaultFor(operation);
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
                    await owner.RunRetryAsync(
                        job,
                        cancellationToken,
                        (key, isRepeat) => owner.DispatchAsync(job, key, isRepeat),
                        attempt);
                }
                catch (Exception ex) when (ex is AiRetryAttemptRejectedException or AiRetryStoreUnavailableException)
                {
                    throw ToPreparationException(ex);
                }
            }
            finally
            {
                // Dispose does nothing once the store consumed the confirmation; a pre-consume cancellation abandons
                // it. After an ambiguous response the store, not this attempt, keeps the exact key.
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
