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
# ※ 各処理状況は Windows の MessageBox にてユーザーに通知します。

# Windows Forms を利用するための型を追加
Add-Type -AssemblyName System.Windows.Forms

# ユーザーへメッセージを表示する関数
function Show-Dialog {
    param (
        [string]$Message,
        [string]$Title = "アップデート"
    )
    [System.Windows.Forms.MessageBox]::Show($Message, $Title, [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}

# ユーザーへ Yes/No の問い合わせダイアログを表示する関数
function Prompt-YesNo {
    param (
        [string]$Message,
        [string]$Title = "アップデート完了"
    )
    return [System.Windows.Forms.MessageBox]::Show($Message, $Title, [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Question)
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
        Remove-Item -Path $LockDir -Recurse -Force
    }
}

# 引数チェック
if ($args.Count -lt 3) {
    Show-Dialog "Usage: win-update.ps1 <update_directory> <target_app_path> <app_process_name> [executable_path]"
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
Write-Host "ロックを取得中…"
if (Acquire-Lock -LockDir $lockDir -LockWaitTime $LOCK_WAIT_TIME) {
    Write-Host "ロックを取得しました。"
}
else {
    Show-Dialog "ロックの取得に失敗しました。"
    exit 1
}

# 既存アプリケーションのバックアップ作成（存在する場合）
if (Test-Path -Path $TARGET_APP_PATH -PathType Container) {
    $timestamp = Get-Date -Format "yyyyMMddHHmmss"
    $BACKUP_APP_PATH = "${TARGET_APP_PATH}_backup_$timestamp"
    Write-Host "現行アプリケーションのバックアップを作成: $BACKUP_APP_PATH"
    try {
        Copy-Item -Path $TARGET_APP_PATH -Destination $BACKUP_APP_PATH -Recurse -Force
    }
    catch {
        Show-Dialog "バックアップの作成に失敗しました。"
        Release-Lock -LockDir $lockDir
        exit 1
    }
    try {
        Remove-Item -Path $TARGET_APP_PATH -Recurse -Force
    }
    catch {
        Show-Dialog "現行アプリケーションの削除に失敗しました。"
        Release-Lock -LockDir $lockDir
        exit 1
    }
}

# 更新ファイルの配置
Write-Host "更新ファイルを配置中…"
try {
    Copy-Item -Path $UPDATE_DIR -Destination $TARGET_APP_PATH -Recurse -Force
}
catch {
    Show-Dialog "更新の配置に失敗しました。バックアップを復元します…"
    try {
        Copy-Item -Path $BACKUP_APP_PATH -Destination $TARGET_APP_PATH -Recurse -Force
    }
    catch {
        Show-Dialog "バックアップの復元に失敗しました。"
    }
    Release-Lock -LockDir $lockDir
    exit 1
}

# 配置成功なら、更新ディレクトリは不要なので削除
try {
    Remove-Item -Path $UPDATE_DIR -Recurse -Force
}
catch {
    Write-Host "更新ディレクトリの削除に失敗しました。"
}

# 正常時はバックアップも削除（※必要に応じて保持してください）
if (Test-Path -Path $BACKUP_APP_PATH) {
    try {
        Remove-Item -Path $BACKUP_APP_PATH -Recurse -Force
    }
    catch {
        Write-Host "バックアップディレクトリの削除に失敗しました。"
    }
}
Write-Host "更新が完了しました。"

# ロック解除
Release-Lock -LockDir $lockDir
Write-Host "ロックを解除しました。"

# ユーザーにアプリケーション起動の確認をする
$response = Prompt-YesNo "更新が完了しました。`nアプリケーションを起動しますか？"
if ($response -eq [System.Windows.Forms.DialogResult]::Yes) {
    Write-Host "アプリケーションを起動します…"
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
    Write-Host "アプリケーションは起動されませんでした。"
}

exit 0
