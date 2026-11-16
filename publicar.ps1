# Genera los ejecutables de Bancada en la carpeta dist\:
#   dist\Bancada-windows-x64.exe   (un solo archivo, no necesita instalar nada)
#   dist\Bancada-linux-x64         (un solo archivo, no necesita instalar nada)
# Uso:  .\publicar.ps1
$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$proyecto = Join-Path $raiz 'src\Bancada\Bancada.csproj'
$dist = Join-Path $raiz 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

$comunes = @(
    '-c', 'Release', '--self-contained', 'true', '--nologo', '-v', 'q',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=none'
)

foreach ($rid in 'win-x64', 'linux-x64') {
    $salida = Join-Path $raiz "artifacts\$rid"
    Write-Host "Compilando $rid..."
    dotnet publish $proyecto -r $rid -o $salida @comunes
    if ($LASTEXITCODE -ne 0) { throw "Falló la compilación para $rid" }
    if ($rid -eq 'win-x64') {
        Copy-Item (Join-Path $salida 'Bancada.exe') (Join-Path $dist 'Bancada-windows-x64.exe') -Force
    } else {
        Copy-Item (Join-Path $salida 'Bancada') (Join-Path $dist 'Bancada-linux-x64') -Force
    }
}

Get-ChildItem $dist | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
