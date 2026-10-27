# Installs (or updates) the Zenith service. Run from an elevated PowerShell:
#   powershell -ExecutionPolicy Bypass -File .\instalar-servico.ps1 -ExePath C:\caminho\zenith-service.exe
param(
    [Parameter(Mandatory = $true)][string]$ExePath
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Abra o PowerShell como administrador para instalar o serviço.'
}

$source = (Resolve-Path $ExePath).Path
$installDir = Join-Path $env:ProgramFiles 'Zenith'
$target = Join-Path $installDir 'zenith-service.exe'

$existing = Get-Service -Name 'Zenith' -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name 'Zenith' }
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -Force $source $target

if (-not $existing) {
    New-Service -Name 'Zenith' -BinaryPathName "`"$target`"" -DisplayName 'Zenith' `
        -Description 'Núcleo do Zenith: aplica e desfaz configurações e guarda o diário de alterações.' `
        -StartupType Automatic | Out-Null
}

Start-Service -Name 'Zenith'
Get-Service -Name 'Zenith'
