全てのメソッドにApiKeyのパラメーターを付ける

# 認証

## RefreshAuth
```
POST /api/refreshauth
```
トークンを更新します。

### Request body
``` Json
{
    "type": "refresh_token",
    "token": "リフレッシュトークン"
}
```

### Response body
``` Json
{
    "access_token": "アクセストークン",
    "expires_in": "このアクセストークンの有効期限 (秒数)",
    "refresh_token": "リフレッシュトークン",
}
```

## SignIn
```
POST /api/signin
```
サインインします。

### Request body
``` Json
{
    "email": "メールアドレス",
    "password": "パスワード"
}
```

### Response body
``` Json
{
    "access_token": "アクセストークン",
    "expires_in": "このアクセストークンの有効期限 (秒数)",
    "refresh_token": "リフレッシュトークン",
}
```

## SignUp
```
POST /api/signup
```
サインアップします。

### Request body
``` Json
{
    "email": "メールアドレス",
    "password": "パスワード",
    "displayname": "表示名"
}
```

### Response body
``` Json
{
    "access_token": "アクセストークン",
    "expires_in": "このアクセストークンの有効期限 (秒数)",
    "refresh_token": "リフレッシュトークン",
}
```

## GetAccountInfo
```
POST /api/getAccountInfo
```
アカウント情報を取得します。

### Request body
``` Json
{
    "access_token": "アクセストークン"
}
```

### Response body
``` Json
{
    "email": "メールアドレス",
    "displayname": "表示名"
}
```

## Update
```
POST /api/update
```
アカウント情報を更新します。

### Request body
``` Json
{
    "access_token": "アクセストークン",
    "email": "メールアドレス",
    "password": "パスワード",
    "displayname": "表示名"
}
```

### Response body
``` Json
{
    "access_token": "アクセストークン",
    "expires_in": "このアクセストークンの有効期限 (秒数)",
    "refresh_token": "リフレッシュトークン",
}
```

## DeleteAccount
```
POST /api/deleteAccount
```
アカウントを削除します。

### Request body
``` Json
{
    "access_token": "アクセストークン"
}
```

## SendPasswordResetEmail
```
POST /api/sendPasswordResetEmail
```
パスワードをリセットするメールを送信します。

### Request body
``` Json
{
    "email": "メールアドレス",
}
```

# パッケージ
## Upload
```
POST /api/upload?token={token}
```
パッケージをアップロードします。
* token: アクセストークン

### Request body
* パッケージファイル

### Response body
``` Json
{
    "version": "パッケージのバージョン (メジャー.マイナー.ビルド)",
    "download_url": "",
    "update_note": "更新ノート",
    "update_note_short": "短い更新ノート",
    "release_datetime": "公開した日時 (例[2021-05-30T00:00:00.0000000])"
}
```

## GetPackages
```
POST /api/getPackages
```
アップロードしたパッケージを取得します。

### Request body
``` Json
{
    "access_token": "アクセストークン"
}
```

### Response body
``` Json
[
    {
        "main_assembly": "",
        "name": "",
        "author": "",
        "homepage": "",
        "description_short": "",
        "description": "",
        "tag": "",
        "id": "",
        "license": "",
        "versions": [
            {
            "version": "",
            "download_url": "",
            "update_note": "",
            "update_note_short": "",
            "release_datetime": ""
            }
        ]
    }
]
```