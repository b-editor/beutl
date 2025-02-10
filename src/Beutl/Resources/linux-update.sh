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
trap 'echo "@(Message.An_error_has_occurred_Terminate_script)" >&2; exit 1' ERR

# ユーザーにメッセージを表示し、Enterキー待ちで続行させる関数
prompt_continue() {
    local message="$1"
    echo "$message"
    read -r -p "@(Message.Press_Enter_to_continue)" dummy
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
    echo "@(Message.Error_Update_directory_does_not_exist)"
    exit 1
fi

# アプリケーション終了待機
sleep 3
while pgrep -x "$APP_PROCESS_NAME" >/dev/null; do
    prompt_continue "@(Message.Exit_the_application)@(Message.Press_the_Enter_key_when_finished)"
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

echo "@(Message.Acquiring_lock)"
if acquire_lock; then
    echo "@(Message.Lock_acquired)"
else
    echo "@(Message.Failed_to_acquire_lock)"
    exit 1
fi

# 既存アプリケーションのバックアップ作成（対象ディレクトリが存在する場合）
if [ -d "$TARGET_APP_PATH" ]; then
    TIMESTAMP=$(date +%Y%m%d%H%M%S)
    BACKUP_APP_PATH="${TARGET_APP_PATH}_backup_${TIMESTAMP}"
    echo "@(Message.Create_backup_of_current_application) $BACKUP_APP_PATH"
    cp -R "$TARGET_APP_PATH" "$BACKUP_APP_PATH"
    if [ $? -ne 0 ]; then
        echo "@(Message.Failed_to_create_backup)"
        release_lock
        exit 1
    fi
    rm -rf "$TARGET_APP_PATH"
fi

# 更新ファイルの配置
echo "@(Message.Updating_files_in_place)"
cp -R "$UPDATE_DIR" "$TARGET_APP_PATH"
if [ $? -ne 0 ]; then
    echo "@(Message.Update_placement_failed_Restore_backup)"
    cp -R "$BACKUP_APP_PATH" "$TARGET_APP_PATH"
    release_lock
    exit 1
fi

# 更新ディレクトリの削除
rm -rf "$UPDATE_DIR"

# 正常時はバックアップも削除（必要に応じてバックアップを保持してください）
rm -rf "$BACKUP_APP_PATH"
echo "@(Message.The_update_has_been_completed)"

# ロック解除
release_lock
echo "@(Message.Lock_released)"

# アプリケーション起動の確認
read -r -p "@(Message.Update_completed_yes_no)" USER_RESPONSE
if [ "$USER_RESPONSE" = "y" ] || [ "$USER_RESPONSE" = "Y" ]; then
    echo "@(Message.Launch_the_application)"
    if [ -n "$EXECUTABLE_PATH" ]; then
        chmod +x "$EXECUTABLE_PATH" || { echo "@(Message.Failed_to_change_application_execution_permissions)"; exit 1; }
        "$EXECUTABLE_PATH" &
    else
        echo "@(Message.The_application_execution_path_is_not_specified)"
    fi
else
    echo "@(Message.The_application_was_not_launched)"
fi

exit 0
