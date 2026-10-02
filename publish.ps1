<#
    Genera la descarga de M0DV0IC3 para Windows x64:
      publish\M0DV0IC3\                            la app (M0DV0IC3.exe y sus DLL), autocontenida: no hace falta instalar .NET
      publish\M0DV0IC3-v<versión>-win-x64.zip        esa carpeta comprimida, para GitHub Releases
      publish\M0DV0IC3-v<versión>-win-x64.zip.sha256 su huella SHA-256, para comprobar la descarga

    No es un único .exe comprimido a propósito. Un ejecutable que se descomprime solo y suelta DLL en la carpeta
    temporal es lo que más falsos positivos da en los antivirus. Así, casi todo lo que va dentro son DLL de .NET
    firmadas por Microsoft.
    ReadyToRun precompila el código para evitar tirones del JIT en el hilo de audio al arrancar.

    Para firmar antes de comprimir (lo hace la GitHub Action de releases):
      .\publish.ps1 -NoPack      publica la carpeta
      (firmar publish\M0DV0IC3)
      .\publish.ps1 -PackOnly    comprime la carpeta ya firmada
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot 'publish'),
    # Por defecto, la de Directory.Build.props.
    [string]$Version,
    [switch]$SkipTests,
    [switch]$NoPack,
    [switch]$PackOnly
)

$ErrorActionPreference = 'Stop'
$appProject = Join-Path $PSScriptRoot 'src\M0DV0IC3.App'
$appFolder = Join-Path $Output 'M0DV0IC3'
if (-not $Version) { $Version = (dotnet msbuild $appProject -getProperty:Version).Trim() }

if (-not $PackOnly) {
    $running = Get-Process -Name M0DV0IC3 -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Output, 'OrdinalIgnoreCase') }
    if ($running) { throw "Cierra M0DV0IC3 (PID $($running.Id -join ', ')) antes de publicar: se ejecuta desde $Output." }

    if (-not $SkipTests) {
        dotnet test (Join-Path $PSScriptRoot 'tests\M0DV0IC3.Tests') -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Los tests han fallado; no se publica.' }
    }

    if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
    dotnet publish $appProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishReadyToRun=true `
        -p:DebugType=embedded `
        -p:Version=$Version `
        -o $appFolder
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish ha fallado.' }

    Copy-Item (Join-Path $PSScriptRoot 'LICENSE') (Join-Path $appFolder 'LICENSE.txt')
    Copy-Item (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') (Join-Path $appFolder 'THIRD-PARTY-NOTICES.txt')
    Copy-Item (Join-Path $PSScriptRoot 'docs\LEEME.txt') (Join-Path $appFolder 'LEEME.txt')
}

if (-not $NoPack) {
    if (-not (Test-Path (Join-Path $appFolder 'M0DV0IC3.exe'))) { throw "No hay nada publicado en $appFolder." }
    $zip = Join-Path $Output "M0DV0IC3-v$Version-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $appFolder -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii

    $files = (Get-ChildItem $appFolder -Recurse -File).Count
    Write-Host ("Carpeta: {0} ({1} archivos)" -f $appFolder, $files)
    Write-Host ("Descarga: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
    Write-Host "SHA-256: $hash"
}
