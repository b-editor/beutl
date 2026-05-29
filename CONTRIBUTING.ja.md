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

### 各モジュールについて

モジュール境界マップと詳細なコントリビューションルールは [`AGENTS.md`](AGENTS.md) を、AI 支援ワークフローのドキュメントは [`docs/ai-workflow/`](docs/ai-workflow/README.md) を参照してください。
