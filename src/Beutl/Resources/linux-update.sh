#!/bin/bash
# update.sh
# 使用方法: update.sh <update_directory> <target_app_path> <app_process_name> [executable_path]
#
# このスクリプトは以下の手順でアップデートを実施します：
# 1. 指定プロセス（app_process_name）の終了を待機
# 2. ロックディレクトリを作成し排他制御
# 3. 現行アプリのバックアップ作成＆更新ファイルの配置
# 4. ロック解除
# 5. アプリケーション再起動の確認後、起動
#
# ※ 本スクリプトでは、ダイアログ表示の代わりにターミナル上で入力を求める方式を採用しています。

# エラー発生時は即時終了する設定
set -e
trap 'echo "エラーが発生しました。スクリプトを終了します。" >&2; exit 1' ERR

# ユーザーにメッセージを表示し、Enterキー待ちで続行させる関数
prompt_continue() {
    local message="$1"
    echo "$message"
    read -r -p "Enterキーを押して続行してください..." dummy
}

# 引数チェック
if [ "$#" -lt 3 ]; then
    echo "Usage: $0 <update_directory> <target_app_path> <app_process_name> [executable_path]"
    exit 1
fi

# 引数の設定（末尾の '/' があれば除去）
UPDATE_DIR="${1%/}"
TARGET_APP_PATH="${2%/}"
APP_PROCESS_NAME="$3"
EXECUTABLE_PATH="$4"

# 更新用ディレクトリの存在確認
if [ ! -d "$UPDATE_DIR" ]; then
    echo "Error: 更新用ディレクトリが存在しません。"
    exit 1
fi

# アプリケーション終了待機
sleep 3
while pgrep -x "$APP_PROCESS_NAME" >/dev/null; do
    prompt_continue "アプリケーション ($APP_PROCESS_NAME) を終了してください。終了後、Enterキーを押してください。"
done

# ロックの取得
LOCKDIR="/tmp/${APP_PROCESS_NAME}_update.lock"
LOCK_WAIT_TIME=10  # ロック取得タイムアウト（秒）

acquire_lock() {
    local start_time
    start_time=$(date +%s)
    while true; do
        if mkdir "$LOCKDIR" 2>/dev/null; then
            # ロック取得成功
            return 0
        fi
        local now
        now=$(date +%s)
        if [ $(( now - start_time )) -ge "$LOCK_WAIT_TIME" ]; then
            return 1
        fi
        sleep 0.5
    done
}

release_lock() {
    rm -rf "$LOCKDIR"
}

echo "ロックを取得中..."
if acquire_lock; then
    echo "ロックを取得しました。"
else
    echo "ロックの取得に失敗しました。"
    exit 1
fi

# 既存アプリケーションのバックアップ作成（対象ディレクトリが存在する場合）
if [ -d "$TARGET_APP_PATH" ]; then
    TIMESTAMP=$(date +%Y%m%d%H%M%S)
    BACKUP_APP_PATH="${TARGET_APP_PATH}_backup_${TIMESTAMP}"
    echo "現行アプリケーションのバックアップを作成: $BACKUP_APP_PATH"
    cp -R "$TARGET_APP_PATH" "$BACKUP_APP_PATH"
    if [ $? -ne 0 ]; then
        echo "バックアップの作成に失敗しました。"
        release_lock
        exit 1
    fi
    rm -rf "$TARGET_APP_PATH"
fi

# 更新ファイルの配置
echo "更新ファイルを配置中..."
cp -R "$UPDATE_DIR" "$TARGET_APP_PATH"
if [ $? -ne 0 ]; then
    echo "更新の配置に失敗しました。バックアップを復元します..."
    cp -R "$BACKUP_APP_PATH" "$TARGET_APP_PATH"
    release_lock
    exit 1
fi

# 更新ディレクトリの削除
rm -rf "$UPDATE_DIR"

# 正常時はバックアップも削除（必要に応じてバックアップを保持してください）
rm -rf "$BACKUP_APP_PATH"
echo "更新が完了しました。"

# ロック解除
release_lock
echo "ロックを解除しました。"

# アプリケーション起動の確認
read -r -p "更新が完了しました。アプリケーションを起動しますか？ (y/n): " USER_RESPONSE
if [ "$USER_RESPONSE" = "y" ] || [ "$USER_RESPONSE" = "Y" ]; then
    echo "アプリケーションを起動します..."
    if [ -n "$EXECUTABLE_PATH" ]; then
        chmod +x "$EXECUTABLE_PATH" || { echo "アプリケーションの実行権限の変更に失敗しました。"; exit 1; }
        "$EXECUTABLE_PATH" &
    else
        echo "アプリケーションの実行パスが指定されていません。"
    fi
else
    echo "アプリケーションは起動されませんでした。"
fi

exit 0
