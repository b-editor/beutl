# update.ps1
# 使用方法: update.ps1 <update_directory> <target_app_path> <app_process_name> [executable_path]
#
# このスクリプトは以下の手順でアップデートを実施します。
# 1. 指定プロセス（app_process_name）の終了を待機
# 2. ロックディレクトリを作成し排他制御
# 3. 現行アプリのバックアップ作成＆更新ファイルの配置
# 4. ロック解除
# 5. アプリケーション再起動の確認後、起動
#
# ※ 各処理状況は Windows Forms の MessageBox を利用してユーザーに通知します。
# ※ このスクリプトは Windows PowerShell（PowerShell Core ではなく）向けです。

# --- 基本設定 ---
$ErrorActionPreference = "Stop"  # エラー発生時に例外として処理

# ログ出力用関数（必要に応じてログファイルへの出力に変更可能）
function Write-Log {
    param ([string]$message)
    Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'): $message"
}

# Windows Forms を利用するためのアセンブリ読み込み
Add-Type -AssemblyName System.Windows.Forms

# ユーザーへメッセージを表示する関数
function Show-Dialog {
    param (
        [string]$Message,
        [string]$Title = "@(Strings.Update)"
    )
    [System.Windows.Forms.MessageBox]::Show($Message, $Title, `
        [System.Windows.Forms.MessageBoxButtons]::OK, `
        [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}

# ユーザーへ Yes/No の問い合わせダイアログを表示する関数
function Prompt-YesNo {
    param (
        [string]$Message,
        [string]$Title = "@(Strings.Update)"
    )
    return [System.Windows.Forms.MessageBox]::Show($Message, $Title, `
        [System.Windows.Forms.MessageBoxButtons]::YesNo, `
        [System.Windows.Forms.MessageBoxIcon]::Question)
}

# ロック取得用の関数
function Acquire-Lock {
    param (
        [string]$LockDir,
        [int]$LockWaitTime = 10  # 待機秒数
    )
    $startTime = Get-Date
    while ($true) {
        try {
            New-Item -Path $LockDir -ItemType Directory -ErrorAction Stop | Out-Null
            # ロック取得成功
            return $true
        }
        catch {
            # タイムアウト判定
            $now = Get-Date
            if (($now - $startTime).TotalSeconds -ge $LockWaitTime) {
                return $false
            }
            Start-Sleep -Milliseconds 500
        }
    }
}

# ロック解除用の関数
function Release-Lock {
    param (
        [string]$LockDir
    )
    if (Test-Path $LockDir) {
        try {
            Remove-Item -Path $LockDir -Recurse -Force
        }
        catch {
            Write-Log "Failed to delete locked directory: $_"
        }
    }
}

try {
    # 引数チェック
    if ($args.Count -lt 3) {
        Show-Dialog "Usage: update.ps1 <update_directory> <target_app_path> <app_process_name> [executable_path]"
        exit 1
    }

    # 引数の設定（末尾の '\' や '/' を削除）
    $UPDATE_DIR      = $args[0].TrimEnd('\', '/')
    $TARGET_APP_PATH = $args[1].TrimEnd('\', '/')
    $APP_PROCESS_NAME = $args[2]
    $EXECUTABLE_PATH = if ($args.Count -ge 4) { $args[3] } else { "" }

    # 更新ファイルのディレクトリが存在するか確認
    if (-not (Test-Path -Path $UPDATE_DIR -PathType Container)) {
        Show-Dialog "Error: Update directory does not exist."
        exit 1
    }

    # アプリケーション終了待機
    Start-Sleep -Seconds 3
    while (Get-Process -Name $APP_PROCESS_NAME -ErrorAction SilentlyContinue) {
        Show-Dialog "@(Message.Exit_the_application) (@(Message.Click_OK_after_completion))"
    }

    # ロックの取得（テンポラリフォルダ内にロック用ディレクトリを作成）
    $lockDir = Join-Path ([System.IO.Path]::GetTempPath()) "${APP_PROCESS_NAME}_update.lock"
    $LOCK_WAIT_TIME = 10
    Write-Log "@(Message.Acquiring_lock)"
    if (Acquire-Lock -LockDir $lockDir -LockWaitTime $LOCK_WAIT_TIME) {
        Write-Log "@(Message.Lock_acquired)"
    }
    else {
        Show-Dialog "@(Message.Failed_to_acquire_lock)"
        exit 1
    }

    # 既存アプリケーションのバックアップ作成（存在する場合）
    if (Test-Path -Path $TARGET_APP_PATH -PathType Container) {
        $timestamp = Get-Date -Format "yyyyMMddHHmmss"
        $BACKUP_APP_PATH = "${TARGET_APP_PATH}_backup_$timestamp"
        Write-Log "@(Message.Create_backup_of_current_application) $BACKUP_APP_PATH"
        try {
            Move-Item -Path $TARGET_APP_PATH -Destination $BACKUP_APP_PATH -Force
        }
        catch {
            Show-Dialog "@(Message.Failed_to_create_backup)"
            Release-Lock -LockDir $lockDir
            exit 1
        }
    }

    # 更新ファイルの配置
    Write-Log "@(Message.Updating_files_in_place)"
    try {
        Move-Item -Path $UPDATE_DIR -Destination $TARGET_APP_PATH -Force
    }
    catch {
        Show-Dialog "@(Message.Update_placement_failed_Restore_backup)"
        try {
            Move-Item -Path $BACKUP_APP_PATH -Destination $TARGET_APP_PATH -Force
        }
        catch {
            Show-Dialog "@(Message.Failed_to_restore_backup)"
        }
        Release-Lock -LockDir $lockDir
        exit 1
    }

    # 正常時はバックアップも削除
    if (Test-Path -Path $BACKUP_APP_PATH) {
        try {
            Remove-Item -Path $BACKUP_APP_PATH -Recurse -Force
        }
        catch {
            Write-Log "@(Message.Failed_to_delete_backup)"
        }
    }
    Write-Log "@(Message.The_update_has_been_completed)"

    # ロック解除
    Release-Lock -LockDir $lockDir
    Write-Log "@(Message.Lock_released)"

    # ユーザーにアプリケーション起動の確認をする
    $response = Prompt-YesNo "@(Message.Update_completed_Do_you_want_to_start_the_ߋn_application)"
    if ($response -eq [System.Windows.Forms.DialogResult]::Yes) {
        Write-Log "@(Message.Launch_the_application)"
        if (![string]::IsNullOrEmpty($EXECUTABLE_PATH)) {
            Start-Process -FilePath $EXECUTABLE_PATH
        }
        else {
            Show-Dialog "@(Message.The_application_execution_path_is_not_specified)"
        }
    }
    else {
        Write-Log "@(Message.The_application_was_not_launched)"
    }

    exit 0
}
catch {
    Show-Dialog "@(Message.An_error_has_occurred_Terminate_script)`n $_"
    exit 1
}
