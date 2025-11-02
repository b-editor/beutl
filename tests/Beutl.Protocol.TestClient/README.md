# Beutl.Protocol Test Client

SignalRトランスポートレイヤーの検証用テストクライアント。

## 実行方法

```bash
cd tests/Beutl.Protocol.TestClient
dotnet run

# カスタムURLで接続
dotnet run http://localhost:5001/sync
```

## テストシナリオ

1. **名前の更新** - プロパティ変更の同期をテスト
2. **カウンターのインクリメント** - 数値プロパティの同期をテスト
3. **リストへのアイテム追加** - リスト追加操作の同期をテスト
4. **リストからのアイテム削除** - リスト削除操作の同期をテスト
5. **現在の状態表示** - オブジェクトの現在の状態を表示

## 複数クライアントでのテスト

### ターミナル1: サーバーを起動
```bash
cd tests/Beutl.Protocol.TestServer
dotnet run
```

### ターミナル2: クライアント1を起動
```bash
cd tests/Beutl.Protocol.TestClient
dotnet run
```

### ターミナル3: クライアント2を起動
```bash
cd tests/Beutl.Protocol.TestClient
dotnet run
```

クライアント1で変更を加え、クライアント2で状態を確認して同期が機能していることを確認します。
