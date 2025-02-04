param (
    [string]$Configuration = "Release"
)

$publishPath = "bin\$Configuration\net8.0-windows\publish"
Write-Host "Building Topiary in $Configuration configuration..."

if (Test-Path $publishPath) {
    Remove-Item -Recurse -Force $publishPath
}
if (Test-Path "installer") {
    Remove-Item -Recurse -Force "installer"
}

Write-Host "Restoring dependencies..."
dotnet restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to restore dependencies" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Publishing application..."
dotnet publish -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to build project" -ForegroundColor Red
    exit $LASTEXITCODE
}

if (!(Test-Path $publishPath)) {
    Write-Host "Publish directory not found at: $publishPath" -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Force -Path "installer"

$innoSetupCompiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (!(Test-Path $innoSetupCompiler)) {
    Write-Host "Inno Setup 6 not found. Please install it from https://jrsoftware.org/isdl.php" -ForegroundColor Red
    exit 1
}

if (!(Test-Path "LICENSE.txt")) {
    @"
MIT License

Copyright (c) $(Get-Date -Format yyyy)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files.
"@ | Out-File -FilePath "LICENSE.txt" -Encoding UTF8
}

Write-Host "Building installer..."
& $innoSetupCompiler "installer.iss"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to create installer" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "Installer can be found in the 'installer' directory."