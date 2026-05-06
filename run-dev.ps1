$ErrorActionPreference = "Stop"

Set-Location -LiteralPath $PSScriptRoot
$Host.UI.RawUI.WindowTitle = "OurPlaneCore Dev Build"

Write-Host "Building OurPlaneCore..." -ForegroundColor Cyan
dotnet build .\ourplanecore.sln
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Build failed. Fix errors above, then run this shortcut again." -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Build OK. Starting app..." -ForegroundColor Green
dotnet run --project .\ourplanecore.csproj
