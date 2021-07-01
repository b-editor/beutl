# 各csprojの説明

## BEditor.Settings

* BEditorの設定。  
* シリアライズのクラスと設定クラス
* プラグインのパッケージクラス等

## BEditor.Compute

* OpenCLのオブジェクト指向のApiを提供します。

## BEditor.Drawing

* 2Dの描画機能を提供します。
* 内部でSkiaSharp, OpenCVを使っていますが、一部の処理はC#で実装しています。

## BEditor.Graphics

* ハードウェアアクセラレーションによる描画機能を提供します。

## BEditor.Media

* メディアファイルの入出力を抽象化します。

## BEditor.Core

* プロジェクトのデータを管理します。

## BEditor.Primitive

* デフォルトであるオブジェクトやエフェクトがあるライブラリです。

## BEditor.Avalonia

* BEditorの実行ファイルのプロジェクトです。  
* AvaloniaUIをつかっています。

# プロジェクトデータの構造

> Project
> > Scene
> > > ClipElement
> > > > EffectElement
> > > > > PropertyElement
> > > > > > (EasingFunc)

のような感じです。  
* 各要素は自分の親要素を取得することができます。  
* EasingFuncはEaseProperty, ColorAnimationPropertyで有効です。
* Projectの親要素はIApplicationです。
* 各要素はIParent, IChild, IElementObjectを実装します。
* 各要素はEditorObjectを継承します。
* EditingObjectとは
    * このクラスを継承してEditingPropertyの設定をするとPropertyElementの初期化が楽になる。
    * さらに子要素のLoad, Unloadも自動でやってくれる。
* Bindableとは
    * プロパティの値を同期する機能
    * 実態はIObservableとIObserverを実装するオブジェクトなのでSystem.Reactiveを使うことができる

## Project

編集データの1番上の要素です。

## Scene

他の動画編集ソフトだとコンポーネントとかコンポって呼ばれてるものです。

## ClipElement

シーンに配置される素材です。

## ObjectElement

動画オブジェクト, 画像オブジェクト, テキストオブジェクトなどがこのクラスを継承しています。  
  
本当はClipElementを継承する方が理にかなっていますが子要素が二種類存在することになるからやめました。  
このオブジェクトはClipElement.Effect[0]にあります。  
プラグインで追加することができます。

## EffectElement

エフェクトのベースクラスです。
画像エフェクトはImageEffectを継承します。  
プラグインで追加することができます。

## PropertyElement

エフェクトのプロパティのベースクラスです。

## EasingFunc

EaseProperty, ColorAnimationPropertyのイージング関数のベースクラスです。
プラグインで追加することができます。

# プラグインについて
プラグインにできること
* オブジェクト, エフェクト, イージングの追加
* エンコーダー, デコーダーの追加
* カスタムメニューの追加
* DIコンテナにサービスを追加

## BEditor.Extensions.FFmpeg
* エンコード、デコードにFFmpegを使えるようにする、拡張機能です

## BEditor.Extensins.Svg
* Svg画像オブジェクトを追加します

## BEditor.Extensions.AviUtl
* AviUtl 拡張編集プラグインのスクリプトを実行できるようにします。
