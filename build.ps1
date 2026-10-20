# Builds bin\OfficeLockChecker.exe with the C# compiler that ships with the .NET Framework in Windows.
$ErrorActionPreference = 'Stop'

$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $compilerPath)) {
    throw "The .NET Framework 4 C# compiler was not found under $env:WINDIR\Microsoft.NET."
}

$iconPath = Join-Path $PSScriptRoot 'assets\app.ico'
if (-not (Test-Path $iconPath)) {
    & (Join-Path $PSScriptRoot 'assets\New-AppIcon.ps1') -OutputPath $iconPath
}

$outputDirectory = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$outputPath = Join-Path $outputDirectory 'OfficeLockChecker.exe'
$manifestPath = Join-Path $PSScriptRoot 'src\app.manifest'
$sourcePaths = Get-ChildItem -Path (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
# XP caption button images, regenerated with assets\Convert-XpCaptionButtons.ps1.
$captionResources = Get-ChildItem -Path (Join-Path $PSScriptRoot 'assets\caption') -Filter '*.png' |
    ForEach-Object { "/resource:$($_.FullName),OfficeLockChecker.caption.$($_.Name)" }

& $compilerPath /nologo /target:winexe /optimize+ /warnaserror+ `
    "/out:$outputPath" "/win32icon:$iconPath" "/win32manifest:$manifestPath" `
    "/resource:$iconPath,OfficeLockChecker.app.ico" $captionResources `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    $sourcePaths
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}
Write-Host "Built $outputPath"
