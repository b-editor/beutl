using System.Collections;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Rejects a delegate target that must not stand for a recorded operation's identity. Type tests only, no
/// reflection; only the single target object is checked, not what it holds.
/// </summary>
internal static class RenderIdentityKeyValidator
{
    private const string IdentityRejection =
        "A value captured by a metadata callback must be a lightweight, immutable CPU value and cannot retain "
        + "a resource, context, request graph, mutable payload, or captured delegate.";

    public static void ThrowIfInvalid(object key, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(key, parameterName);

        // A node holds no device resource or session, and the recorded-answer cross-check catches an answer of
        // its that moves, so it is exempt here; a node that is also a mutable collection is still rejected below.
        bool retainsLifetimeOrCapability = key is (IDisposable and not RenderNode)
            or RenderResource
            or RenderNodeContext
            or RenderRequest
            or RenderRequestOptions
            or RecordedRenderGraph
            or RecordedRenderGraphBuilder
            or IRenderResourceSlot
            or RenderFragmentHandle
            or RenderExecutionInput
            or RenderCallbackCanvas
            or OpaqueRenderSession
            or OpaqueRenderOutput
            or ShaderExecutionContext
            or ShaderUniformWriter
            or ShaderResourceWriter
            or TargetScopeSession
            or TargetCommandSession
            or RawTargetScopeSession
            or RawTargetCommandSession;

        // A non-generic collection interface marks the payload as mutable without asking the type anything.
        // The immutable collections implement it too, so they are rejected as well.
        bool mutablePayload = key is ICollection;
        if (retainsLifetimeOrCapability || mutablePayload)
        {
            throw new ArgumentException(IdentityRejection, parameterName);
        }
    }
}
