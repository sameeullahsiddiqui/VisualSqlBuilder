# Build script for Visual SQL Builder
param(
    [string]$Configuration = "Release",
    [switch]$Docker,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

Write-Host "Visual SQL Builder - Build Script" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green

# Clean if requested
if ($Clean) {
    Write-Host "Cleaning solution..." -ForegroundColor Yellow
    dotnet clean
    Remove-Item -Path "*/bin" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path "*/obj" -Recurse -Force -ErrorAction SilentlyContinue
}

# Restore packages
Write-Host "Restoring packages..." -ForegroundColor Yellow
dotnet restore

# Build solution
Write-Host "Building solution in $Configuration mode..." -ForegroundColor Yellow
dotnet build --configuration $Configuration --no-restore

# Run tests if they exist
if (Test-Path "tests") {
    Write-Host "Running tests..." -ForegroundColor Yellow
    dotnet test --configuration $Configuration --no-build --verbosity normal
}

# Publish
Write-Host "Publishing application..." -ForegroundColor Yellow
dotnet publish src/VisualSqlBuilder.Demo/VisualSqlBuilder.Demo.csproj `
    --configuration $Configuration `
    --output ./publish `
    --no-build

# Docker build if requested
if ($Docker) {
    Write-Host "Building Docker image..." -ForegroundColor Yellow
    docker build -f docker/Dockerfile -t visualsqlbuilder:latest .

    Write-Host "Docker image built successfully!" -ForegroundColor Green
    Write-Host "Run with: docker-compose -f docker/docker-compose.yml up" -ForegroundColor Cyan
}

Write-Host "Build completed successfully!" -ForegroundColor Green