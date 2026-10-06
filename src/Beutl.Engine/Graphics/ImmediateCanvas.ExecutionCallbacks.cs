using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.Graphics;

public partial class ImmediateCanvas
{
    internal void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        _dispatcher?.VerifyAccess();
        if (_executionToken is not null && !_executionToken.IsActiveCanvas(this))
            throw new InvalidOperationException("The executor-managed callback canvas is no longer active.");
    }

    internal void ConfigureExecutionCallback(
        RenderExecutionSessionToken token,
        CallbackCanvasCapability capability)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (_executionToken is not null)
            throw new InvalidOperationException("The canvas already has an execution capability.");
        if (!token.IsActiveCanvas(this))
            throw new InvalidOperationException("The canvas must be active before a capability is attached.");

        _executionToken = token;
        _callbackCapability = capability;
    }

    /// <summary>
    /// Attaches the guarded draw capability to this canvas for the duration of a direct replay, then detaches
    /// it again.
    /// </summary>
    /// <remarks>
    /// A direct replay writes onto a canvas the executor keeps using afterwards, so the capability cannot be
    /// ended by <see cref="CloseWithoutFlush"/> the way an execution view's is. Transform and clip are left
    /// untouched: attaching the guard must not change a single pixel of what the replay draws.
    /// </remarks>
    internal DirectExecutionScope BeginDirectExecution(RenderExecutionSessionToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        VerifyAccess();
        int canvasSaveCount = Canvas.Save();
        var outer = new DirectExecutionScope(
            this,
            token,
            _executionToken,
            _callbackCapability,
            _callbackStateFloor,
            _isReplayingTargetScope,
            canvasSaveCount);
        try
        {
            token.EnterCanvas(this, facade: null);
            _executionToken = token;
            _callbackCapability = CallbackCanvasCapability.Draw;
            _callbackStateFloor = _states.Count;
            _isReplayingTargetScope = false;
            return outer;
        }
        catch
        {
            Canvas.RestoreToCount(canvasSaveCount);
            throw;
        }
    }

    private void EndDirectExecution(
        RenderExecutionSessionToken token,
        RenderExecutionSessionToken? outerToken,
        CallbackCanvasCapability? outerCapability,
        int outerStateFloor,
        bool outerIsReplayingTargetScope,
        int canvasSaveCount)
    {
        try
        {
            // The destination outlives the replay, so state the callback left pushed has to be unwound here;
            // an execution view gets the same treatment from CloseWithoutFlush.
            while (_states.Count > _callbackStateFloor && _states.TryPop(out CanvasPushedState? state))
                state.Pop(this);
        }
        finally
        {
            try
            {
                Canvas.RestoreToCount(canvasSaveCount);
            }
            finally
            {
                _executionToken = outerToken;
                _callbackCapability = outerCapability;
                _callbackStateFloor = outerStateFloor;
                _isReplayingTargetScope = outerIsReplayingTargetScope;
                token.ExitCanvas(this);
            }
        }
    }

    internal readonly struct DirectExecutionScope(
        ImmediateCanvas canvas,
        RenderExecutionSessionToken token,
        RenderExecutionSessionToken? outerToken,
        CallbackCanvasCapability? outerCapability,
        int outerStateFloor,
        bool outerIsReplayingTargetScope,
        int canvasSaveCount) : IDisposable
    {
        public void Dispose() => canvas.EndDirectExecution(
            token,
            outerToken,
            outerCapability,
            outerStateFloor,
            outerIsReplayingTargetScope,
            canvasSaveCount);
    }

    internal ImmediateCanvas CreateExecutionView()
    {
        VerifyAccess();
        if (_executionToken is not null && !_isReplayingTargetScope)
        {
            throw new InvalidOperationException(
                "An executor-managed callback canvas cannot create another execution view.");
        }

        return new ImmediateCanvas(this);
    }

    internal void ConfigureRawExecutionCallback(RenderExecutionSessionToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (_executionToken is not null)
            throw new InvalidOperationException("The canvas already has an execution capability.");
        if (!token.IsActiveCanvas(this))
            throw new InvalidOperationException("The canvas must be active before a capability is attached.");

        _executionToken = token;
        _callbackCapability = null;
        _callbackStateFloor = _states.Count;
    }

    internal void PinExecutionCallbackState()
    {
        VerifyAccess();
        if (_executionToken is null)
            throw new InvalidOperationException("Only an execution callback canvas can pin its base state.");

        _callbackStateFloor = _states.Count;
    }

    internal void CloseWithoutFlush()
    {
        if (IsDisposed)
            return;

        if (_dispatcher == null)
        {
            CloseCore(flush: false, submit: false);
        }
        else
        {
            _dispatcher.Invoke(() => CloseCore(flush: false, submit: false));
        }
    }

    internal void DrawExecutionInput(
        SKImage image,
        Rect destination,
        SKPaint? paint = null,
        SKSamplingOptions? sampling = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        VerifyPixelOperation();
        SKPaint effective = paint ?? _sharedFillPaint;
        if (paint is null)
            _sharedFillPaint.Reset();
        ApplyDirectBlendMode(effective);
        effective.IsAntialias = true;
        Canvas.DrawImage(
            image,
            SKRect.Create(image.Width, image.Height),
            destination.ToSKRect(),
            sampling ?? new SKSamplingOptions(SKCubicResampler.Mitchell),
            effective);
    }

    internal void DrawExecutionInputDeviceSpace(SKImage image, Point localDevicePoint)
    {
        ArgumentNullException.ThrowIfNull(image);
        VerifyPixelOperation();
        using (PushDeviceSpace())
        {
            _sharedFillPaint.Reset();
            ApplyDirectBlendMode(_sharedFillPaint);
            _sharedFillPaint.IsAntialias = true;
            Canvas.DrawImage(
                image,
                localDevicePoint.X,
                localDevicePoint.Y,
                new SKSamplingOptions(SKCubicResampler.Mitchell),
                _sharedFillPaint);
        }
    }

    /// <summary>
    /// Replays a target scope's recorded input, permitting nested render work for the replay's duration.
    /// </summary>
    internal void ReplayTargetScopeInput(Action<ImmediateCanvas> replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        VerifyAccess();
        if (_executionToken is null
            || (_callbackCapability is not null and not CallbackCanvasCapability.TargetScope)
            || _isReplayingTargetScope)
        {
            throw new InvalidOperationException("A target-scope replay is not active for this canvas.");
        }

        _isReplayingTargetScope = true;
        try
        {
            replay(this);
        }
        finally
        {
            _isReplayingTargetScope = false;
        }
    }

    private void VerifyPixelOperation(bool isClear = false)
    {
        VerifyAccess();
        switch (_callbackCapability)
        {
            case CallbackCanvasCapability.TargetScope when !_isReplayingTargetScope:
                throw new InvalidOperationException(
                    "A target-scope callback may only surround ReplayInput with transform and clip state.");
            case CallbackCanvasCapability.TargetCommandEmpty:
                throw new InvalidOperationException("An empty target command cannot perform pixel operations.");
            case CallbackCanvasCapability.TargetCommandRegion when isClear:
                throw new InvalidOperationException(
                    "The native clear operation is valid only for a full target command region.");
        }
    }

    private void VerifyHiddenLayerOperation()
    {
        if (_callbackCapability is not null && !_isReplayingTargetScope)
        {
            throw new InvalidOperationException(
                "SaveLayer-backed state is not available on a guarded callback canvas.");
        }
    }

    private void VerifyNestedExecutionOperation()
    {
        if (_callbackCapability is not null && !_isReplayingTargetScope)
        {
            throw new InvalidOperationException(
                "Nested render work, snapshots, and effectItem raw callbacks are not available on a guarded callback canvas.");
        }
    }

    private void VerifyNativeTargetOperation()
    {
        if (_callbackCapability is not null && !_isReplayingTargetScope)
        {
            throw new InvalidOperationException(
                "Raw surfaces and render targets are not available on a guarded callback canvas.");
        }
    }

    private void VerifyCallbackResource(object? resource, string parameterName)
    {
        if (resource is null
            || _executionToken is null
            || _callbackCapability is null
            || _isReplayingTargetScope)
            return;

        if (!_executionToken.IsResourceAuthorized(resource))
        {
            throw new InvalidOperationException(
                $"The resource passed as '{parameterName}' is not authorized in the active execution scope.");
        }
    }
}
