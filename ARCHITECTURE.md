# 各csprojの説明

## BEditor.Settings

* BEditorの設定。  
* シリアライズのクラスと設定クラスのみ

## BEditor.Compute

* OpenCLのラッパーです。

## BEditor.Drawing

* 2Dの描画ライブラリです。  
* 内部でSkiaSharpを使っていますが、一部の処理はC#で実装しています。

## BEditor.Graphics

* OpenGLを使った3Dの描画ライブラリです。

## BEditor.Media

* メディアファイルの入出力ライブラリです。  
* FFmpegを利用しています。

## BEditor.Core

* 主にプロジェクトのデータがあるライブラリです。

## BEditor.Primitive

* デフォルトであるオブジェクトやエフェクトがあるライブラリです。

## BEditor.Console

* コンソールでプロジェクトを編集する実行ファイルのプロジェクトです。

## BEditor.WPF.Controls

* BEditor.WPF用のカスタムコントロールライブラリです。

## BEditor.WPF

* BEditorの実行ファイルのプロジェクトです。  
* WPFをつかっています。

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
    * 実態はIObservableとIObserverを実装するオブジェクトなのでSystem.Reactiveを使うことが出来る

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
