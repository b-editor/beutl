# 🎬 BeUtl

![](https://img.shields.io/github/issues/b-editor/BeUtl)
![](https://img.shields.io/github/forks/b-editor/BeUtl)
![](https://img.shields.io/github/stars/b-editor/BeUtl)
![](https://img.shields.io/github/license/b-editor/BeUtl)
![](https://img.shields.io/github/downloads/b-editor/BeUtl/total)
![](https://img.shields.io/github/v/release/b-editor/BeUtl)
![](https://img.shields.io/github/repo-size/b-editor/BeUtl)
[![Daily build](https://github.com/b-editor/BeUtl/actions/workflows/daily-build.yml/badge.svg)](https://github.com/b-editor/BeUtl/actions/workflows/daily-build.yml)
[![Discord](https://img.shields.io/discord/868076100511760385.svg?label=&logo=discord&logoColor=ffffff&color=7389D8&labelColor=6A7EC2)](https://discord.gg/Bm3pnVc928)

BeUtlはクロスプラットフォームで動作する動画編集ソフトウェアです。  

[古いバージョン(BEditor)のソースコード](https://github.com/b-editor/BeUtl/tree/old/main)

![](https://raw.github.com/b-editor/BeUtl/main/assets/screenshots/screenshot-light-dark.png)

## 📖 Feature

✅ ダークモード  
✅ クロスプラットフォーム (0.1.0から)  
✅ アニメーション機能  
✅ アカウント機能  
🚧 プラグイン機能  
🚧 シーン機能  

## ビルド方法
このビルド方法で生成された実行ファイルはあくまでテスト用に使ってください。
それ以外の用途の場合、[日次ビルド](https://github.com/b-editor/BeUtl/actions/workflows/daily-build.yml)
からダウンロードしたものを使用してください。
1. このリポジトリを`--recursive`を付けてクローンします。
3. Authentication(Email, Google)、Firestore, Storageを有効にしたFirebaseプロジェクトを用意します。
4. 環境変数`BEUTL_FIREBASE_KEY`に上のAPIキーを設定します。
5. `src/BeUtl/Models/Constants.cs`の`FirebaseProjectId`変数の値を変更します。
6. `src/BeUtl.NetCore`内で`dotnet build`コマンドを実行
7. `src/BeUtl.NetCore/bin`以下に実行ファイルが生成されます。

## ブランチ
| 名前 | 目標 |
| --- | --- |
| main |  |
| restore-beditor-legacy | BEditorのコード資産を復元する |

## License

- [MIT License](https://github.com/b-editor/BeUtl/blob/main/LICENSE)

## Patrons

- [Bony_Chops](https://www.patreon.com/user/creators?u=52944861)
- [Hayashi Tomonari](https://www.patreon.com/user/creators?u=62872137)
