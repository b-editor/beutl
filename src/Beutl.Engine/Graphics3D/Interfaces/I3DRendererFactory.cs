namespace Beutl.Graphics.Rendering;

/// <summary>
/// レンダリングバックエンドファクトリー
/// </summary>
public interface I3DRendererFactory
{
    /// <summary>
    /// サポートされているバックエンド名一覧
    /// </summary>
    IReadOnlyList<string> SupportedBackends { get; }

    /// <summary>
    /// 指定されたバックエンドが使用可能かチェック
    /// </summary>
    bool IsBackendAvailable(string backendName);

    /// <summary>
    /// レンダラーを作成
    /// </summary>
    I3DRenderer? CreateRenderer(string backendName);
}
