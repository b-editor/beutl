using System.Collections;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Rejects a value that must not stand for a recorded operation's identity.
/// </summary>
/// <remarks>
/// <para>
/// Recording runs this once per node per frame, so every test here is a type pattern the JIT compiles to a
/// cast. Nothing reads a field, asks a type for its members, or otherwise reaches for reflection: a walk
/// like that costs the render path whatever the author's object graph happens to be, and no amount of
/// per-type memoization makes the first frame that meets a type free.
/// </para>
/// <para>
/// What reaches this is one object - the delegate's target - so what it decides is what that single object
/// is, never what it holds. A closure over anything besides <see langword="this"/> arrives as a compiler
/// display class, which is none of these types however many of them its fields hold, and has always been
/// accepted. So the list bites a callback that <em>is</em> one of these: a method group bound to one, or a
/// lambda inside one that reads nothing but its own instance.
/// </para>
/// </remarks>
internal static class RenderIdentityKeyValidator
{
    private const string IdentityRejection =
        "A value captured by a metadata callback must be a lightweight, immutable CPU value and cannot retain "
        + "a resource, context, request graph, mutable payload, or captured delegate.";

    public static void ThrowIfInvalid(object key, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(key, parameterName);

        // Disposability is the only ground this list had against a node, and a node holds no device
        // resource, opens no session, and reaches no part of the request graph. An answer of the node's that
        // moves after recording is caught by the recorded-answer cross-check instead. The exemption is
        // written into this clause alone, so a node that is also a mutable collection is still rejected.
        bool retainsLifetimeOrCapability = key is (IDisposable and not RenderNode)
            or RenderResource
            or RenderNodeContext
            or RenderRequest
            or RenderRequestOptions
            or RecordedRenderGraph
            or RecordedRenderGraphBuilder
            or RenderResourceSlot
            or RenderResourceRegistration
            or RenderFragmentHandle
            or RenderExecutionInput
            or RenderCallbackCanvas
            or OpaqueRenderSession
            or OpaqueRenderOutput
            or GeometrySession
            or ShaderExecutionContext
            or ShaderUniformWriter
            or ShaderResourceWriter
            or TargetScopeSession
            or TargetCommandSession
            or RawTargetScopeSession
            or RawTargetCommandSession;

        // A collection interface says the payload is mutable without asking the type anything: the
        // immutable collections implement the read-only interfaces instead.
        bool mutablePayload = key is Array or IList or IDictionary or ICollection;
        if (retainsLifetimeOrCapability || mutablePayload)
        {
            throw new ArgumentException(IdentityRejection, parameterName);
        }
    }
}
