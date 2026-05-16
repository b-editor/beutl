using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

namespace Beutl.Editor;

/// <summary>
/// シーン編集器が公開する強い型付けのコンテキスト契約。タイムライン / プロパティ /
/// グラフエディタなどの 1st-party scene tab はこのインターフェース実装を要求する。
/// <see cref="IEditorContext"/> のみを実装する拡張機能 (例: 独自テキストエディタ)
/// は scene tab を開けない — これは意図的なゲートで、必要なサービス
/// (Scene、HistoryManager 等) を提供できない context が runtime で
/// <c>GetRequiredService</c> 例外を出すのを防ぐ。
/// </summary>
/// <remarks>
/// 全プロパティはコンストラクタで初期化され、authoritative な teardown
/// (<see cref="System.IAsyncDisposable.DisposeAsync"/>) が完了するまで non-null。
/// 破棄後の参照動作は未定義。<see cref="IEditorContext"/> のデフォルト
/// <see cref="System.IDisposable.Dispose"/> 実装は no-op のため、同期破棄は実際の
/// クリーンアップを行わない点に注意。
/// </remarks>
public interface ISceneEditorContext : IEditorContext
{
    /// <summary>編集対象のシーン。</summary>
    Scene Scene { get; }

    /// <summary>undo/redo を管理する履歴マネージャ。</summary>
    HistoryManager HistoryManager { get; }

    /// <summary>再生位置 / 最大時間を保持するクロック。</summary>
    IEditorClock Clock { get; }

    /// <summary>タイムラインで選択中のオブジェクト。</summary>
    IEditorSelection Selection { get; }

    /// <summary>プレビュー用プレイヤ。Dispose 後は <c>AfterRendered</c> 等にアクセスしない。</summary>
    IPreviewPlayer Player { get; }

    /// <summary>新規エレメント追加の入口。</summary>
    IElementAdder ElementAdder { get; }

    /// <summary>フレームキャッシュの状態。</summary>
    IBufferStatus BufferStatus { get; }

    /// <summary>タイムライン表示オプション (スケール / オフセット / レイヤ数 等) のプロバイダ。</summary>
    ITimelineOptionsProvider TimelineOptions { get; }
}
