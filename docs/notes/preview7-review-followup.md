# Claude再レビューへの対応

対象: `fix/preview-7-review-findings`、再レビューの基準 `4d75dedd6`。初回110項目の記録は [初回検証](preview7-review-verification.md) を参照。ここでは追加指摘の再現、修正、反証、残課題を分ける。

## 修正と検証

| 項目 | 判定・対応 | コミット／検証 |
|---|---|---|
| V-P3 同一プロジェクト再オープン | 自動保存を無効にしてシーンの長さを変更すると、古い保存内容で置き換わることを再現。canonical pathで現在のプロジェクトと同一なら、現在の編集状態を維持する。別の壊れたプロジェクトを開く際の保護も維持。 | `4dfd9916e`。プロジェクト回帰と公開コンストラクタの計5件通過。 |
| V-N1 既存テストの型期待 | CloseProjectの非同期コマンド化に合わせ、公開プロパティの型期待を更新。 | `e23eebb1b`, `1b4baa545`。型期待に加えて、CloseProjectを実行した直後にGit履歴を読む3か所もawaitへ変更。同一プロセスの字幕・保存ワークフロー134件通過。 |
| A-N1 シーンミックスの正規化 | シーンミックスとソース経路が同じ正規化関数を通り、Endクランプ後の配列を利用する。 | `174dd0255`。シーンミックスの0.07秒を0.05秒へクランプする実行経路を検証。 |
| A-N1 受理不能な課金済み結果 | 通常の再試行は同じキーを使う。「受理できない結果を破棄」を明示的に選ぶと、その未受理チャンクのキーを退役させ、完了済みチャンクを保持する。次の実行で再課金される可能性を画面に表示する。 | `174dd0255`, `b7737bd16`。アカウント切替後の古い結果によるUI更新も抑制。同じキーで再試行→明示的破棄→新キーで成功を検証。AiDialogWorkflowTests 96件通過。 |
| S-P1 拒否済みnupkgの再利用 | ハッシュ不一致時は検証層でファイルを閉じて削除し、展開へ進ませない。空ファイルでも宣言ハッシュを検証する。失敗したインストールコンテキストも除去し、次の試行がダウンロードを飛ばさないようにした。 | `376bf0be4`, `d7073b47d`。通常／空アーカイブの拒否、削除、新しいコンテキストを検証。 |
| S-N4 生存版の復元 | ownerの版をNuGetVersionで比較し、`2.0`と`2.0.0`を同一視する。抽出ディレクトリがない版を復元先候補にしない。利用可能な生存版がなければ、削除対象版の所有payloadを除去する。 | `16d42987b`。パッケージ関連33件通過。 |
| E-N1 単一消費者の中間レイヤー | 入力ポートが複数の消費者へ接続される場合にだけ事前の共有用レイヤーを作る。単一消費者で余計なLayer fragmentが作られないことと、2分岐が成功することを検証。 | `bb737ca9e`。ノードグラフ38件通過。 |
| S-N5 アプリ終了時のキャンセル | 初回の「設計上の制約」という判定を訂正。共通PackageOperationHandlerで、呼び出し元トークンをアプリ寿命と連結する。Local/Remote両方からのインストール・更新に適用され、画面の単なる再読み込みとは区別される。 | `fc1628225`。CancellationToken.Noneで開始した実際のHTTPダウンロードが、アプリ終了でキャンセルされるテスト通過。 |
| 履歴取引の部分失敗 | 成功済みのApply/Revert状態を操作ごとに保持する。取引の再試行で成功済み操作を二重実行しない。操作内部で変更後に例外を投げた場合の原子性を保証するものではない。 | `cd0eda314`。非冪等な加減算を含むUndo/Redo両方向の再試行と往復を検証。HistoryManagerTests 121件通過。 |
| ヘッドレスの順序依存 | 通常のNUnitテストがDispatcherを先に取得してNullDispatcherImplを固定するのを防ぐため、assembly setupで共有ヘッドレスアプリを開始する。使用版[Avalonia 11.3.17のHeadlessUnitTestSession](https://github.com/AvaloniaUI/Avalonia/blob/11.3.17/src/Headless/Avalonia.Headless/HeadlessUnitTestSession.cs)とDispatcherのソースと照合した。 | `11aa1a0e5`。AiSubtitleAdvancedTestsとExternalReviewRegressionTestsの混在24件通過。 |
| R-P2 負のOffset | 通常の負Offsetは実際の縮小されたstroke pathとhitが一致する。境界はfillを含む保守的な範囲なので、縮小しないことだけでは不具合にならない。一方、Offsetで輪郭が完全に消滅した場合の誤hitは実在し、画像／動画の両方を修正。 | `fae6445e4`。Skiaが作る実stroke pathと比較。最終単体回帰221件に含む。 |

## 追加シナリオと反証

| 項目 | 現在の根拠 |
|---|---|
| R-N9 Opacity 50%のDrawableGroup内のSourceBackdrop | 100%時の画素一致に加えて、50%時は元の不透明シーンと全効果結果の線形合成を対照にして検証。両ケース通過。`4c9b239b3`。 |
| D-N3 FilterEffect配下のScene3D | 親scale 0.3/0.5/1.7、OutputScale 1.3、MaxWorkingScale 0.7で、通常経路とInvert配下の経路を実GPUで実行。6件通過。`ca44d07b0`。 |
| E-N2 変更直後の補助Preview | 主描画で実際にキャッシュがヒットするまでウォームアップした後、入力色を変更してPreviewを評価。新しい画素が直ちに出て、HasChangesも残る。Previewの既定CacheOptionsはDisabledであり、古いFrameキャッシュを使わない。`985000598`, `8370b886c`。 |
| M-N1 / R-N14 / P-N4 / E-P9 | 今回の取り下げを反映。初回の反証を覆す変更はない。 |
| P-N1 PrepareSaveをtry内へ移す案 | SaveRelocationコンストラクタは移動計画と一部の宛先ディレクトリを作るが、モデルのURI変更・ファイル移動はApply以降。現在のcatchはRollback後に再throwするため、PrepareSaveを移動してnullable rollbackに変えるだけでは、構築途中のディレクトリ作成もアクセス権問題も解消しない。通知先も同じ呼び出し元であり、根本のidentity解決を維持して下記Issueで扱う。 |

## 残課題

レビューの範囲を広げる変更はIssueにするという作業ルールに従い、以下は未修正として追跡する。これらをテスト通過や仕様上の制約として処理していない。

- [#2359](https://github.com/b-editor/beutl/issues/2359): R-P1のドローごとのUBO/descriptor pool割り当ては実在。GPU完了前の上書きを再導入せず、完了フェンスに基づく再利用が必要。定常時の割り当て数、共有材質画素、複数submission、resize/context変更を受入条件に追記した。D-N8のLinuxフォント列挙は未計測で、同Issueへ計測条件を追加。
- [#2360](https://github.com/b-editor/beutl/issues/2360): データパッケージ公開途中のプロセスクラッシュでは例外rollbackが走らない。startup sweepだけでbackupを削除せず、永続ジャーナルと再開／復元プロトコルが必要。
- [#2361](https://github.com/b-editor/beutl/issues/2361): lifetime.Exitからの同期CloseProject経路は残る。Exit直後にDispatcherが停止するため、単なるasync void化で保存の完了を保証できない。Exitより前のShutdownRequestedで非同期終了を待つ契約へ変更する。
- [#2357](https://github.com/b-editor/beutl/issues/2357): 列挙できない祖先下の保存。所有判定を弱めず、ネイティブidentityなどの代替経路が必要。
- [#2362](https://github.com/b-editor/beutl/issues/2362): 追加の対照実験で、SourceBackdrop自身のOpacityがRenderに適用されていない既存問題も確認した。今回通過したグループ側のOpacityとは別に追跡する。
- 初回からの [#2356](https://github.com/b-editor/beutl/issues/2356) と [#2358](https://github.com/b-editor/beutl/issues/2358) も引き続き未解決。

## 最終検証

コードの最終検証対象は `1b4baa545`。以後はこの記録のみを更新した。

- ヘッドレス全体: **998 passed / 0 failed / 0 skipped**。103フィクスチャを別プロセス（最大2並列）で完走。全テストのdiscovery一覧と全TRXのテスト名・重複数を照合し、漏れ・重複とも0を確認した。メモリ不足は発生していない。
- 同一プロセスの混在: **134 passed**（AiSubtitleAdvancedTests、AiDialogWorkflowTests、VersionControlSaveTests）。初回の全フィクスチャ実行では996 passed / 2 failedだったが、非同期CloseProjectを待っていなかったテストを直し、最終実行では全998件が通過した。
- 関連単体回帰: **221 passed**（履歴、パッケージ、ノードグラフ、画像／動画のstroke、PenHelper、Backdrop）。
- 実GPUの追加3D境界テスト: **6 passed**（Apple M3 / MoltenVK、フィルター有無と3種類の親scale）。
- アプリ本体ビルド: **net10.0 / net10.0-windowsとも成功、0 errors / 18 warnings**。
- Windows/Linux実機、Vulkan validation、実課金を伴うAI通信、リモートCIは実行していない。課金キーと受理不能結果の回復はHTTP mockで検証した。

初回のヘッドレス実行、修正後の混在実行、最終全体実行を分離して記録しており、失敗した途中実行を成功として加算していない。

ローカル証跡:
- 最終: `/tmp/beutl-rereview-headless-final/results.json`, `coverage-audit.json` とフィクスチャごとの `.trx` / `.log`
- 最終全体の実行スクリプト: `/tmp/beutl-rereview-headless-final.py`
- 初回: `/tmp/beutl-rereview-headless-shards/results.json` と各 `.trx` / `.log`
- `/tmp/beutl-rereview-final-headless-targeted.log`, `/tmp/beutl-rereview-final-unit.log`, `/tmp/beutl-rereview-final-build.log`
- `/tmp/beutl-rereview-reopen-red.log`, `/tmp/beutl-rereview-project-green.log`
- `/tmp/beutl-rereview-ai-workflows.log`, `/tmp/beutl-rereview-package-lifetime.log`
- `/tmp/beutl-rereview-history.log`, `/tmp/beutl-rereview-packages.log`
- `/tmp/beutl-rereview-preview-cache.log`, `/tmp/beutl-rereview-render-regressions.log`, `/tmp/beutl-rereview-3d-filter.log`
