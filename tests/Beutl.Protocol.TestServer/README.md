# Beutl.Protocol Test Server

SignalRトランスポートレイヤーの検証用テストサーバー。

## 実行方法

```bash
cd tests/Beutl.Protocol.TestServer
dotnet run

# 特定のポートで実行
dotnet run --urls "http://localhost:5001"
```

## エンドポイント

- `/` - ステータス確認
- `/sync` - SignalR Hubエンドポイント

## 動作確認

ブラウザで http://localhost:5000 にアクセスして、
「Beutl Protocol Test Server is running. Connect to /sync for SignalR.」
が表示されることを確認します。
