using System;
using Beutl.Media;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for 3D render pass abstraction.
/// </summary>
public interface IRenderPass3D : IDisposable
{
    /// <summary>
    /// Begins the render pass.
    /// </summary>
    /// <param name="framebuffer">The framebuffer to render to.</param>
    /// <param name="clearColor">The color to clear the framebuffer with.</param>
    /// <param name="clearDepth">The depth value to clear the depth buffer with.</param>
    void Begin(IFramebuffer3D framebuffer, Color clearColor, float clearDepth = 1.0f);

    /// <summary>
    /// Binds a pipeline for rendering.
    /// </summary>
    /// <param name="pipeline">The pipeline to bind.</param>
    void BindPipeline(IPipeline3D pipeline);

    /// <summary>
    /// Ends the render pass.
    /// </summary>
    void End();

    /// <summary>
    /// Binds a vertex buffer for rendering.
    /// </summary>
    /// <param name="buffer">The vertex buffer to bind.</param>
    void BindVertexBuffer(IBuffer buffer);

    /// <summary>
    /// Binds an index buffer for rendering.
    /// </summary>
    /// <param name="buffer">The index buffer to bind.</param>
    void BindIndexBuffer(IBuffer buffer);

    /// <summary>
    /// Binds a descriptor set for rendering.
    /// </summary>
    /// <param name="pipeline">The pipeline to bind the descriptor set to.</param>
    /// <param name="descriptorSet">The descriptor set to bind.</param>
    void BindDescriptorSet(IPipeline3D pipeline, IDescriptorSet descriptorSet);

    /// <summary>
    /// Draws indexed primitives.
    /// </summary>
    /// <param name="indexCount">The number of indices to draw.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="firstIndex">The first index to draw.</param>
    /// <param name="vertexOffset">The vertex offset to add to each index.</param>
    /// <param name="firstInstance">The first instance to draw.</param>
    void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0);
}
