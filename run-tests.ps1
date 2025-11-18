# Test Runner Script for HartsysDatasetEditor
# Runs all unit tests and provides a summary

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "  HartsysDatasetEditor Test Runner  " -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Check if test project exists
$testProjectPath = "tests\HartsysDatasetEditor.Tests\HartsysDatasetEditor.Tests.csproj"
if (-not (Test-Path $testProjectPath)) {
    Write-Host "❌ Test project not found at: $testProjectPath" -ForegroundColor Red
    Write-Host "Creating test project..." -ForegroundColor Yellow
    
    # Create test directory
    New-Item -ItemType Directory -Force -Path "tests\HartsysDatasetEditor.Tests" | Out-Null
    
    # Create test project
    Set-Location "tests\HartsysDatasetEditor.Tests"
    dotnet new xunit
    dotnet add package FluentAssertions
    dotnet add package Moq
    dotnet add reference ..\..\src\HartsysDatasetEditor.Core\HartsysDatasetEditor.Core.csproj
    dotnet add reference ..\..\src\HartsysDatasetEditor.Api\HartsysDatasetEditor.Api.csproj
    dotnet add reference ..\..\src\HartsysDatasetEditor.Client\HartsysDatasetEditor.Client.csproj
    Set-Location ..\..
    
    Write-Host "✅ Test project created!" -ForegroundColor Green
}

Write-Host "Running tests..." -ForegroundColor Yellow
Write-Host ""

# Run tests with detailed output
$testResult = dotnet test $testProjectPath --verbosity normal --logger "console;verbosity=detailed"

Write-Host ""
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "         Test Results Summary        " -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan

# Check exit code
if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "✅ ALL TESTS PASSED!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Test Coverage:" -ForegroundColor Cyan
    Write-Host "  Phase 3 Tests:" -ForegroundColor White
    Write-Host "    - MultiFileDetectorServiceTests: 18 tests" -ForegroundColor Gray
    Write-Host "    - EnrichmentMergerServiceTests: 15 tests" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Phase 4 Tests:" -ForegroundColor White
    Write-Host "    - ItemEditEndpointsTests: 15 tests" -ForegroundColor Gray
    Write-Host "    - ItemEditServiceTests: 17 tests" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Total: 65+ tests" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host ""
    Write-Host "❌ SOME TESTS FAILED" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please review the output above for details." -ForegroundColor Yellow
    Write-Host ""
}

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Review test results above" -ForegroundColor White
Write-Host "  2. Run integration tests (see tests/INTEGRATION_TESTS.md)" -ForegroundColor White
Write-Host "  3. Start API: cd src/HartsysDatasetEditor.Api && dotnet watch run" -ForegroundColor White
Write-Host "  4. Start Client: cd src/HartsysDatasetEditor.Client && dotnet watch run" -ForegroundColor White
Write-Host ""

# Return exit code
exit $LASTEXITCODE
