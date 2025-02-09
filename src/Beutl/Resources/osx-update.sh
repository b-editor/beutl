#!/bin/bash
# update.sh
# 使用方法: update.sh <update_directory> <target_app_path> <app_process_name>
#
# このスクリプトは以下の手順でアップデートを実施します。
# 1. 指定プロセス（APP_PROCESS_NAME）の終了を待機
# 2. ロックファイルを作成し排他制御
# 3. 現行アプリのバックアップ作成＆更新ファイルの配置
# 4. ロック解除
# 5. アプリケーション再起動
#
# ※ 各処理状況を AppleScript のダイアログでユーザーに通知します。

# 引数チェック
if [ "$#" -lt 3 ]; then
    osascript -e 'display dialog "Usage: osx-update.sh <update_directory> <target_app_path> <app_process_name>" buttons {"OK"} default button 1'
    exit 1
fi

UPDATE_DIR="${1%/}"
TARGET_APP_PATH="${2%/}"
APP_PROCESS_NAME="$3"
EXECUTABLE_PATH="$4"

# 更新ファイルのディレクトリが存在するか確認
if [ ! -d "$UPDATE_DIR" ]; then
    osascript -e 'display dialog "Error: Update directory does not exist." buttons {"OK"} default button 1'
    exit 1
fi

# AppleScript のダイアログを表示する関数
function show_dialog() {
    local message="$1"
    # ダブルクォート(")をエスケープしておく
    local safe_message
    safe_message=$(echo "$message" | sed 's/"/\\"/g')
    osascript -e "display dialog \"$safe_message\" with title \"アップデート\" buttons {\"OK\"} default button \"OK\""
}

# アプリケーション終了待機
# ※ループ内でのダイアログ表示はユーザー操作が必要となるため、
#    ここでは「待機中」の旨のメッセージは１回のみ表示し、
#    後続で終了後のメッセージを表示する方法としています。
sleep 3
while pgrep -x "$APP_PROCESS_NAME" >/dev/null; do
    show_dialog "アプリケーション ($APP_PROCESS_NAME) を終了してください（終了後OKをクリックしてください）"
done

# ロックファイルの設定
LOCKDIR="/tmp/${APP_PROCESS_NAME}_update.lock"
LOCK_WAIT_TIME=10  # ロック取得に失敗した場合の待機時間（秒）

acquire_lock() {
    local start_time=$(date +%s)
    while true; do
        if mkdir "$LOCKDIR" 2>/dev/null; then
            # ロックの取得に成功
            return 0
        fi
        # タイムアウトのチェック（例として10秒待機）
        local now=$(date +%s)
        if (( now - start_time >= LOCK_WAIT_TIME )); then
            return 1
        fi
        sleep 0.5
    done
}

release_lock() {
    rmdir "$LOCKDIR"
}

echo "ロックを取得中…"
# 排他ロックを取得
if acquire_lock; then
    echo "ロックを取得しました。"
else
    show_dialog "ロックの取得に失敗しました。"
    exit 1
fi

# 既存アプリケーションのバックアップ作成（失敗時のロールバック用）
BACKUP_APP_PATH="${TARGET_APP_PATH}_backup_$(date +%Y%m%d%H%M%S)"
if [ -d "$TARGET_APP_PATH" ]; then
    echo "現行アプリケーションのバックアップを作成: $BACKUP_APP_PATH"
    cp -R  "$TARGET_APP_PATH" "$BACKUP_APP_PATH"
    if [ $? -ne 0 ]; then
        show_dialog "バックアップの作成に失敗しました。"
        release_lock
        exit 1
    fi

    rm -rf "$TARGET_APP_PATH"
fi

# 更新ファイルの配置（ここでは単純にmvで更新ディレクトリを移動）
echo "更新ファイルを配置中…"
cp -R "$UPDATE_DIR" "$TARGET_APP_PATH"
if [ $? -ne 0 ]; then
    show_dialog "更新の配置に失敗しました。バックアップを復元します…"
    cp -R "$BACKUP_APP_PATH" "$TARGET_APP_PATH"
    release_lock
    exit 1
fi
# cpに成功したら、更新ディレクトリは不要なので削除
rm -rf "$UPDATE_DIR"

# ※ 必要に応じてバックアップは削除（今回は正常時は削除）
rm -rf "$BACKUP_APP_PATH"
echo "更新が完了しました。"

# ロック解除
release_lock
echo "ロックを解除しました。"

# ユーザーにアプリケーション起動の確認をする
USER_RESPONSE=$(osascript -e 'display dialog "更新が完了しました。\nアプリケーションを起動しますか？" buttons {"はい", "いいえ"} default button "はい" with title "アップデート完了"' -e 'button returned of result')

if [ "$USER_RESPONSE" = "はい" ]; then
    echo "アプリケーションを起動します…"
    if ! chmod +x "$EXECUTABLE_PATH"; then
        show_dialog "アプリケーションの実行権限の変更に失敗しました。"
        exit 1
    fi

    "$EXECUTABLE_PATH" &
else
    echo "アプリケーションは起動されませんでした。"
fi

exit 0
