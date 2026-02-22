param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("close","build","deploy","launch","status")]
    [string]$Step
)

$ErrorActionPreference = "Stop"

# ============================================================
# Configuration — edit these paths when versions change
# ============================================================
$MendixVersion      = "10.24.13.86719"
$MendixBase         = "C:\Users\tomekgolembiewski\AppData\Local\Programs\Mendix\$MendixVersion\modeler"
$StudioProExe       = "$MendixBase\studiopro.exe"
$ExtensionsDest     = "$MendixBase\Extensions\AideLite"

$ProjectDir         = "C:\Users\tomekgolembiewski\ClaudeWinProjects\AIDE-Lite"
$CsprojPath         = "$ProjectDir\src\AideLite.csproj"
$BuildOutput        = "$ProjectDir\src\bin\Release\net8.0-windows"

$MendixProjectPath  = "C:\MendixProjects\LEA-development-current\Zeiterfassung 2024.mpr"
$LockFile           = "$MendixProjectPath.lock"

# ============================================================
# Step functions
# ============================================================

function Step-Close {
    Write-Host "=== Closing Studio Pro ===" -ForegroundColor Cyan

    $sp = Get-Process -Name "studiopro" -ErrorAction SilentlyContinue
    if ($sp) {
        Write-Host "Stopping Studio Pro (PID: $($sp.Id))..." -ForegroundColor Yellow
        $sp | Stop-Process -Force
        Start-Sleep -Seconds 3

        # Verify it's gone
        $check = Get-Process -Name "studiopro" -ErrorAction SilentlyContinue
        if ($check) {
            Write-Host "ERROR: Studio Pro still running after Stop-Process" -ForegroundColor Red
            exit 1
        }
        Write-Host "Studio Pro stopped." -ForegroundColor Green
    } else {
        Write-Host "Studio Pro is not running." -ForegroundColor Gray
    }

    # Kill orphaned Java runtime holding port 8090 (Mendix app server)
    try {
        $portHolders = netstat -ano 2>$null | Select-String ":8090\s" | ForEach-Object {
            if ($_ -match '\s(\d+)\s*$') { [int]$Matches[1] }
        } | Where-Object { $_ -ne 0 } | Sort-Object -Unique
        foreach ($procId in $portHolders) {
            $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
            if ($proc) {
                Write-Host "Killing orphaned process on port 8090: $($proc.ProcessName) (PID: $procId)" -ForegroundColor Yellow
                Stop-Process -Id $procId -Force
            }
        }
    } catch {
        # Port check is best-effort — don't fail the build
    }

    if (Test-Path $LockFile) {
        Remove-Item $LockFile -Force
        Write-Host "Removed lock file." -ForegroundColor Gray
    }
}

function Step-Build {
    Write-Host "=== Building AIDE-Lite ===" -ForegroundColor Cyan
    Write-Host "Project: $CsprojPath" -ForegroundColor Gray

    Push-Location $ProjectDir
    try {
        dotnet build src\AideLite.csproj -c Release
        if ($LASTEXITCODE -ne 0) {
            Write-Host "BUILD FAILED (exit code $LASTEXITCODE)" -ForegroundColor Red
            exit 1
        }
        Write-Host "Build succeeded." -ForegroundColor Green

        # Show output summary
        $dll = Join-Path $BuildOutput "AideLite.dll"
        if (Test-Path $dll) {
            $info = Get-Item $dll
            $sizeKB = [math]::Round($info.Length / 1024)
            Write-Host "Output: AideLite.dll ($sizeKB KB, $($info.LastWriteTime))" -ForegroundColor Gray
        }
    } finally {
        Pop-Location
    }
}

function Step-Deploy {
    Write-Host "=== Deploying to Extensions ===" -ForegroundColor Cyan
    Write-Host "Source: $BuildOutput" -ForegroundColor Gray
    Write-Host "Target: $ExtensionsDest" -ForegroundColor Gray

    # Verify build output exists
    $dll = Join-Path $BuildOutput "AideLite.dll"
    if (!(Test-Path $dll)) {
        Write-Host "ERROR: Build output not found at $dll" -ForegroundColor Red
        Write-Host "Run the 'build' step first." -ForegroundColor Yellow
        exit 1
    }

    # Clean and recreate destination
    if (Test-Path $ExtensionsDest) {
        Remove-Item $ExtensionsDest -Recurse -Force
    }
    New-Item -ItemType Directory -Path $ExtensionsDest -Force | Out-Null

    # Copy all build output
    Copy-Item -Path "$BuildOutput\*" -Destination $ExtensionsDest -Recurse -Force

    # Verify deployment
    $deployedDll = Join-Path $ExtensionsDest "AideLite.dll"
    if (Test-Path $deployedDll) {
        $fileCount = (Get-ChildItem $ExtensionsDest -Recurse -File).Count
        Write-Host "Deployed $fileCount files to Extensions folder." -ForegroundColor Green
    } else {
        Write-Host "ERROR: Deployment verification failed - AideLite.dll not found in destination" -ForegroundColor Red
        exit 1
    }
}

function Step-Launch {
    Write-Host "=== Launching Studio Pro ===" -ForegroundColor Cyan

    if (!(Test-Path $StudioProExe)) {
        Write-Host "ERROR: Studio Pro not found at $StudioProExe" -ForegroundColor Red
        exit 1
    }
    if (!(Test-Path $MendixProjectPath)) {
        Write-Host "ERROR: Mendix project not found at $MendixProjectPath" -ForegroundColor Red
        exit 1
    }

    # Check if already running
    $sp = Get-Process -Name "studiopro" -ErrorAction SilentlyContinue
    if ($sp) {
        Write-Host "WARNING: Studio Pro is already running (PID: $($sp.Id))" -ForegroundColor Yellow
        Write-Host "Close it first or use the 'close' step." -ForegroundColor Yellow
        exit 1
    }

    Write-Host "Opening: $MendixProjectPath" -ForegroundColor Gray
    Start-Process $StudioProExe -ArgumentList "`"$MendixProjectPath`""
    Write-Host "Studio Pro launched." -ForegroundColor Green
}

function Step-Status {
    Write-Host "=== AIDE-Lite Status ===" -ForegroundColor Cyan

    # Studio Pro process
    $sp = Get-Process -Name "studiopro" -ErrorAction SilentlyContinue
    if ($sp) {
        Write-Host "Studio Pro: RUNNING (PID: $($sp.Id))" -ForegroundColor Green
    } else {
        Write-Host "Studio Pro: NOT RUNNING" -ForegroundColor Yellow
    }

    # Lock file
    if (Test-Path $LockFile) {
        Write-Host "Lock file:  EXISTS" -ForegroundColor Yellow
    } else {
        Write-Host "Lock file:  none" -ForegroundColor Gray
    }

    # Build output
    $dll = Join-Path $BuildOutput "AideLite.dll"
    if (Test-Path $dll) {
        $info = Get-Item $dll
        $sizeKB = [math]::Round($info.Length / 1024)
        Write-Host "Build output: AideLite.dll ($sizeKB KB, $($info.LastWriteTime))" -ForegroundColor Gray
    } else {
        Write-Host "Build output: NOT FOUND" -ForegroundColor Yellow
    }

    # Deployed extension
    $deployedDll = Join-Path $ExtensionsDest "AideLite.dll"
    if (Test-Path $deployedDll) {
        $info = Get-Item $deployedDll
        $fileCount = (Get-ChildItem $ExtensionsDest -Recurse -File).Count
        $sizeKB = [math]::Round($info.Length / 1024)
        Write-Host "Deployed:   $fileCount files (DLL: $sizeKB KB, $($info.LastWriteTime))" -ForegroundColor Gray

        # Check if build output is newer than deployed
        $buildDll = Get-Item $dll -ErrorAction SilentlyContinue
        if ($buildDll -and $buildDll.LastWriteTime -gt $info.LastWriteTime) {
            Write-Host "            WARNING: Build output is newer than deployed version" -ForegroundColor Yellow
        }
    } else {
        Write-Host "Deployed:   NOT FOUND" -ForegroundColor Yellow
    }

    # Mendix version
    Write-Host "Mendix:     $MendixVersion" -ForegroundColor Gray
    Write-Host "Project:    $MendixProjectPath" -ForegroundColor Gray
}

# ============================================================
# Dispatch
# ============================================================
switch ($Step) {
    "close"  { Step-Close }
    "build"  { Step-Build }
    "deploy" { Step-Deploy }
    "launch" { Step-Launch }
    "status" { Step-Status }
}
