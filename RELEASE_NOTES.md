## BEditor 0.1.3
[ダウンロード](https://github.com/b-editor/BEditor/releases/tag/v0.1.3)

### 変更
* 動画ファイルの入出力を拡張機能で追加するように変更。
* シーン, クリップ, エフェクトを追加するダイアログを追加。(#171, #172, ＃173)
* キーフレームのあるプロパティを読み込んだときに例外が出るのを修正。
* アライメントを設定するエフェクトを追加。(#175)
* 通知機能を追加。(#191)
* 極座標変換エフェクトを追加。(#185)
* プラグインのインストーラーを追加。 (#198)
* スタートウィンドウを追加。 (#175)
* テキストオブジェクトの "個別オブジェクト" を削除。
* 音声オブジェクトの座標プロパティを削除。
* Idが重複する可能性があったので修正
* タイムラインの目盛りを横幅を超えたとき表示しないようにした
* クリップの移動を修正

## BEditor 0.1.2

* [ダウンロード](https://github.com/b-editor/BEditor/releases/tag/v0.1.2)

### 変更
* 動画出力、音声出力機能を追加
* 角丸矩形をSkiaから自分で実装したものに変更
* 内側シャドウ エフェクトを追加
* 不透明度を反転するエフェクトを追加
* 設定のUIの項目を縦に並べるように変更
* ローカライズした文字列を修正
* イージングの設定を開いた時に強制終了する不具合を修正
* エフェクトの順番を変更したときに強制終了する不具合を修正
* IDをコピーするメニューを追加
* WPFからの移植
    * プラグインを読み込むかのダイアログを実装
    * プラグインの設定UIを実装
    * 無効なプラグインのUIを実装
    * ObjectViewerを実装
* OpenCVのエフェクト
    * ガウスぼかし
    * ぼかし
    * メディアンぼかし

## BEditor 0.1.1 Preview 2

* [ダウンロード](https://github.com/b-editor/BEditor/releases/tag/v0.1.1-preview.2)

### 変更
* 日本語環境のLinux系OSで起動できない不具合を修正
* Gpuでの画像処理に対応
* プロジェクトの要素をGuidで管理するように変更
* FilePropertyのModeがプロジェクトファイルから復元されない不具合を修正
* タイムラインの目盛りを追加
* ColorDialogが強制終了するのを修正
* プレビュー中にUIが固まるのを軽減
* ツールバーに表示されないオブジェクトを追加できない不具合を修正
* キーフレームの編集UI追加

## BEditor 0.1.0 Preview 1

* [ダウンロード](https://github.com/b-editor/BEditor/releases/tag/0.1.0-preview.1.0)

### 変更
* アイコンを変更
* クロスプラットフォームに対応 (検証済み: Windows10 19042.928, Ubuntu 20.04.2 LTS)
* テキストの描画が改行に対応
* エフェクトの追加
   * 二階調化
   * 明るさ調整
   * コントラスト調整
   * 拡散
   * ガンマ調整
   * グレースケール
   * ネガポジ
   * RGB調整
   * セピア
   * Xor
   * ライトの実装
* オブジェクトの追加
   * 3Dオブジェクトの実装
   * 音声オブジェクトの実装
   * フレームバッファ
   * リスナーオブジェクト (要調整)
* OpenALの音声出力
* プロジェクトファイルをJsonに変更

### Linux系OSで起動出来ない。

FontManager系の例外(*)が発生している場合、  
OSの言語を英語に変更すると起動できます。

(*) 出力のスタックトレースにFontManagerが含まれていたら該当します

---

## BEditor 0.0.5

* [ダウンロード](https://github.com/b-editor/BEditor/releases/tag/v0.0.5-alpha)
* [マイルストーン](https://github.com/b-editor/BEditor/milestone/2)


### 変更
* スタートウィンドウを追加。
* タイムラインの表示非表示がわかりやすくなりました。
* OpenGL4に移行しました。
* クリップの分割機能を追加。
* 多重オブジェクトを追加。
* ファイルの参照を相対パスで保存可能にしました。
* 作成系ウィンドウを変更しました。
* クリップが重ねられないようにしました。
* 使い方のページを追加。
* FFmpegを自動でインストールする機能を追加。
* ログ機能を追加。
* 依存ライブラリのライセンスを追加。
* エフェクトを追加。
    * カラーキー
    * クロマキー
    * マスク
    * 線形グラデーション
    * 円形グラデーション

### 注意点
* 音声再生のAPIを __OpenAL/FFmpeg__ に移行しているため音声オブジェクトは使えません。
* 名前空間を変更したため、BEditor 0.0.4以前のプロジェクトは利用することができません。
* OpenGL4に移行したため3DObjectとライトの実装はまだです

---

## BEditor 0.0.4

* [ダウンロード](https://github.com/b-editor/BEditor/releases/tag/v0.0.4-alpha)
* [マイルストーン](https://github.com/b-editor/BEditor/milestone/3)

### 変更
* 角丸四角形の追加
* Polygonの追加
* UIの変更
* 再生機能の追加
* 音声ファイルの読み込み
* 動画ファイルの入出力
* SkiaSharpを使った画像処理に変更
* 設定項目の追加
    * 背景色を変更可能に
    * 読み込むフォントのディレクトリの指定(Jsonから変更可)
    * 読み込むプラグインの選択

### 既知のバグ
* __開始時にフォント読み込みが終わらない場合はソフトを閉じてuser/setting.jsonのIncludeFontDirの1つ目の要素を削除してください__
* プロジェクトの作成時にDllNotFoundExceptionがスローされる場合は __OpenAL__ をインストールしてから再度作成してください

---

## BEditor 0.0.3

* [ダウンロード](https://github.com/b-editor/BEditor/releases/tag/v0.0.3-alpha)
* [マイルストーン](https://github.com/b-editor/BEditor/milestone/1)

### 変更
* ランタイムが.NET5になりました
* 3Dオブジェクトの追加
* シーン, クリップを管理するObjectViewerを追加
* クリッピング, 領域拡張エフェクトの追加
* 光源エフェクトの追加

---

## BEditor 0.0.2

* [ダウンロード](https://drive.google.com/file/d/15BZabYO3jz_bGCnBT3IyMnxiJWHLAb-o/view?usp=sharing)

---

## BEditor 0.0.1

* [ダウンロード](https://drive.google.com/file/d/19w8gj_la7JAaCQjlEVldbbpos9xyMjrL/view?usp=sharing)