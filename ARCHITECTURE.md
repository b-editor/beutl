# 各csprojの説明

## BEditor.Settings

BEditorの設定。  
シリアライズのクラスと設定クラスのみ

## BEditor.Drawing

* 2Dの描画ライブラリです。  
* 内部でSkiaSharpを使っていますが、一部の処理はC#で実装しています。
* プラットフォーム - [Windows, Linux, macOS]

## BEditor.Graphics

* OpenGLを使った3Dの描画ライブラリです。
* プラットフォーム - [Windows, X11対応OS, macOS]

## BEditor.Media

* メディアファイルの入出力ライブラリです。  
* FFmpegを利用しています。
* プラットフォーム - [Windows, Linux, macOS]

## BEditor.Core

* 主にプロジェクトのデータがあるライブラリです。
* プラットフォーム - [Windows, Linux, macOS]

## BEditor.Primitive

* デフォルトであるオブジェクトやエフェクトがあるライブラリです。
* プラットフォーム - [Windows, Linux, macOS]

## BEditor.CLI

* コンソールでプロジェクトを編集する実行ファイルのプロジェクトです。
* プラットフォーム - [Windows, Linux, macOS]

## BEditor.WPF.Controls

* BEditor.WPF用のカスタムコントロールライブラリです。
* プラットフォーム - [Windows]

## BEditor.WPF

* BEditorの実行ファイルのプロジェクトです。  
* WPFをつかっています。
* プラットフォーム - [Windows]

## BEditor.Package

* デスクトップブリッジのプロジェクトです。
* ストア配布するには証明書が必要なので使わないと思う
* プラットフォーム - [Windows10]

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
* EditorObjectとは
    * WPFのUIElementなどをキャッシュするために作ったクラス
    * このクラスを継承するとプロパティを拡張できる。
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
  
本当はClipElementを継承する方が理にかなってが子要素が二種類存在することになるからやめました。  
このオブジェクトはClipElement.Effect[0]にあります。  

## EffectElement

エフェクトのベースクラスです。
画像エフェクトはImageEffectを継承します。  
プラグインで追加することができます。

## PropertyElement

エフェクトのプロパティのベースクラスです。

## EasingFunc

EaseProperty, ColorAnimationPropertyのイージング関数のベースクラスです。
プラグインで追加することができます。
