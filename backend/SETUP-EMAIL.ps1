# SETUP-EMAIL.ps1 - Turn on REAL verification-code email for SmartLMS via Gmail SMTP.
# The Gmail app password is typed into this window and written to dotnet user-secrets
# on THIS machine only. It is never printed, never saved to any project file, and
# never leaves your computer. Safe to run again; it overwrites the same settings.
#
# Before running:
#   1. Turn on 2-Step Verification for the Gmail account (myaccount.google.com/security).
#   2. Create an app password at myaccount.google.com/apppasswords (16 letters).
#   3. If you ever pasted an app password into a chat, revoke it first and make a new one.
#
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File backend\SETUP-EMAIL.ps1

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot 'src\SmartLMS.API')

$gmail = Read-Host 'Gmail address that sends the codes (e.g. name@gmail.com)'
if ($gmail -notmatch '^\S+@\S+\.\S+$') { Write-Host 'That does not look like an email address. Stop.' -ForegroundColor Red; exit 1 }

$secure = Read-Host 'Gmail APP password (16 letters, NOT your Gmail password)' -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
$appPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
$appPassword = $appPassword -replace '\s', ''   # Google shows it with spaces; strip them
if ($appPassword.Length -ne 16) { Write-Host 'An app password is 16 letters. Stop.' -ForegroundColor Red; $appPassword = $null; exit 1 }

try {
    dotnet user-secrets set 'Email:Enabled' 'true'
    dotnet user-secrets set 'Email:Host' 'smtp.gmail.com'
    dotnet user-secrets set 'Email:Port' '587'
    dotnet user-secrets set 'Email:UseSsl' 'true'
    dotnet user-secrets set 'Email:UserName' $gmail
    dotnet user-secrets set 'Email:Password' $appPassword
    dotnet user-secrets set 'Email:FromAddress' $gmail
    dotnet user-secrets set 'Email:FromName' 'SmartLMS'
    # A configured SMTP host wins over the development local outbox; clearing the pickup
    # directory too, so Development cannot silently keep writing local files.
    dotnet user-secrets set 'Email:PickupDirectory' ''
}
finally { $appPassword = $null }

Write-Host ''
Write-Host 'Done. Verification codes will now be emailed from' $gmail -ForegroundColor Green
Write-Host 'Restart the API (dotnet run), register a test account, and check that inbox (and spam).'
Write-Host 'Gmail can take a minute and may hold the first messages; daily sending limits apply.'
