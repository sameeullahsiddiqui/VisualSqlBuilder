#!/bin/bash

# Build script for Visual SQL Builder
CONFIGURATION=${1:-Release}
DOCKER=${2:-false}
CLEAN=${3:-false}

echo -e "\033[32mVisual SQL Builder - Build Script\033[0m"
echo -e "\033[32m=================================\033[0m"

# Clean if requested
if [ "$CLEAN" = "true" ]; then
    echo -e "\033[33mCleaning solution...\033[0m"
    dotnet clean
    find . -type d -name "bin" -exec rm -rf {} + 2>/dev/null
    find . -type d -name "obj" -exec rm -rf {} + 2>/dev/null
fi

# Restore packages
echo -e "\033[33mRestoring packages...\033[0m"
dotnet restore

# Build solution
echo -e "\033[33mBuilding solution in $CONFIGURATION mode...\033[0m"
dotnet build --configuration $CONFIGURATION --no-restore

# Run tests if they exist
if [ -d "tests" ]; then
    echo -e "\033[33mRunning tests...\033[0m"
    dotnet test --configuration $CONFIGURATION --no-build --verbosity normal
fi

# Publish
echo -e "\033[33mPublishing application...\033[0m"
dotnet publish src/VisualSqlBuilder.Demo/VisualSqlBuilder.Demo.csproj \
    --configuration $CONFIGURATION \
    --output ./publish \
    --no-build

# Docker build if requested
if [ "$DOCKER" = "true" ]; then
    echo -e "\033[33mBuilding Docker image...\033[0m"
    docker build -f docker/Dockerfile -t visualsqlbuilder:latest .

    echo -e "\033[32mDocker image built successfully!\033[0m"
    echo -e "\033[36mRun with: docker-compose -f docker/docker-compose.yml up\033[0m"
fi

echo -e "\033[32mBuild completed successfully!\033[0m"