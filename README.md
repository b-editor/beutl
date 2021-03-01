# BEditor

![](https://raw.githubusercontent.com/b-editor/BEditor/develop/docs/imgs/header.png)

![](https://img.shields.io/github/issues/b-editor/BEditor)
![](https://img.shields.io/github/forks/b-editor/BEditor)
![](https://img.shields.io/github/stars/b-editor/BEditor)
![](https://img.shields.io/github/license/b-editor/BEditor)
![](https://github.com/b-editor/BEditor/workflows/Debug%20Build%20&%20Test/badge.svg)

## Description
現在開発中の動画編集ソフトウェアです。

## Requirements
* [OpenAL](https://www.openal.org/)
* [FFmpeg(同梱されています)](https://ffmpeg.org)

<details>
<summary>使用ライブラリ</summary>

## 使用ライブラリ
* [.NET runtime](https://github.com/dotnet/runtime)
* [OpenTK](https://github.com/opentk/opentk)
* [System.Reactive](https://github.com/dotnet/reactive)
* [NAudio](https://github.com/naudio/NAudio)
* [SkiaSharp](https://github.com/mono/SkiaSharp)

</details>

## License

* [MIT License](https://github.com/b-editor/BEditor/blob/main/LICENSE)

## Author

* [indigo-san](https://github.com/indigo-san)

# News

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

## BEditor 0.0.4

* [ダウンロード](https://github.com/b-editor/BEditor/releases/tag/v0.0.4-alpha)
* [マイルストーン](https://github.com/b-editor/BEditor/milestone/3)

## BEditor 0.0.3

* [ダウンロード](https://github.com/b-editor/BEditor/releases/tag/v0.0.3-alpha)
* [マイルストーン](https://github.com/b-editor/BEditor/milestone/1)

## BEditor 0.0.2

* [ダウンロード](https://drive.google.com/file/d/15BZabYO3jz_bGCnBT3IyMnxiJWHLAb-o/view?usp=sharing)

## BEditor 0.0.1

* [ダウンロード](https://drive.google.com/file/d/19w8gj_la7JAaCQjlEVldbbpos9xyMjrL/view?usp=sharing)
