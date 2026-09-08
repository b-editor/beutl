## Beutlへの貢献についてのガイドライン

貢献に興味を持っていただきありがとうございます。このガイドでは、ローカルでのビルド方法、テストとフォーマットの手順、そして従っている規約について説明します。

### 前提条件

- **.NET SDK** — [`global.json`](global.json) で `10.0.100` 以上に固定されています（`rollForward: latestFeature`）。CI と同じバージョンが解決されるよう、対応する SDK をインストールしてください。
- 機能ブランチをプッシュできる **Git**。
- FFmpeg はビルドの前提条件では**ありません**。`Beutl.FFmpegWorker` という別プロセスとして IPC 経由で動作し、実行時にインストールされます。

Beutl は `net10.0` と `net10.0-windows` をデュアルターゲットにしています。Windows 専用ターゲットは Windows でビルドされます。Linux/macOS で単一フレームワークが必要な場合は `-f net10.0` を使ってください。

### ビルド / テスト / フォーマット

```bash
dotnet build Beutl.slnx                                            # ビルド
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings  # テスト
dotnet format Beutl.slnx                                           # フォーマット
./build.sh <Target>                                                # Nuke (CI と同等)
```

`dotnet format` は CI ([Format check](.github/workflows/format-check.yml)) で強制されるため、プッシュ前に実行してください。

### 実行 / デバッグ

アプリのエントリーポイントは `src/Beutl` プロジェクトです。`dotnet run --project src/Beutl` で実行する（または IDE でスタートアッププロジェクトに設定する）ことができます。

### Pull request

既に作業中である可能性もあるので、PRを送る前にIssueを開くことをおすすめします。変更内容が少ない場合はIssueを開かなくても良いです。

履歴を見やすくするために**リベースと強制プッシュを忘れないで下さい。**

PRテンプレートでは概要・影響範囲・テスト計画・破壊的変更の記入を求めています。CI とレビュアーが強制するルールは以下の通りです。

- **新しいロジックには NUnit テストを付ける** — `tests/` 配下（例: `tests/Beutl.UnitTests/`、`tests/SourceGeneratorTest/`）。
- **新しい XAML はコンパイル済みバインディングを使う**（`x:CompileBindings="True"` + `x:DataType`）。
- **GPL/MIT の境界を越えない** — MIT プロジェクトは `Beutl.FFmpegWorker` への `ProjectReference` を持たず、IPC 経由でのみアクセスします。

ビルド、テスト、カバレッジ、アーキテクチャに関する要件は、
[開発品質ゲート](docs/development/quality-gates.md)にまとめています。

### 公開 API の設計

明確に優れた設計がある場合、扱いにくい API の互換性維持を既定の選択にはしません。
公開抽象は直交性を保ち、現在のアプリケーションが想定していない用途にもプラグイン作者が
対応できるよう、インターフェイス、仮想フック、合成可能なプリミティブを優先してください。

既存の呼び出し元を更新しないためだけに、`[Obsolete]` shim、重複した `V2` 型、
legacy パラメーター、互換ラッパーを追加しないでください。ツリー内の呼び出し元は同じ変更で
更新します。公開済みの拡張契約に廃止猶予を設ける場合は、メンテナーが明示的に選択し、
PR に削除時期を記載する必要があります。判断が自明でない場合は、互換性維持または大規模な
書き換えを黙って選ばず、PR でトレードオフを説明してください。

### 変更範囲とブランチの安全性

変更範囲に含まれる欠陥や回帰は同じ変更内で完了してください。別の機能に属する作業、または
メンテナーだけが提供できる情報によってブロックされている作業は、未説明の TODO として残さず、
その境界を明記してください。

`.github/workflows/` 配下の既存ファイルは、メンテナーの明示的な承認なしに変更しないでください。
`main` または `master` を force-push せず、feature branch を使用してください。

### コミットメッセージ

[Conventional Commits](https://www.conventionalcommits.org/) に従います。

- `fix:` — バグ修正
- `feat:` — 新機能
- `refactor:` — 挙動を変えないリファクタリング
- `docs:` — ドキュメント

破壊的変更は `feat!:` / `refactor!:` のサブジェクトと、移行方法を記した `BREAKING CHANGE:` フッターを使います。

### コードガイドライン

[.NETのコードスタイル](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md)を使います。

**UIの実装**
- UserControlのイベントハンドラが複雑になる場合は、Behaviorに分けるか、
  `partial`でファイルを分割してください。

XAMLファイル
- インデントは4つのスペースにして下さい。
- コントロールにプロパティを追加する場合、
  最初のプロパティは項目と同じ行に配置し、
  残りのすべてのプロパティは最初のプロパティに合わせて別の行に配置します。
- `Binding`を使う場合、[コンパイル済みのバインディング](https://docs.avaloniaui.net/docs/next/basics/data/data-binding/compiled-bindings)を使用して下さい。
```xaml
<UserControl x:CompileBindings="True"
             x:DataType="viewModel:MyViewModel">
    <TextBox Foreground="White"
             MaxWidth="240"
             Text="{Binding Text.Value}" />
</UserControl>
```

カスタム Drawable、フィルター効果、ブラシ、シェーダーを実装する場合は、
[解像度非依存レンダリングのガイド](docs/extension-authoring/resolution-independent-rendering.md)も参照してください。
ドッキング可能なエディターツールを追加する場合は、
[ツールタブ拡張ガイド](docs/extension-authoring/tool-tabs.md)を参照してください。

[プロジェクト構成ガイド](docs/development/project-structure.md)には、各モジュールの責務と依存境界をまとめています。
