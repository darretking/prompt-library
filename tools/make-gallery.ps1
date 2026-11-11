# Renders every Prompt Library screen offscreen (light + dark, 100% + 150%) to artifacts/gallery as PNGs.
# Never starts the real app and never touches %APPDATA%\PromptLibrary.
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts\gallery'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
$env:PL_SCREENSHOTS = $out
dotnet test (Join-Path $root 'tests\PromptLibrary.App.Tests') --filter "FullyQualifiedName~ScreenshotGallery"
$count = (Get-ChildItem $out -Recurse -Filter *.png | Measure-Object).Count
Write-Host "$count PNG files in $out"
