# Beutl.Language リソースファイル分割ルール

## リソースファイル一覧

| ファイル | 用途 |
|---|---|
| `Strings` | 汎用的な文字列 (OK, Cancel, Yes, No など)  |
| `AudioStrings` | Beutl.Audio 名前空間のクラス用 |
| `GraphicsStrings` | Beutl.Graphics, Beutl.Graphics3D, Beutl.Media 名前空間のクラス用 |
| `SettingsStrings` | 設定画面用 |
| `ExtensionsStrings` | 拡張機能画面用 |
| `TutorialStrings` | チュートリアル画面用 |
| `Message` | メッセージ文字列 |
| `CommandNames` | コマンド名 |

## 参照ルール

### エンジン層 (Beutl.Audio, Beutl.Graphics等)

- **Beutl.Audio** から参照してよいリソースは **AudioStrings のみ**。
- **Beutl.Graphics, Beutl.Graphics3D, Beutl.Media** のグラフィック関係のクラスから参照してよいリソースは **GraphicsStrings のみ**。
- 他のリソースファイルに同じ文字列が存在していても、自身の属する名前空間のリソースファイルを使うこと。

### UI, プロジェクト層 (Beutl, Beutl.ProjectSystem 等)

- UIやプロジェクト層からは **自由にリソースファイルを参照できる**。

## 文字列キーの命名規則

AudioStrings と GraphicsStrings では以下の命名規則を使う。

- **クラス**: クラス名をそのまま使う (例: `EllipseGeometry`)
- **プロパティ**: `クラス名_プロパティ名` の形式にする (例: `EllipseGeometry_RadiusX`)
- **複数箇所で使われる文字列**: 単純な名前にする (例: `EllipseShape_Width` ではなく `Width`)

## リソースファイルの追加

リソースファイルを分けた方がよい場合は、画面や機能などのグループ単位で分ける。

例: `SettingsStrings`, `ExtensionsStrings`, `TutorialStrings`

## Strings に入れる文字列の基準

Strings には、**複数の言語に翻訳しても文脈ごとに意味が変わらない文字列** を入れる。

- OK, Cancel, Yes, No -> ほとんどの場合で一つの意味になるので **Strings に入れる**
- Translate -> 「移動」「翻訳」の二つの意味があるため **Strings には入れない** (使用箇所に応じた専用リソースファイルに入れる) 
