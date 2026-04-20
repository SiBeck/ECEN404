# Database Connection Test Script
# Run this from the PatientPortal directory

Write-Host "================================" -ForegroundColor Cyan
Write-Host "Database Connection Test" -ForegroundColor Cyan
Write-Host "================================`n" -ForegroundColor Cyan

# Test 1: Relative Path (from project root)
$relativePath = "..\BioMetrixDatabase\MedicalImaging.db"
Write-Host "Test 1: Relative Path" -ForegroundColor Yellow
Write-Host "  Path: $relativePath" -ForegroundColor Gray

if (Test-Path $relativePath) {
    $fileInfo = Get-Item $relativePath
    $sizeKB = [math]::Round($fileInfo.Length / 1KB, 2)
    Write-Host "  ✓ File Found!" -ForegroundColor Green
    Write-Host "  Size: $sizeKB KB" -ForegroundColor Gray
    Write-Host "  Full Path: $($fileInfo.FullName)" -ForegroundColor Gray
    Write-Host "  Last Modified: $($fileInfo.LastWriteTime)" -ForegroundColor Gray
} else {
    Write-Host "  ✗ File NOT Found!" -ForegroundColor Red
}

Write-Host ""

# Test 2: Absolute Path
$absolutePath = "C:\Users\simon\source\repos\403DesktopApp\BioMetrixDatabase\MedicalImaging.db"
Write-Host "Test 2: Absolute Path" -ForegroundColor Yellow
Write-Host "  Path: $absolutePath" -ForegroundColor Gray

if (Test-Path $absolutePath) {
    $fileInfo = Get-Item $absolutePath
    $sizeKB = [math]::Round($fileInfo.Length / 1KB, 2)
    Write-Host "  ✓ File Found!" -ForegroundColor Green
    Write-Host "  Size: $sizeKB KB" -ForegroundColor Gray
} else {
    Write-Host "  ✗ File NOT Found!" -ForegroundColor Red
    Write-Host "  Update the path in appsettings.Development.json" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Run: dotnet run" -ForegroundColor White
Write-Host "  2. Check console for 'DATABASE CONNECTION CHECK'" -ForegroundColor White
Write-Host "  3. Look for 'File Exists: True'" -ForegroundColor White
Write-Host "================================" -ForegroundColor Cyan
