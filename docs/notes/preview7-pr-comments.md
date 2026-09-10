# PR差分コメント22件への対応

対象ブランチ: `fix/preview-7-review-findings`。今回の基準: `f4556097c`。
コメント本文と提案コードはそのまま採用せず、現在の呼び出し経路と回帰テストで確認した。重複する指摘は同じ修正へまとめた。PRへの返信・リアクションは行っていない。

| コメント | 対応 | コミット |
|---|---|---|
| 1 | 取引単位の実行済みフラグだけでは不十分だった。移動をコピー上で事前検証し、不正インデックスでは実コレクションを変更しない。例外時は変更前／完了後の状態を照合し、通知中の例外でも完了済み移動を再実行しない。状態不明の操作は履歴を保持して再実行・他の履歴制御を停止する。 | `0acdabae6`, `bd5e31a60` |
| 2 / 4 / 9 | マーカー未作成と既存の不正マーカーを区別。JSON破損、null、欠落したName、無効なVersion、読めないファイルは所有権を与えない。旧形式の登録メタデータへのフォールバックは実際に未作成の場合だけ。 | `d40400c04` |
| 3 | globの一致結果をcanonical pathでシーンディレクトリ内か確認してから、子の削除／復元へ進む。`..`とsymlinkによる脱出を拒否し、外部ファイルを保持する。 | `cb7a593db` |
| 5 / 16 / 22 | コピーをキャンセル可能な準備段階へ分離。メインアプリとPackageToolsの両方で、トークン付きUIディスパッチ後に短い公開・登録処理を実行する。UI待機中のキャンセルは、コールバックが実行されなくても完了し、公開・登録しない。 | `3d47d1ccd` |
| 6 | 開いている全ルートについて、既存のSerializedGraphTraversalでファイル付きCoreObjectの子孫も調べる。ネストしたbrush sidecarの名前変更・削除を阻止する。 | `e14524078` |
| 7 / 14 | 待機通知をキューのキャンセル状態と同じロックで予約し、待機／キャンセル通知をジョブ単位で順番に配送する。別ジョブのキャンセル購読者が停止していても、その後のジョブが待機通知を追い越して成功しない。購読者はロック外で呼ぶため、別スレッドからの再入キャンセルもデッドロックしない。 | `4a66d6545`, `98a304034` |
| 8 | 「生存版なし」と「生存版への修復失敗」を混同するOR式を撤去。失敗時は後続の無条件削除へ進まない。修復例外をパッケージ単位で扱い、失敗した抽出先と登録を再試行用に残し、他パッケージを処理する。 | `fd030c57b` |
| 10 | サイドカーへの書き込みは親の埋め込み設定を継承せず、WriteとSaveReferencedObjectsを使用。CoreSerializableJsonConverterの同じ経路にも適用。原子的保存は維持。 | `314dab260` |
| 11 | splitのrollback後もscene.Childrenに残る要素のサイドカーを削除しない。canonical比較が失敗した場合もファイルを保持し、元の失敗と合わせて報告する。 | `8933476c9` |
| 12 | 基底型のコンストラクタも購読コールバックの解析対象に含める。基底側で既に診断した状態／コールバックは派生側で重複報告しない。 | `9811b7d7c` |
| 13 | RunsNestedFunctionで、呼ばれないローカル関数内の購読を除外。呼ばれる場合の検出は維持。 | `7c7e0cd0d` |
| 15 | キャンセル・ライブラリ欠落も含め、全起動例外でCleanupする。この2種類を除外するのは一般的な再試行cooldownだけ。実際の短命プロセスと接続待ちキャンセルで、process/log pumpの解放を検証。 | `db6cca4ba` |
| 17 | 復元済みプロファイルと未提供プロファイルの順序を記録。未提供エントリを末尾へ移動させず、再び利用可能になったときの先頭プロファイルを保持する。 | `f72d5247f` |
| 18 | 警告にactivation revisionを付け、ディスパッチ後、実際の表示直前に同じ状態ロックで再確認する。後続activationへの切替後は抑止し、自身のcleanup後でも同じrevisionの警告は表示する。 | `3cb6eb1aa` |
| 19 | クライアントによる拒否は専用例外で識別し、専用バナー／破棄操作を表示する。一般的なAiUnexpectedErrorを重ねて出さない。他のInvalidDataExceptionを誤って隠さない。 | `973858e18` |
| 20 | 有限かつ正のチャンク長を検証した後、空のsegmentsを有効な無音結果として受理。無音→音声の2チャンクが両方完了することを検証。ローカルbeutl-webのaudio-validationとも一致。 | `c5b4732d1` |
| 21 | メインアプリの即時インストール2経路ともHashVerifiedを必須にする。ハッシュ未提供のアーカイブは展開・ロードへ進まない。PackageToolsの明示的なローカル選択／確認という既存経路とは区別する。 | `25135a8aa` |

## 履歴の失敗契約

`ChangeOperation.FailureState` は最後の失敗が `Unchanged`（状態不変）、`Completed`（処理は完了したが通知などが失敗）、`Unknown` のどれかを示す。Unknownを勝手に再実行しない。`CustomOperation.FailureIsAtomic` はApply/Revertの両デリゲートが例外時に状態を変えないと保証できる場合だけ設定する。

移動の照合では参照型はidentityを使う。値型の粗いEqualsや符号付きゼロが変更を隠さないよう、参照を含まない値型はバイト列を比較し、参照を含む値型は保守的にUnknownとする。判別不能な失敗は自動復旧を意味しない。現在のモデルを確認して履歴をクリアするか、プロジェクトを閉じて開き直す必要がある。

## パッケージのキャンセル境界

準備段階では一時ディレクトリへのコピーを中断でき、まだ公開データを変えない。UI実行待ちにもキャンセルトークンを渡す。公開開始の直前を最後のキャンセル受付点とし、開始後は短い同期の公開・登録を完了させる。そこで追加のキャンセル例外を投げて「新しいデータだけ公開、登録は旧状態」という結果にしない。古い大きなバックアップの削除はUI外で行う。

ファイル公開時の例外はrenameによるrollbackを行う。登録／拡張ロード自体に失敗した場合は旧バックアップを保持する。プロセスクラッシュを含む永続的な復旧ジャーナルは、既存の[#2360](https://github.com/b-editor/beutl/issues/2360)の範囲であり、この修正で解決したとは扱わない。

## 検証

本番コードの最終コミットは `98a304034`。以後の変更は検証記録のみ。

- Headless: **1,005ケースの最終結果を確認、漏れ・重複0**。105フィクスチャをプロセスごとに分離。`f88f370fe` に対する全件実行は1,003 passed / 2 timeoutだった。ビルド・整形と重なった実行であり、その記録を残している。
- `98a304034` 後に再ビルドし、ProxyMediaServicesReinitTestsの10件を通過。タイムアウトしたVersionControlRestoreTestsは他のビルド・テストと重ねず全131件を再実行し、**131 passed / 0 failed**（12分25秒）。この最終フィクスチャ結果を全体のdiscovery一覧と照合した。
- 途中の混在再実行（Proxy10件＋失敗した2件）は11 passed / 1 timeout。タイムアウトした終了処理テストは単独でも通過し、2件だけの連続実行も通過した。したがって途中実行を「全件成功」とは扱わない。採取スタックにFSEventsのファイル監視からcanonical pathのディレクトリ列挙へ入る高負荷を確認したが、タイムアウトの原因を断定する根拠にはしていない。

- エディタ領域と関連コアの広域単体: 1,938 passed / 1 skipped（ファイルシステムの大文字・小文字区別条件）。
- SourceGeneratorTest全体: 328 passed。
- ソリューション全体ビルド: 0 errors / 22 warnings。
- プロキシ通知とJSON serialization: 68件。最終のジョブ別通知修正後、ProxyJobQueueTests全59件を再ビルドして通過。追加した複数ジョブ競合テストは修正前にSucceeded→WaitingForAdmissionの逆順を再現し、修正後に通過。
- 出力順序、警告revision、即時ハッシュ必須、アプリ寿命: 5件。
- UI待機中キャンセル・アプリ寿命・即時ハッシュ必須: 4 passed。

フォーマットは[失敗したCIジョブ](https://github.com/b-editor/beutl/actions/runs/34424949512/job/102708095381)のログを確認した。原因は10ファイルのusing順序・空白・改行。CIと同じ `dotnet format Beutl.slnx --verbosity diagnostic` を実行し、今回の追加コードも含めた15ファイルを `f88f370fe` で整形した。同じコマンドを再実行し、formatterの終了コード0と `git diff --exit-code` の終了コード0を確認した。`--verify-no-changes` の中断時に終了コードを回収できなかったため、CIが実際に判定する「整形後のGit差分なし」で再検証した。CA1416は修正プログラムを持たない既存警告として残る。


証跡:

- `/tmp/beutl-round3-headless/results.json`, `coverage-audit.json` と各フィクスチャのTRX。
- 失敗した全件実行: `/tmp/beutl-round3-headless-overlap/`。最終の単独フィクスチャ: `/tmp/beutl-round3-restore-serial.log`, `.trx`。
- `/tmp/beutl-round3-headless-after-proxy.log`, `/tmp/beutl-round3-disposal-alone.log`, `/tmp/beutl-round3-restore-pair-alone.log`。
- `/tmp/beutl-round3-proxy-multijob-red.log`, `/tmp/beutl-round3-proxy-final.log`。最後の2ファイルの整形も終了コード0（`/tmp/beutl-round3-proxy-format.log`）。
- `/tmp/beutl-round3-unit-broad.log`, `/tmp/beutl-round3-analyzers-all.log`, `/tmp/beutl-round3-build-all.log`。
- `/tmp/beutl-round3-format-final.log`, `.status`, `.diff`。

Windows/Linux実機とリモートCIの結果はこのローカル検証には含めない。従来からの広い設計・性能課題は前回記録のIssueを継続する。
