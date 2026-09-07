$ErrorActionPreference = "Stop"

Set-Location -LiteralPath $PSScriptRoot
$Host.UI.RawUI.WindowTitle = "OurPlanCore Dev Build"

Write-Host "Building OurPlanCore..." -ForegroundColor Cyan
dotnet build .\ourplancore.sln
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Build failed. Fix errors above, then run this shortcut again." -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Build OK. Starting app..." -ForegroundColor Green
dotnet run --project .\ourplancore.csproj
