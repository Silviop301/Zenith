# Removes the Zenith service. The change journal in %ProgramData%\Zenith is kept.
# Run "zenith-probe desfazer-tudo" first if you want every change undone.
$ErrorActionPreference = 'Stop'

if (Get-Service -Name 'Zenith' -ErrorAction SilentlyContinue) {
    Stop-Service -Name 'Zenith' -ErrorAction SilentlyContinue
    sc.exe delete Zenith | Out-Null
}
Remove-Item -Recurse -Force (Join-Path $env:ProgramFiles 'Zenith') -ErrorAction SilentlyContinue
Write-Output 'Serviço do Zenith removido. O diário continua em %ProgramData%\Zenith.'
