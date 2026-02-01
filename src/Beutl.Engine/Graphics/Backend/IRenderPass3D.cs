using System;
using Beutl.Media;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for 3D render pass with MRT support.
/// </summary>
public interface IRenderPass3D : IDisposable
{
    /// <summary>
    /// Begins the render pass with multiple clear colors for MRT.
    /// </summary>
    /// <param name="framebuffer">The framebuffer to render to.</param>
    /// <param name="clearColors">Clear colors for each color attachment.</param>
    /// <param name="clearDepth">The depth value to clear the depth buffer with.</param>
    void Begin(IFramebuffer3D framebuffer, ReadOnlySpan<Color> clearColors, float clearDepth = 1.0f);

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
    void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0);

    /// <summary>
    /// Draws a fullscreen triangle for post-processing.
    /// </summary>
    void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0);

    /// <summary>
    /// Sets push constants for the currently bound pipeline.
    /// </summary>
    /// <typeparam name="T">The type of push constants data.</typeparam>
    /// <param name="data">The push constants data.</param>
    /// <param name="stageFlags">The shader stages that will access the push constants.</param>
    void SetPushConstants<T>(T data, ShaderStage stageFlags = ShaderStage.Vertex | ShaderStage.Fragment) where T : unmanaged;
}
