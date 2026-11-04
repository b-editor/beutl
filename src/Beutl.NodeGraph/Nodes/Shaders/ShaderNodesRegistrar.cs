using Beutl.Language;
using Beutl.Media;

namespace Beutl.NodeGraph.Nodes.Shaders;

internal static class ShaderNodesRegistrar
{
    internal static void RegisterAll()
    {
        GraphNodeRegistry.RegisterNodes(NodeGraphStrings.Shaders, Colors.MediumPurple)
            .Add<ShaderFullscreenPassNode>(NodeGraphStrings.ShaderFullscreenPassNode)
            .Add<ShaderComputePassNode>(NodeGraphStrings.ShaderComputePassNode)
            .Add<ShaderRenderPassNode>(NodeGraphStrings.ShaderRenderPassNode)
            .Add<ShaderExtendedRenderPassNode>(NodeGraphStrings.ShaderExtendedRenderPassNode)
            .Add<ShaderSourceNode>(NodeGraphStrings.ShaderSourceNode)
            .Add<ShaderPictureNode>(NodeGraphStrings.ShaderPictureNode)
            .Add<ShaderApplyNode>(NodeGraphStrings.ShaderApplyNode)
            .AddGroup(NodeGraphStrings.ShaderCategory_Resources, group => group
                .Add<ShaderImageNode>(NodeGraphStrings.ShaderImageNode)
                .Add<ShaderFixedImageNode>(NodeGraphStrings.ShaderFixedImageNode)
                .Add<ShaderImageViewNode>(NodeGraphStrings.ShaderImageViewNode)
                .Add<ShaderBufferNode>(NodeGraphStrings.ShaderBufferNode)
                .Add<ShaderDataNode>(NodeGraphStrings.ShaderDataNode)
                .Add<ShaderPackNode>(NodeGraphStrings.ShaderPackNode)
                .Register())
            .AddGroup(NodeGraphStrings.ShaderCategory_Operations, group => group
                .Add<ShaderClearImageNode>(NodeGraphStrings.ShaderClearImageNode)
                .Add<ShaderClearBufferNode>(NodeGraphStrings.ShaderClearBufferNode)
                .Add<ShaderCopyImageNode>(NodeGraphStrings.ShaderCopyImageNode)
                .Add<ShaderCopyBufferNode>(NodeGraphStrings.ShaderCopyBufferNode)
                .Add<ShaderGenerateMipmapsNode>(NodeGraphStrings.ShaderGenerateMipmapsNode)
                .Add<ShaderConvertColorNode>(NodeGraphStrings.ShaderConvertColorNode)
                .Register())
            .AddGroup(NodeGraphStrings.ShaderCategory_Loops, group => group
                .Add<ShaderRepeatNode>(NodeGraphStrings.ShaderRepeatNode)
                .Add<ShaderReplayNode>(NodeGraphStrings.ShaderReplayNode)
                .Add<ShaderLoopInfoNode>(NodeGraphStrings.ShaderLoopInfoNode)
                .Add<ShaderLoopInput>(NodeGraphStrings.ShaderLoopInput)
                .Add<ShaderLoopOutput>(NodeGraphStrings.ShaderLoopOutput)
                .Register())
            .AddGroup(NodeGraphStrings.ShaderCategory_Inputs, group => group
                .Add<ShaderSamplerNode>(NodeGraphStrings.ShaderSamplerNode)
                .Add<ShaderInputRegionNode>(NodeGraphStrings.ShaderInputRegionNode)
                .Add<ShaderImageParametersNode>(NodeGraphStrings.ShaderImageParametersNode)
                .Add<ShaderVectorNode>(NodeGraphStrings.ShaderVectorNode)
                .Add<ShaderColorNode>(NodeGraphStrings.ShaderColorNode)
                .Register())
            .AddGroup(NodeGraphStrings.ShaderCategory_PassSettings, group => group
                .Add<ShaderBlendNode>(NodeGraphStrings.ShaderBlendNode)
                .Add<ShaderDrawNode>(NodeGraphStrings.ShaderDrawNode)
                .Add<ShaderDispatchNode>(NodeGraphStrings.ShaderDispatchNode)
                .Add<ShaderRasterizerNode>(NodeGraphStrings.ShaderRasterizerNode)
                .Add<ShaderDepthNode>(NodeGraphStrings.ShaderDepthNode)
                .Add<ShaderStencilNode>(NodeGraphStrings.ShaderStencilNode)
                .Add<ShaderVertexBufferNode>(NodeGraphStrings.ShaderVertexBufferNode)
                .Add<ShaderVertexAttributeNode>(NodeGraphStrings.ShaderVertexAttributeNode)
                .Add<ShaderIndexBufferNode>(NodeGraphStrings.ShaderIndexBufferNode)
                .Add<ShaderLoadNode>(NodeGraphStrings.ShaderLoadNode)
                .Add<ShaderPartialRenderingNode>(NodeGraphStrings.ShaderPartialRenderingNode)
                .Register())
            .Register();
    }
}
