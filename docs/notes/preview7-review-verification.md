# Claudeレビューの確認・修正結果

対象: `fix/preview-7-review-findings`、基準コミット `8d3cf5a55`。確認日: 2026-09-09〜10。

元HTMLの110項目を個別に判定した。重複、仕様上の制約、未再現の推論を、修正した問題と区別する。先行4件とClaudeレビュー91項目を、計95個の項目別コミットへ分割した。修正のない判定へ空コミットは作らず、この記録に根拠を残す。

## 集計

| 判定 | 件数 |
|---|---:|
| 既知制約 | 2 |
| 修正 | 78 |
| 文書化 | 6 |
| Issue | 2 |
| 性能Issue | 5 |
| 非該当 | 5 |
| 重複 | 2 |
| 未再現 | 2 |
| 一部修正 | 3 |
| 防御強化 | 1 |
| 一部修正・Issue | 1 |
| 設計上の制約 | 3 |

## 別Issueにした項目

- [#2356: SaveLayer内の拡張描画](https://github.com/b-editor/beutl/issues/2356) — 現在レイヤーの画素を取得するネイティブ連携が必要。ガードの削除は誤描画になる。
- [#2357: 列挙できない祖先ディレクトリ下の保存](https://github.com/b-editor/beutl/issues/2357) — filesystem identityの保証を落とさない別経路が必要。
- [#2358: Append/Setのambient transform](https://github.com/b-editor/beutl/issues/2358) — 境界/hit-testのメタデータに周囲の変換を扱う設計が必要。特異行列の誤hitは今回修正。
- [#2359: 性能の計測と最適化](https://github.com/b-editor/beutl/issues/2359) — 未計測の計算量/所有権変更を、出力を変える不具合修正から分離。監視イベントの明確な列挙ボトルネックは今回修正。

## 判定一覧

| ID | 判定 | コミット | 根拠・対応 |
|---|---|---|---|
| R-N1 | 既知制約 | — | GPU初期化クリアの同期は未初期化データ漏れを防ぐ処理。tests/README.md と #2263 の制約を維持。性能だけを理由に削除しない。 |
| R-N2 | 修正 | `0a93f03b2` | SPIR-V失敗をコンテキスト別に短時間保持。実GPUで連続失敗時のコンパイル試行抑制を検証。 |
| R-N3 | 修正 | `be6315331` | TransformToDeliveredAABBをハンドル計算でも使用し、PlayerViewから実際の描画領域を渡す。 |
| R-N4 | 文書化 | `8535f57a8` | ImmediateCanvasの単発描画は所有権が呼び出し内で閉じる便利API。反復描画ではRenderNodeRendererを保持する指針を追記。 |
| R-N5 | Issue | — | 現在レイヤーの画素を取得するSkiaSharp APIがないためガードを維持。https://github.com/b-editor/beutl/issues/2356 |
| R-N6 | 修正 | `cfbbf90ef` | 遅延解放の例外をログへ記録。ロギング失敗も残りの解放や通知完了を妨げない。 |
| R-N7 | 修正 | `ee1d16465` | 有限Layer配下のcache opt-outが8回中5回しか実行されないことを再現。要求内の無効化依存を親キャッシュにも反映。 |
| R-N8 | 性能Issue | — | 計算量の指摘と実測回帰を区別。所有権・プラン整合を壊さない計測/最適化を #2359 に整理。 |
| R-N9 | 非該当 | — | SourceBackdrop.Render はフィルタをPushする前にSnapshotを記録する。提示されたSourceBackdrop自身にBlurを付ける経路では捕捉位置が異なる。 |
| R-N10 | 重複 | — | R-P1と同じ共有UBO/descriptorの問題。ドローごとの不変bindingで対応。 |
| R-N11 | 性能Issue | — | Purposeが異なる要求の記録再利用コスト。Purposeを同一視する修正はしない。#2359で計測対象。 |
| R-N12 | 非該当 | — | Purposeはコールバックから観測可能な実行契約でありキャッシュ分離は必要。CacheWarmupの本番呼び出し元もない。 |
| R-N13 | 文書化 | `620f4a15f` | IProperty.ReplaceCurrentValue/GetValidator と IReferenceExpression.Rebind の移行説明を追記。 |
| R-N14 | 未再現 | `b7dfae04b` | 共有Shader出力を2ルートへ公開し、入力・出力確保の失敗を注入する2ケースが通過。予測された再消費例外は発生せず。 |
| R-N15 | 一部修正 | `33c0302be` | TryDefer不成立時は対象コンテキストをflushし、不可能ならpoolをretire。極小scaleの有限性ガードは維持。GetBoundsには既存キャッシュあり。サムネイルはindex付き疎列、Renderer操作は指定Dispatcherを検証する。 |
| R-P1 | 修正 | `b62a4d69a` | GPU実行完了まで各drawのUBO/descriptorを不変に維持。共有材質と独立材質の画素一致を実GPUで検証。 |
| R-P2 | 修正 | `dfa49ab29` | 元画像/動画矩形からstroke外周・内周を計算し、既に膨らんだBoundsへの二重加算を除去。 |
| R-P3 | 修正 | `a55fa797a` | SaveLayerへ累積Opacityではなく今回の係数を適用。 |
| R-P4 | 防御強化 | `848969cd4` | RenderTargetのdispose取得をInterlockedに変更し、surface counterの0以下releaseも拒否。 |
| R-P5 | 一部修正・Issue | `b0194b84c` | 特異行列のhit-testはfalse。Append/Setのambient-transformモデルは #2358 に分離。 |
| R-P6 | 修正 | `a9c4a6e55` | Cube faceコピーのaspectを実際のsource formatから選ぶ。 |
| R-P7 | 既知制約 | — | GPU資源を所有Dispatcherで同期解放する契約。単に待機を外すとshutdownと所有権の順序を変えるため維持。 |
| E-N1 | 修正 | `a7f35cf5d` | 入力facadeを消費前に共有可能な値へ正規化。独立2分岐テストを追加。 |
| E-N2 | 修正 | `6ce3ed5fa` | 補助Previewは主描画のcache lifecycle/HasChangesを消費しない。 |
| E-N3 | 修正 | `f3a277faf` | 外側の非staticローカル関数を呼ぶlambdaをBESG003で検出。 |
| E-N4 | 修正 | `9a1f4857a` | 線キャップ/ドット半径を宣言出力境界に含める。元の信号矩形は描画座標として保持。 |
| E-N5 | 修正 | `10afc0c84` | Output不在時もbindingに記録済みの入力を公開し、Previewとの生入力二重消費を避ける。 |
| E-N6 | 修正 | `5026ea8d2` | 任意の未知例外は引き続き伝播。有限target domainがない補助Measure/Previewのみ空/未提供として扱う。 |
| E-N7 | 修正 | `e328010b7` | generic method/stateの照合をOriginalDefinitionへ統一。 |
| E-N8 | 修正 | `e91b5dbee` | readonly field/get-only propertyのconstructor再代入を検出。 |
| E-N9 | 修正 | `8a7498f58` | 値型のネストしたmemberへの書き込みを親stateの変更として検出。 |
| E-N10 | 修正 | `9f3ca7451` | ChildNodes getterの依存とconstructorからevent/Subscribeへ渡すcallbackの書き込みを解析。 |
| E-N11 | 修正 | `92afb757c` | 終了したblock/for scopeで同名localを再利用できるようにし、同時に有効なscopeのshadowは拒否。 |
| E-N12 | 修正 | `f9e28c1a1` | CurrentPixelのunsupported uniform boolを記録時に拒否。 |
| E-N13 | 修正 | `447d8885f` | primary constructorのnode stateを認識。重複診断を抑制し、より浅い経路で再訪したmethodは深さのある解析を再実行。 |
| E-N14 | 性能Issue | — | immutable curve/LUTの画像生成・転送を #2359 の計測/所有権設計対象にした。出力の色空間バグはE-P3で別途修正。 |
| E-N15 | 文書化 | `e22e37e59` | SKSLShader/GLSLShaderのnamespace移動を追記。Particleの有限domain/PixelSortの非対応backendは別の能力制約。 |
| E-P1 | 修正 | `9bfc2db3f` | MatrixConvolutionの左上outsetの符号を訂正。 |
| E-P2 | 修正 | `00b448c7f` | 例外を伝播し全SKPathと作成済みtargetを解放。EffectTargetの所有権経由で旧targetを破棄。 |
| E-P3 | 修正 | `c8d872eb7` | 恒等LUTの中間色が55から10へ暗くなることを再現。データ画像をlinear色空間で生成しテスト通過。 |
| E-P4 | 修正 | `e32b038aa` | linearToSrgbの非整数pow入力を非負に制限。 |
| E-P5 | 修正 | `7e5a694d5` | 既にpremultipliedなRGBへalphaを二重に掛けない。hit-testも同じ定義へ変更。 |
| E-P6 | 一部修正 | `488e11d2f` | cycleで除外した接続をErrorにしreleaseログも記録。UIのConnectionLineは既にObserveOnUIDispatcherで通知を受けるため、指摘のUIスレッド違反は非該当。 |
| E-P7 | 修正 | `9e5bc379b` | TransformNode.Resourceが所有する出力RenderNodeを破棄。入力参照wrapperは元入力を所有しない。 |
| E-P8 | 修正 | `1d6dfb5af` | 同じresource versionのスペクトラム再描画は計算済みbarsを使い、ピークreleaseを再適用しない。 |
| E-P9 | 非該当 | — | 粒子描画とbounds計算はspriteの中心を差し引く。固定sprite配置領域だけからシーン解像度依存の位置ずれは導けない。 |
| E-P10 | 修正 | `616d16ef6` | 補間によるrange外値も考慮し、Mosaicの描画とhit-testへ有限の正のtile sizeを渡す。 |
| D-N1 | 修正 | `40756768e` | 材質pipelineをRenderPassの同一性で更新。3材質のresize後描画を実GPUで検証。 |
| D-N2 | 修正 | `2fdee9868` | inline paddingを上流/下流latencyの合計から一度だけ引く。両側limiterの残360samplesと末尾120samplesを検証。 |
| D-N3 | 未再現 | `626960902` | fractional parent scale 0.3/0.5/1.7、OutputScale 1.3/Max 0.7のdrawable texture描画が実GPUで通過。密度の厳密な契約チェックは維持。 |
| D-N4 | 文書化 | `cc5fa3fe6` | drawable texture依存の列挙をMaterial3Dの移行ガイドに明記。未準備のnested renderer起動は禁止を維持。 |
| D-N5 | 修正 | `cb73f0787` | 非有限/範囲外のspline control pointを要素全体の失敗でなくeasing fallbackへ送る。 |
| D-N6 | 修正 | `b93723507` | 非同期通知の購読者例外もログへ記録し、通知完了を必ず決着。 |
| D-N7 | 文書化 | `adbcd05c7` | Geometryのresource移行は既に文書化済み。IKeyFrame.ReplaceValueとIntegrate(IAnimation<float>)のバイナリ再ビルド要件を追記。 |
| D-N8 | 修正 | `e43d753e6` | font列挙をdirectory単位に分け、1つの失敗が待機中の他subtreeを破棄しない。リンク循環も防止。 |
| D-N9 | 修正 | `dbfa7a12f` | priority昇格時にadmission waitへ通知。最終監査で欠けていた呼び出しを補完。修正前は回帰テストが10秒でtimeout、修正後はProxyJobQueueTestsの57件が通過。 |
| D-N10 | 修正 | `8420fadcf` | MessageType.Errorでもmsg.ErrorCodeをFFmpegWorkerExceptionへ渡す。 |
| D-N11 | 修正 | `393e486aa` | WaitingForAdmission通知はWorkItem lockを解放してから呼ぶ。 |
| D-N12 | 性能Issue | — | 旧fallbackキャッシュの到達性と実際のnested request/capture hitを区別して #2359 で計測。 |
| D-N13 | 修正 | `1bae17cad` | 欠落control pointは従来の既定値0/0/1/1を補完。明示的な不正値はfallbackを維持。 |
| D-P1 | 重複 | — | R-P1と同一。 |
| D-P2 | 修正 | `b2fe4e370` | ExponentialEaseInOutの0/1端点を厳密に返す。 |
| D-P3 | 修正 | `e3ffe6ed8` | worker開始のawaitでUI SynchronizationContextを捕捉しない。 |
| D-P4 | 修正 | `64bde2a15` | ライブラリ欠落以外の開始失敗にも短い再試行cooldownを設ける。 |
| D-P5 | 修正 | `db9527002` | transparent childrenをworld transform付きで再帰収集し、opaque passでは透明meshを描かない。 |
| D-P6 | 修正 | `ae9c001de` | shadow passがCastShadowsを参照。Basic/PBRのReceiveShadowsをG-buffer経由でlightingへ伝え、無効時はshadow無効controlと画素一致。 |
| D-P7 | 修正 | `350cea476` | 最大cached secondを保持し、遠い時刻から空のsecondを線形走査しない。 |
| D-P8 | 修正 | `e4d70be8e` | mono入力では左channelを右へ複製し、存在しないchannel 1を参照しない。 |
| P-N1 | Issue | — | 列挙不要でも安全なfilesystem identityが必要。所有外と決めつけるfallbackは避ける。https://github.com/b-editor/beutl/issues/2357 |
| P-N2 | 修正 | `db9c48b21` | 保存衝突でSourceUriへ勝手に戻さず、直前の有効なrehome先を保持。既存collisionテストの期待を訂正。 |
| P-N3 | 性能Issue | — | migration requirementは再配置にも必要なので消さない。繰り返し走査/書込みの最適化は #2359。 |
| P-N4 | 非該当 | — | 途中まで新形式を保存した場合にも旧readerを拒否するため、migration metadataを先行永続化する必要がある。 |
| P-N5 | 修正 | `6914d2424` | traceでHasReservedMetadataSegmentからの大量列挙を確認。別名かつ非linkの通常Unix siblingは早期に不一致判定。43秒でpath/watcher 66件通過（1件Windows専用skip）。残る広いIO最適化は #2359。 |
| P-P1 | 修正 | `524b8a194` | 参照sceneとconverterのファイル書込みをStoreToUriのatomic経路へ統一。 |
| P-P2 | 修正 | `ed30bed32` | standalone sceneにrootがなくても保存する。削除はsceneから外れたElementに限定。 |
| P-P3 | 設計上の制約 | — | 既知型のunknown propertyはextension-data契約ではない。未知型/復旧bytes保護と区別し、無条件のunknown field永続化は追加しない。 |
| P-P4 | 修正 | `79703ef15` | glob結果をraw file pathとして絶対化し、URI相対結合前のunescapeを撤去。#/%20ファイルの再読込テスト通過。 |
| V-N1 | 修正 | `abf78795c` | メニュー/ウィンドウ終了の通常UI経路をasync化。互換用の同期APIは残す。 |
| V-N2 | 修正 | `4131402eb` | repository hygiene失敗を通知する。compare-and-exchangeを保証できないFSへ非atomic fallbackは導入しない。 |
| V-N3 | 修正 | `806ff0fc4` | Git stderrを明示的にUTF-8で読む。 |
| V-P1 | 修正 | `b3f4d1688` | OutputServiceの復元はread-onlyで開く。 |
| V-P2 | 修正 | `e4833d610` | 利用不能な出力profileのJSONを保持して次の保存にも含める。 |
| V-P3 | 修正 | `18304e839` | 新projectのversion/deserialize成功後に現在のprojectを閉じる。 |
| V-P4 | 修正 | `701ee24bf` | CloseFileのasync event例外を通知へ接続。 |
| V-P5 | 修正 | `12a88131a` | 既存project itemとの比較をcanonical file identityへ変更。 |
| V-P6 | 修正 | `ef615b8f9` | splitをisolated history transactionにし、新規sidecar書込み失敗時は変更/新規ファイルを戻す。無効なsplitはtransactionを始めない。 |
| A-N1 | 修正 | `840d64f05` | beutl-webの0.05s許容/終端clampを照合。クライアント検証エラーはpaid successのkeyを廃棄しない。 |
| A-N2 | 修正 | `4a1ece0c8` | 成功後のretry-key退役処理をprovider operationの失敗判定から分離。 |
| A-N3 | 設計上の制約 | — | admissionは明示的なhost統合用契約であり、この機能単体が全host処理を排他化する約束ではない。 |
| A-N4 | 修正 | `2ffef6dc5` | 一時的なexport/project transition中は有効化を待って再試行するhintを追加。 |
| A-N5 | 修正 | `469bf50ba` | live create_project/add_sceneの保存をhost project-file-write lease内に入れる。open遷移前にはleaseを解放。 |
| A-P1 | 文書化 | `a82a57754` | project MCP configがcredentialを含むことと共有禁止を明記。既存clientを無効化する自動rotationは導入しない。 |
| A-P2 | 修正 | `8fa4127d3` | LocalPathの二重unescapeを除去。 |
| A-P3 | 修正 | `d05bb4a69` | 終端render jobsは最新128件に制限。running jobsはevictしない。 |
| S-N1 | 修正 | `8c4a25d46` | data payload展開はTask.Run、extension activation/登録通知はUI threadで行う。 |
| S-N2 | 修正 | `db59d4dd5` | template watcherがdirectory作成/renameも監視。populated directory移動の反映テスト通過。 |
| S-N3 | 修正 | `30ca7b8a9` | owner markerまたは登録済み旧package metadataがあるpayloadだけを入替/削除。自作同名directoryを保持。 |
| S-N4 | 修正 | `a19f39abb` | 公開済みversionを削除する場合は生存するversionのpayloadへ戻す。 |
| S-N5 | 設計上の制約 | — | 5s idle/30s drainは中断fallbackを確定する既存の終了契約。画面を離れただけでインストールを中断する変更はしない。 |
| S-P1 | 修正 | `b1ec7b2b1` | 宣言hash不一致は展開/ロード前に拒否。hash未提供ケースとは区別。 |
| S-P2 | 修正 | `4866fb162` | downloadを一時ファイルへ保存後rename。壊れた既存nupkgの読込もファイル単位で隔離。 |
| S-P3 | 修正 | `b431c30bb` | HTTP非成功を拒否。Bearerは設定済みAPI originだけへ送る。 |
| S-P4 | 修正 | `3e48cb618` | installedPackages.jsonをatomic JsonSaveへ変更。 |
| S-P5 | 一部修正 | `b904f7297` | item破棄でmetadata tokenをcancel。待機/開始前処理を打ち切り、遅い結果をUIへ反映しない。開始済みnative probe自体の中断は既存APIの制約。 |
| S-P6 | 修正 | `e41e90a2a` | preferences.jsonをatomic JsonSaveへ変更。 |
| S-P7 | 修正 | `e03d1afbe` | hostからファイル使用状態を取得し、open project/editorの保存対象やその親directoryの削除/renameを拒否。 |
| M-N1 | 非該当 | — | 固定版codecov-actionはCC_FORK検出後OIDC取得をスキップしtokenlessへ分岐する。https://github.com/codecov/codecov-action/blob/fb8b3582c8e4def4969c97caa2f19720cb33a72f/action.yml#L213-L273 |
| M-P1 | 修正 | `38e273592` | template名のパス成分を拒否し、保存失敗をUIへ通知。 |
| M-P2 | 修正 | `cbb95510c` | RecentFiles/RecentProjectsもheaderで解決。 |
| M-P3 | 修正 | `a5ee82ba5` | template serializationとasync eventの例外を処理し、プロセスへ漏らさない。 |

## 検証

- Engine: 4,374 passed / 3 skipped（4,377件）。描画、音声、アニメーション、キャッシュ、resource lifetimeなど。
- 実GPU（MoltenVK / Apple M3）: 12 passed。共有材質、resize、透明子階層、影の制御、fractional texture density。
- アナライザ: 313 passed。キャプチャ、constructor代入、generic基底、mutable struct、ChildNodes依存、走査順/重複。
- Agent Toolkit関連: 80 passed。ジョブ保持上限、プロジェクト操作、履歴、workspace boundary。
- ヘッドレス回帰: 5 passed。転写許容値、現在プロジェクト保持、読み取り専用/未知profile保持、開いたシーンの保護。
- 非Engine広域テスト: 4,085 passed / 6 skipped（4,091件）。Gitの実リポジトリを使うVersionControl等を除外。
- 分割前の広域検証: 8,869 passed / 9 skipped。
- 最終監査で追加したD-N9回帰を含むProxyJobQueueTests: 57 passed。旧広域実行と重なる56件を除くと、検証済みの異なるテストは計8,870 passed / 9 skipped。
- 最終アプリ本体ビルド: 成功（net10.0 / net10.0-windows、9 warnings / 0 errors。nullableとMediaFoundationのplatform analyzer警告）。
- Git実リポジトリの大規模suite、Windows/Linux実機、Vulkan validation、有料AIの実通信は今回の最終広域実行には含めていない。

NUnit Adapterは選択数が既定の2,000を超えると選択リストを捨てるため、最終の広域実行では `AssemblySelectLimit=100000` を指定した。途中の広すぎる実行は通過扱いにしていない。

参考: [NUnit Adapterの選択上限](https://docs.nunit.org/articles/vs-test-adapter/Tips-And-Tricks.html#assemblyselectlimit)、[使用中Codecov Actionのfork分岐](https://github.com/codecov/codecov-action/blob/fb8b3582c8e4def4969c97caa2f19720cb33a72f/action.yml#L213-L273)。

ログ: `/tmp/beutl-claude-engine-final.log`, `/tmp/beutl-claude-nonengine-final.log`, `/tmp/beutl-claude-3d-final.log`, `/tmp/beutl-claude-analyzer-final.log`, `/tmp/beutl-claude-agent-final.log`, `/tmp/beutl-claude-host-final.log`。

## 履歴分割の監査

- 元の2個の一括コミット（`680d1a3cd`, `6c6dbb9ce`）を基準HEADから分割。元の履歴はローカルの `refs/backup/preview7-review-before-item-split` に保持。pushはしていない。
- 全357 diff hunkを項目へ割り当て、同じhunk内の別項目の実装・テスト・文書も分離。依存する修正を先に配置した。
- 分割後のGit treeと検証済み作業ツリーの完全一致を確認してからブランチを更新。元の一括コミットからの追加はD-N9の通知呼び出しと回帰テストの2ファイル・39行だけ。
- 個々の中間コミットで全ビルド・全テストを繰り返したものではない。テスト結果は最終ファイル内容に対する検証である。
- 追加ログ: `/tmp/beutl-review-priority-red.log`, `/tmp/beutl-review-priority-green.log`, `/tmp/beutl-review-split-build.log`。

先行レビュー4件のコミット:

- `1ecdde91c`: initial-package
- `735561e64`: initial-path-boundary
- `70f3a4d41`: initial-history
- `4653fd1f2`: initial-recovery-alias
