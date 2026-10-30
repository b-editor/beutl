namespace Beutl.Graphics.Rendering.Requests;

internal interface IRenderRequestRecordingHost
{
    RenderRequest Request { get; }

    bool IsRenderCacheEnabled { get; }

    IReadOnlyList<RenderFragmentReference> RecordNode(
        NodeRecordingTransaction parent,
        RenderNode node,
        IReadOnlyList<RenderFragmentReference> inputs,
        bool subtree);

    IReadOnlyList<RenderFragmentReference> RecordSharedSubtree(
        NodeRecordingTransaction parent, object identity, RenderNode root);

    RecordedNestedRenderRequest RecordNestedRequest(
        RenderNode root,
        RenderRequestOptions options);

    void Commit(in NodeRecordingCommit commit);
}
