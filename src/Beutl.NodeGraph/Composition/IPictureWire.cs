using Beutl.Graphics.Rendering;

namespace Beutl.NodeGraph.Composition;

/// <summary>A value on a wire that an input of pictures can also take.</summary>
/// <remarks>A wire's value is made anew at each evaluation, while a picture has to stay the same render node for its
/// changes to be tracked and what depends on it to stay cached.</remarks>
internal interface IPictureWire
{
    /// <summary>Brings <paramref name="picture"/> up to date with this value, making it on first use.</summary>
    /// <param name="owner">The node whose output carries this value, which explains a failure if it can.</param>
    /// <returns>Whether this value has a picture form at all.</returns>
    bool UpdatePicture(ref IWirePicture? picture, GraphNode owner, GraphCompositionContext context);
}

/// <summary>What <see cref="IPictureWire"/> keeps from one evaluation to the next.</summary>
internal interface IWirePicture : IDisposable
{
    RenderNode? RenderNode { get; }

    bool HasFailed { get; }
}
