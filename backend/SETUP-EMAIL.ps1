# SETUP-EMAIL.ps1 - Turn on REAL verification-code email for SmartLMS via Gmail SMTP.
# The Gmail app password is typed into this window and written straight into the
# dotnet user-secrets file on THIS machine only. It is never printed, never passed
# on any command line, never saved to any project file, and never leaves your
# computer. Safe to run again; it overwrites the same settings.
#
# Before running:
#   1. Turn on 2-Step Verification for the Gmail account (myaccount.google.com/security).
#   2. Create an app password at myaccount.google.com/apppasswords (16 letters).
#   3. If you ever pasted an app password into a chat, revoke it first and make a new one.
#
# Run from the repo root:  powershell -ExecutionPolicy Bypass -File backend\SETUP-EMAIL.ps1

$ErrorActionPreference = 'Stop'
$apiDir = Join-Path $PSScriptRoot 'src\SmartLMS.API'
Set-Location $apiDir

$gmail = Read-Host 'Gmail address that sends the codes (e.g. name@gmail.com)'
if ($gmail -notmatch '^\S+@\S+\.\S+$') { Write-Host 'That does not look like an email address. Stop.' -ForegroundColor Red; exit 1 }

$secure = Read-Host 'Gmail APP password (16 letters, NOT your Gmail password)' -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
$appPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
$appPassword = $appPassword -replace '\s', ''   # Google shows it with spaces; strip them
if ($appPassword.Length -ne 16) { Write-Host 'An app password is 16 letters. Stop.' -ForegroundColor Red; $appPassword = $null; exit 1 }

function Invoke-Dotnet([string[]]$CmdArgs) {
    & dotnet @CmdArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "dotnet $($CmdArgs -join ' ') failed (exit $LASTEXITCODE). Earlier settings may already be saved; fix the error above and run again (re-running overwrites the same settings)." -ForegroundColor Red
        exit 1
    }
}

function Read-SecretsFile([string]$Path) {
    $map = [ordered]@{}
    if (Test-Path $Path) {
        $obj = Get-Content $Path -Raw | ConvertFrom-Json
        foreach ($p in $obj.PSObject.Properties) { $map[$p.Name] = $p.Value }
    }
    return $map
}

try {
    # Non-secret settings go through the dotnet tool itself: this validates the SDK,
    # the project and its UserSecretsId, and creates the secrets file.
    Invoke-Dotnet @('user-secrets', 'set', 'Email:Enabled', 'true')
    Invoke-Dotnet @('user-secrets', 'set', 'Email:Host', 'smtp.gmail.com')
    Invoke-Dotnet @('user-secrets', 'set', 'Email:Port', '587')
    Invoke-Dotnet @('user-secrets', 'set', 'Email:UseSsl', 'true')
    Invoke-Dotnet @('user-secrets', 'set', 'Email:UserName', $gmail)
    Invoke-Dotnet @('user-secrets', 'set', 'Email:FromAddress', $gmail)
    Invoke-Dotnet @('user-secrets', 'set', 'Email:FromName', 'SmartLMS')

    # The app password and the intentionally EMPTY pickup-directory override are written
    # directly into the same secrets file the tool uses, so the password never appears
    # on any process command line. (An empty argument would also be dropped by Windows
    # PowerShell 5.1, so the empty override cannot go through the tool reliably.)
    $csproj = Join-Path $apiDir 'SmartLMS.API.csproj'
    $id = ([xml](Get-Content $csproj)).Project.PropertyGroup.UserSecretsId | Select-Object -First 1
    if (-not $id) { Write-Host 'UserSecretsId missing from SmartLMS.API.csproj. Stop.' -ForegroundColor Red; exit 1 }
    $secretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$id\secrets.json"
    $secrets = Read-SecretsFile $secretsPath
    $secrets['Email:Password'] = $appPassword
    $secrets['Email:PickupDirectory'] = ''   # a configured SMTP host wins over the dev outbox; this removes it outright
    $secrets | ConvertTo-Json | Set-Content $secretsPath -Encoding UTF8

    # Read the file back and confirm every key landed, without printing any value.
    $saved = Read-SecretsFile $secretsPath
    $expected = @('Email:Enabled', 'Email:Host', 'Email:Port', 'Email:UseSsl', 'Email:UserName', 'Email:Password', 'Email:FromAddress', 'Email:FromName', 'Email:PickupDirectory')
    $missing = @($expected | Where-Object { -not $saved.Contains($_) })
    if ($missing.Count -gt 0) { Write-Host ('These settings did not save: ' + ($missing -join ', ')) -ForegroundColor Red; exit 1 }
    if ([string]$saved['Email:Password'] -ne $appPassword) { Write-Host 'Saved app password does not match what was typed. Run again.' -ForegroundColor Red; exit 1 }
}
finally { $appPassword = $null }

Write-Host ''
Write-Host 'Email settings saved for SmartLMS.API (sender:' $gmail'). Nothing has been sent yet.' -ForegroundColor Green
Write-Host 'To confirm delivery works: restart the API (dotnet run), register a test account or resend a code, and check that inbox (and spam).'
Write-Host 'Gmail can take a minute and may hold the first messages; daily sending limits apply.'
