# Beutl アーキテクチャ再設計提案

## 現状の分析

### 良い点
- **明確なレイヤー分離**: Foundation → Engine → Editor → UI の依存方向が整理されている
- **循環依存なし**: プロジェクト間の依存グラフが非循環
- **リアクティブパターン**: Rx.NET / ReactiveUI の活用によるレスポンシブなUI
- **拡張性**: `Beutl.Extensibility` によるプラグインシステム
- **クロスプラットフォーム**: Avalonia + .NET による Win/Linux/macOS 対応

### 課題

| カテゴリ | 問題 | 影響 |
|---------|------|------|
| 巨大ViewModel | `TimelineTabViewModel` (1168行), `EditViewModel` (983行), `PlayerViewModel` (903行) | 保守性・テスタビリティの低下 |
| シングルトン | `GlobalConfiguration.Instance` がstaticフィールドで直接参照される | テスト困難、暗黙の依存 |
| DIコンテナ不在 | `Microsoft.Extensions.DependencyInjection` を使わず、手動で `IServiceProvider` を各所に実装 | サービスの発見・差し替えが困難 |
| プロパティシステムの二重化 | `CoreProperty` (シリアライゼーション用) と `IProperty<T>` (アニメーション/レンダリング用) が並存 | 学習コスト・ブリッジ層の複雑さ |
| Engine内の責務過多 | `Beutl.Engine` がグラフィックス、オーディオ、メディア、アニメーション、シリアライゼーション変換をすべて含む | ビルド時間増大、単独テスト困難 |
| UI code-behindの肥大化 | `TimelineTabView.axaml.cs` (741行), `ElementView.axaml.cs` (745行) | ViewとViewModelの境界が不明瞭 |
| テストカバレッジ | テストファイル106本 vs プロダクションコード186k行 | 回帰テスト不足 |
| Nullableの不統一 | プロジェクトごとにnullable有効/無効が混在 | null参照の潜在的リスク |

---

## 再設計案

### 1. レイヤー構造の再編

```
┌─────────────────────────────────────────────┐
│  Application Shell                          │
│  Beutl.App (Avalonia entry point のみ)       │
└──────────────┬──────────────────────────────┘
               │
┌──────────────▼──────────────────────────────┐
│  Presentation Layer                         │
│  ┌──────────────────┐ ┌──────────────────┐  │
│  │ Beutl.UI.Shell   │ │ Beutl.UI.Editor  │  │
│  │ (メインウィンドウ,  │ │ (タイムライン,     │  │
│  │  設定, ページ)     │ │  プロパティ,       │  │
│  │                  │ │  グラフエディタ)    │  │
│  └──────────────────┘ └──────────────────┘  │
│  ┌──────────────────┐                       │
│  │ Beutl.UI.Controls│                       │
│  │ (共通コントロール) │                       │
│  └──────────────────┘                       │
└──────────────┬──────────────────────────────┘
               │
┌──────────────▼──────────────────────────────┐
│  Application Layer                          │
│  ┌──────────────────┐ ┌──────────────────┐  │
│  │ Beutl.Editor     │ │ Beutl.Api        │  │
│  │ (編集操作,        │ │ (拡張配布,        │  │
│  │  Undo/Redo,      │ │  テレメトリ)      │  │
│  │  AutoSave)       │ │                  │  │
│  └──────────────────┘ └──────────────────┘  │
└──────────────┬──────────────────────────────┘
               │
┌──────────────▼──────────────────────────────┐
│  Domain Layer                               │
│  ┌──────────────────┐ ┌──────────────────┐  │
│  │ Beutl.Project    │ │ Beutl.Extensibi- │  │
│  │ (Scene, Element, │ │  lity            │  │
│  │  NodeTree)       │ │ (拡張インターフェ │  │
│  │                  │ │  ース)           │  │
│  └──────────────────┘ └──────────────────┘  │
└──────────────┬──────────────────────────────┘
               │
┌──────────────▼──────────────────────────────┐
│  Engine Layer (分割後)                       │
│  ┌────────────────┐ ┌────────────────────┐  │
│  │ Beutl.Graphics │ │ Beutl.Audio        │  │
│  │ (2D/3D描画,    │ │ (オーディオ処理,    │  │
│  │  SkiaSharp,    │ │  再生)             │  │
│  │  Rendering)    │ │                    │  │
│  └────────────────┘ └────────────────────┘  │
│  ┌────────────────┐ ┌────────────────────┐  │
│  │ Beutl.Media    │ │ Beutl.Animation    │  │
│  │ (デコード/エン  │ │ (キーフレーム,     │  │
│  │  コード,       │ │  イージング,       │  │
│  │  フォーマット)  │ │  Expression)       │  │
│  └────────────────┘ └────────────────────┘  │
└──────────────┬──────────────────────────────┘
               │
┌──────────────▼──────────────────────────────┐
│  Foundation Layer                            │
│  ┌────────────────┐ ┌────────────────────┐  │
│  │ Beutl.Core     │ │ Beutl.Abstractions │  │
│  │ (PropertySys,  │ │ (共通インターフェ   │  │
│  │  Serialization)│ │  ース, 型定義)      │  │
│  └────────────────┘ └────────────────────┘  │
│  ┌────────────────┐ ┌────────────────────┐  │
│  │ Beutl.Threading│ │ Beutl.Language     │  │
│  └────────────────┘ └────────────────────┘  │
└─────────────────────────────────────────────┘
```

**変更のポイント:**

- **`Beutl.Engine` の分割**: 現在の `Beutl.Engine` はグラフィックス、オーディオ、メディア処理、アニメーションが一体。これを `Beutl.Graphics`, `Beutl.Audio`, `Beutl.Media`, `Beutl.Animation` に分割する。各モジュールを独立してテスト・ビルドできるようにする。
- **`Beutl.Abstractions` の新設**: `Beutl.Core` に含まれるインターフェース群（`ICoreObject`, `ICoreSerializable` など）と、`Beutl.Extensibility` の基底インターフェースを一つにまとめる。実装のない純粋な契約のみを定義。
- **`Beutl.Configuration` の吸収**: 設定系は `Beutl.Core` または Application Layer に統合。独立プロジェクトとして存在する必然性が低い。

---

### 2. DI コンテナの導入

現状、`GlobalConfiguration.Instance` のようなstatic singletonや、各ViewModelが独自に `IServiceProvider` を実装するパターンが散在している。

**提案:**

```csharp
// Program.cs / App.axaml.cs
var services = new ServiceCollection();

// Foundation
services.AddSingleton<GlobalConfiguration>();
services.AddSingleton<ILocalizationService, LocalizationService>();

// Engine
services.AddSingleton<IGraphicsContext, SkiaGraphicsContext>();
services.AddSingleton<IAudioContext, PlatformAudioContext>();
services.AddTransient<IRenderPipeline, DefaultRenderPipeline>();

// Editor
services.AddScoped<IHistoryManager, HistoryManager>();
services.AddScoped<IAutoSaveService, AutoSaveService>();

// Extension
services.AddSingleton<IExtensionManager, ExtensionManager>();

var provider = services.BuildServiceProvider();
```

**メリット:**
- テスト時にモックへ差し替え可能
- 依存関係が明示的になる
- `GlobalConfiguration.Instance` のようなstaticアクセスを排除
- ViewModel のコンストラクタインジェクションが自然に使える

---

### 3. プロパティシステムの統合

現在 `CoreProperty`（データ永続化）と `IProperty<T>`（アニメーション/レンダリング）の二系統が存在し、`NodePropertyAdapter` がブリッジしている。

**提案: 統合プロパティシステム**

```csharp
public interface IBeutlProperty<T> : INotifyPropertyChanged
{
    // 基本値（シリアライゼーション対象）
    T BaseValue { get; set; }

    // 評価済み値（アニメーション/Expression適用後）
    T EvaluatedValue { get; }

    // アニメーション
    IAnimation<T>? Animation { get; set; }

    // Expression
    IExpression<T>? Expression { get; set; }

    // メタデータ
    PropertyMetadata Metadata { get; }

    // 時刻ベースの値評価
    T Evaluate(TimeSpan time, RenderContext? context = null);

    // バリデーション
    ValidationResult Validate(T value);
}
```

**メリット:**
- 二重のプロパティ登録が不要に
- Adapterパターンの複雑さを排除
- 一つのプロパティが「保存可能かつアニメーション可能」であることが自然に表現できる

**注意:** 破壊的変更のため、段階的な移行が必要。まず `IBeutlProperty<T>` ラッパーを作り、内部的に既存の二系統に委譲する。

---

### 4. ViewModel の分割とサービス抽出

巨大ViewModel を責務ごとに分割する。

```
TimelineTabViewModel (1168行)
├── TimelineSelectionService      (選択管理)
├── TimelineDragDropService       (ドラッグ&ドロップ)
├── TimelineZoomService           (ズーム/スクロール)
├── TimelineElementPlacement      (要素配置ロジック)
└── TimelineTabViewModel          (上記を組み合わせるオーケストレータ)

EditViewModel (983行)
├── SceneEditorService            (シーン管理)
├── PlaybackService               (再生制御)
├── EditorToolService             (ツール状態管理)
└── EditViewModel                 (組み合わせ)
```

**原則:**
- ViewModel は UI の状態管理とコマンドルーティングに徹する
- ビジネスロジックはサービスクラスに抽出
- 各サービスは DI で注入、単体テスト可能に

---

### 5. メディアバックエンドの抽象化改善

現在 FFmpeg / MediaFoundation / AVFoundation がそれぞれ独立プロジェクト。

**提案: `Beutl.Media.Abstractions` + Provider パターン**

```csharp
// Beutl.Media.Abstractions（新設）
public interface IMediaBackend
{
    IVideoDecoder CreateVideoDecoder(string path);
    IAudioDecoder CreateAudioDecoder(string path);
    IVideoEncoder CreateVideoEncoder(EncoderSettings settings);
    bool CanHandle(string format);
    int Priority { get; }  // 複数バックエンドの優先順位
}

// Beutl.Media (コアの管理ロジック)
public class MediaBackendManager
{
    private readonly IEnumerable<IMediaBackend> _backends;

    public IVideoDecoder GetDecoder(string path)
        => _backends
            .Where(b => b.CanHandle(Path.GetExtension(path)))
            .OrderByDescending(b => b.Priority)
            .First()
            .CreateVideoDecoder(path);
}
```

**メリット:**
- プラットフォーム固有の実装をランタイムで自動選択
- 新しいバックエンド追加時に既存コードを変更不要
- テスト時にダミーバックエンドを注入可能

---

### 6. コマンド/オペレーションシステムの強化

現在の `HistoryManager` + `OperationSequenceGenerator` は機能しているが、より宣言的なアプローチに移行可能。

**提案: Event Sourcing 風のコマンドシステム**

```csharp
public interface IEditCommand
{
    string Description { get; }
    void Execute(IEditContext context);
    void Undo(IEditContext context);

    // コマンドの合成
    IEditCommand Then(IEditCommand next);

    // コマンドのマージ（連続する同種操作をまとめる）
    bool TryMerge(IEditCommand other, out IEditCommand merged);
}

public interface IEditCommandBus
{
    void Execute(IEditCommand command);
    void Undo();
    void Redo();

    IObservable<IEditCommand> Executed { get; }
    IObservable<IEditCommand> Undone { get; }
}
```

**メリット:**
- コマンドのマージにより、プロパティ値の連続変更などを一つのUndo操作に集約
- `IObservable` による通知で、UIの更新やAutoSaveのトリガーを疎結合に
- テスト時にコマンドを再生してシナリオテストが可能

---

### 7. テスト戦略

現在のテストファイル数（106）はプロダクションコード規模（186k行）に対して不十分。

**提案:**

| レイヤー | テスト種別 | 優先度 |
|---------|-----------|--------|
| Core / PropertySystem | ユニットテスト | **最高** |
| Engine (Graphics/Audio) | ユニット + スナップショットテスト | 高 |
| ProjectSystem | シリアライゼーション往復テスト | 高 |
| Editor (Command/Undo) | インテグレーションテスト | 高 |
| ViewModel | ユニットテスト（DIモック使用） | 中 |
| UI | Avalonia Headless テスト | 低 |

**テスト容易性の確保:**
- DI導入によりサービスのモック化が容易に
- ViewModel の分割により、個別のサービスを独立テスト
- `Beutl.Testing.Helpers` プロジェクトを新設し、テスト共通基盤を整備

---

### 8. ビルドとモジュール境界

**提案: Directory.Build.props の強化**

```xml
<!-- 全プロジェクト共通 -->
<PropertyGroup>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>
```

- **Nullable を全プロジェクトで有効化** (現在は混在)
- **ArchUnit / NDepend 的な制約テスト**: レイヤー間の依存方向をCIで検証

---

## 移行戦略

一度にすべてを変更するのは非現実的。以下の段階で進める。

### Phase 1: 基盤整備（破壊的変更なし）
1. `Nullable` を全プロジェクトで有効化、警告を修正
2. `Microsoft.Extensions.DependencyInjection` を導入し、`App.axaml.cs` にルートコンテナを構築
3. `GlobalConfiguration.Instance` をDIに置き換え（旧コードはobsoleteマーク）
4. テストプロジェクトを拡充

### Phase 2: Engine の分割
1. `Beutl.Engine` から `Beutl.Animation` を分離
2. `Beutl.Engine` から `Beutl.Audio` を分離
3. `Beutl.Media.Abstractions` を新設、メディアバックエンド抽象化

### Phase 3: ViewModel / UI の改善
1. 巨大ViewModel からサービスを抽出
2. code-behind の UI ロジックを Behavior / Interaction に移動
3. コマンドシステムを強化

### Phase 4: プロパティシステム統合
1. `IBeutlProperty<T>` を設計・実装
2. 段階的に `CoreProperty` + `IProperty<T>` から移行
3. Adapter / ブリッジ層を削除

---

## まとめ

| 観点 | 現状 | 再設計後 |
|------|------|---------|
| DI | static singleton + 手動IServiceProvider | Microsoft.Extensions.DI ベース |
| Engine | モノリシック (1プロジェクト) | Graphics / Audio / Media / Animation に分割 |
| ViewModel | 巨大 (1000行超) | サービス抽出により200-300行 |
| Property | 二系統 + Adapter | 統合 `IBeutlProperty<T>` |
| メディア | 3つの独立実装 | 共通抽象 + Provider パターン |
| テスト | 106ファイル / 186k行 | レイヤーごとに体系的テスト |
| Nullable | プロジェクトごとに混在 | 全プロジェクト有効 |

この再設計はBeutlの既存の良いアーキテクチャを活かしつつ、テスタビリティ・保守性・モジュール性を段階的に改善するものです。
