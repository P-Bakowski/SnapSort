param(
    [switch]$BuildWorker,
    [string]$InnoCompiler = $env:INNO_COMPILER
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$props = [xml](Get-Content (Join-Path $root 'Directory.Build.props'))
$version = $props.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
$workerRoot = Join-Path $root 'python\PhotoAnalysis.Worker'
$workerExe = Join-Path $workerRoot 'dist\PhotoAnalysis.Worker\PhotoAnalysis.Worker.exe'
$publishDir = Join-Path $root 'artifacts\SnapSort-win-x64'
$releaseDir = Join-Path $root 'artifacts\release'
$stageDir = Join-Path $releaseDir 'zip'

if ($BuildWorker) {
    python -m PyInstaller --name PhotoAnalysis.Worker (Join-Path $workerRoot 'main.py') `
        --add-data "$(Join-Path $workerRoot 'models');models" `
        --hidden-import timm.models.mobilenetv3 --hidden-import safetensors.torch `
        --exclude-module sentence_transformers --exclude-module transformers `
        --exclude-module sklearn --exclude-module scipy --exclude-module tensorboard `
        --distpath (Join-Path $workerRoot 'dist') --workpath (Join-Path $workerRoot 'build') `
        --specpath $workerRoot --clean --noconfirm
}

if (-not (Test-Path -LiteralPath $workerExe)) {
    throw "Brak workera: $workerExe. Uruchom skrypt z parametrem -BuildWorker."
}

dotnet build (Join-Path $root 'SnapSort.sln') -c Release
dotnet run --project (Join-Path $root 'tests\SnapSort.SelfTests\SnapSort.SelfTests.csproj') -c Release
dotnet publish (Join-Path $root 'src\SnapSort.App\SnapSort.App.csproj') -c Release -r win-x64 --self-contained true -o $publishDir

$publishRoot = [IO.Path]::GetFullPath($publishDir).TrimEnd('\') + '\'
foreach ($unused in @('libvlc\win-arm64', 'libvlc\win-x86')) {
    $path = [IO.Path]::GetFullPath((Join-Path $publishDir $unused))
    if (-not $path.StartsWith($publishRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Nieprawidłowa ścieżka czyszczenia: $path"
    }
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
Get-ChildItem -LiteralPath $publishDir -Filter '*.pdb' -Recurse -File | Remove-Item -Force

if (-not $InnoCompiler) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'SnapSortDevTools\InnoSetup\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $InnoCompiler) { throw 'Nie znaleziono kompilatora Inno Setup (ISCC.exe).' }

& $InnoCompiler "/DAppVersion=$version" (Join-Path $root 'installer\setup.iss')
if ($LASTEXITCODE -ne 0) { throw "Kompilacja instalatora nie powiodła się (kod $LASTEXITCODE)." }

New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
Get-ChildItem -LiteralPath $stageDir -File | Remove-Item -Force
$setup = Join-Path $releaseDir "SnapSort_Setup_v$version.exe"
$zip = Join-Path $releaseDir "SnapSort_v$version.zip"
Copy-Item -LiteralPath $setup, (Join-Path $root 'README.md'), (Join-Path $root 'INSTALL.md') -Destination $stageDir
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zip -CompressionLevel Optimal

Write-Host "Gotowe: $setup"
Write-Host "Gotowe: $zip"
