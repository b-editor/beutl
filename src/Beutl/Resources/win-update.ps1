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
        [string]$Title = "アップデート"
    )
    [System.Windows.Forms.MessageBox]::Show($Message, $Title, `
        [System.Windows.Forms.MessageBoxButtons]::OK, `
        [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}

# ユーザーへ Yes/No の問い合わせダイアログを表示する関数
function Prompt-YesNo {
    param (
        [string]$Message,
        [string]$Title = "アップデート完了"
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
            Write-Log "ロックディレクトリの削除に失敗しました: $_"
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
        Show-Dialog "アプリケーション ($APP_PROCESS_NAME) を終了してください（終了後OKをクリックしてください）"
    }

    # ロックの取得（テンポラリフォルダ内にロック用ディレクトリを作成）
    $lockDir = Join-Path ([System.IO.Path]::GetTempPath()) "${APP_PROCESS_NAME}_update.lock"
    $LOCK_WAIT_TIME = 10
    Write-Log "ロックを取得中…"
    if (Acquire-Lock -LockDir $lockDir -LockWaitTime $LOCK_WAIT_TIME) {
        Write-Log "ロックを取得しました。"
    }
    else {
        Show-Dialog "ロックの取得に失敗しました。"
        exit 1
    }

    # 既存アプリケーションのバックアップ作成（存在する場合）
    if (Test-Path -Path $TARGET_APP_PATH -PathType Container) {
        $timestamp = Get-Date -Format "yyyyMMddHHmmss"
        $BACKUP_APP_PATH = "${TARGET_APP_PATH}_backup_$timestamp"
        Write-Log "現行アプリケーションのバックアップを作成: $BACKUP_APP_PATH"
        try {
            Move-Item -Path $TARGET_APP_PATH -Destination $BACKUP_APP_PATH -Force
        }
        catch {
            Show-Dialog "バックアップの作成に失敗しました。"
            Release-Lock -LockDir $lockDir
            exit 1
        }
    }

    # 更新ファイルの配置
    Write-Log "更新ファイルを配置中…"
    try {
        Move-Item -Path $UPDATE_DIR -Destination $TARGET_APP_PATH -Force
    }
    catch {
        Show-Dialog "更新の配置に失敗しました。バックアップを復元します…"
        try {
            Move-Item -Path $BACKUP_APP_PATH -Destination $TARGET_APP_PATH -Force
        }
        catch {
            Show-Dialog "バックアップの復元に失敗しました。"
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
            Write-Log "バックアップディレクトリの削除に失敗しました。"
        }
    }
    Write-Log "更新が完了しました。"

    # ロック解除
    Release-Lock -LockDir $lockDir
    Write-Log "ロックを解除しました。"

    # ユーザーにアプリケーション起動の確認をする
    $response = Prompt-YesNo "更新が完了しました。`nアプリケーションを起動しますか？"
    if ($response -eq [System.Windows.Forms.DialogResult]::Yes) {
        Write-Log "アプリケーションを起動します…"
        if (![string]::IsNullOrEmpty($EXECUTABLE_PATH)) {
            if (-not (Test-Path -Path $EXECUTABLE_PATH)) {
                Show-Dialog "アプリケーションの実行ファイルが見つかりません。"
                exit 1
            }
            Start-Process -FilePath $EXECUTABLE_PATH
        }
        else {
            Show-Dialog "アプリケーションの実行パスが指定されていません。"
        }
    }
    else {
        Write-Log "アプリケーションは起動されませんでした。"
    }

    exit 0
}
catch {
    Show-Dialog "スクリプト実行中にエラーが発生しました。`nエラー内容: $_"
    exit 1
}
