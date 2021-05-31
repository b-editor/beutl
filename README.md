# BEditor

![](https://img.shields.io/github/issues/b-editor/BEditor)
![](https://img.shields.io/github/forks/b-editor/BEditor)
![](https://img.shields.io/github/stars/b-editor/BEditor)
![](https://img.shields.io/github/license/b-editor/BEditor)
![](https://img.shields.io/github/downloads/b-editor/BEditor/total)
![](https://img.shields.io/github/v/release/b-editor/BEditor)
![](https://img.shields.io/github/repo-size/b-editor/BEditor)
![](https://github.com/b-editor/BEditor/workflows/Debug%20Build%20&%20Test/badge.svg)
![](https://github.com/b-editor/BEditor/workflows/CodeQL/badge.svg)

Windows, Linux, macOSで動作する動画編集ソフトウェアです。

![](https://beditor.net/api/header/?version=0.1.3)

## Feature

* OpenGLを使ったレンダリング
* カメラ制御
* GPUを使った画像処理
* ダークモード
* クロスプラットフォーム (0.1.0から)
* キーフレーム機能
* 100個のレイヤー
* プラグイン機能
* [編集データの値を同期](https://beditor.net/Document?page=how-to-use/data-binding)
* [シーン機能](https://beditor.net/Document?page=keywords/scene)
* [30種類以上のエフェクト](https://beditor.net/Document?page=effects/overview)
* 12種類のオブジェクト
    * フレームバッファ, 画像ファイル, 多角形, 角丸四角形, シーン, 図形, テキスト, 動画ファイル, カメラ, 3Dオブジェクト

## Requirements
* [OpenAL](https://www.openal.org/)

## License

* [MIT License](https://github.com/b-editor/BEditor/blob/main/LICENSE)
* 一部の拡張機能は __GPL version 2__ です。
* このソフトウェアは、[Apache 2.0 ライセンス](http://www.apache.org/licenses/LICENSE-2.0)で配布されている製作物が含まれています。

## Building BEditor

* .NET 5.0 が必要です
* 以下のコマンドを実行すると `./publish` に出力されます。
```
dotnet restore
dotnet cake
```

## Screenshots

![](https://raw.githubusercontent.com/b-editor/BEditor/main/docs/imgs/ScreenShot_1.png)
