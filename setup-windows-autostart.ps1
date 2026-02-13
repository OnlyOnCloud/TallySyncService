# TallySyncService - Complete Setup (Final Version)
# This script publishes, copies config files, and sets up auto-start

# Check if running as Administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "This script needs Administrator privileges to manage scheduled tasks." -ForegroundColor Yellow
    Write-Host "Restarting as Administrator..." -ForegroundColor Yellow
    Write-Host ""
    
    # Restart as admin
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File "$($MyInvocation.MyCommand.Path)""
    exit
}

Write-Host "===============================================" -ForegroundColor Cyan
Write-Host "  TallySyncService - Complete Setup" -ForegroundColor Cyan
Write-Host "  Running as Administrator" -ForegroundColor Green
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host ""

$TaskName = "TallySyncService"
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# STEP 1: Clean up everything first
Write-Host "STEP 1: Cleaning up old installations..." -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Remove old scheduled task
Write-Host "Removing old scheduled task..." -ForegroundColor Yellow
$ExistingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($ExistingTask) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "[OK] Old task removed" -ForegroundColor Green
} else {
    Write-Host "[OK] No existing task found" -ForegroundColor Green
}

# Kill any running processes
Write-Host "Stopping any running service instances..." -ForegroundColor Yellow
Get-Process | Where-Object {
    $_.ProcessName -like "TallySync" -or
    ($.Path -and $.Path -like "TallySyncService")
} | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host "[OK] Processes stopped" -ForegroundColor Green

# Remove old launcher files
Write-Host "Removing old launcher files..." -ForegroundColor Yellow
@("$ProjectDir\run-hidden.vbs", "$ProjectDir\start-service-hidden.ps1") | ForEach-Object {
    if (Test-Path $) { Remove-Item $ -Force }
}
Write-Host "[OK] Old files cleaned up" -ForegroundColor Green

Write-Host ""

# STEP 2: Check for .NET SDK
Write-Host "STEP 2: Checking prerequisites..." -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan
Write-Host ""

$DotnetPath = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $DotnetPath) {
    Write-Host "[ERROR] dotnet command not found!" -ForegroundColor Red
    Write-Host "You need .NET SDK installed to publish the application." -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "[OK] .NET SDK found: $DotnetPath" -ForegroundColor Green

# Find project file
$ProjectFile = Get-ChildItem -Path $ProjectDir -Filter "*.csproj" | Select-Object -First 1
if (-not $ProjectFile) {
    Write-Host "[ERROR] No .csproj file found!" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "[OK] Project file: $($ProjectFile.Name)" -ForegroundColor Green

Write-Host ""

# STEP 3: Publish the application
Write-Host "STEP 3: Publishing self-contained executable..." -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Creating standalone .exe (no .NET needed on customer machines)" -ForegroundColor Yellow
Write-Host "Please wait..." -ForegroundColor Yellow
Write-Host ""

$PublishDir = "$ProjectDir\publish"

# Remove old publish folder
if (Test-Path $PublishDir) {
    Write-Host "Removing old publish folder..." -ForegroundColor Yellow
    Remove-Item -Path $PublishDir -Recurse -Force
}

# Publish command
$PublishArgs = @(
    "publish"
    ""$($ProjectFile.FullName)""
    "-c", "Release"
    "-r", "win-x64"
    "--self-contained", "true"
    "-p:PublishSingleFile=true"
    "-p:IncludeNativeLibrariesForSelfExtract=true"
    "-p:PublishTrimmed=false"
    "-o", ""$PublishDir""
)

& $DotnetPath $PublishArgs | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Failed to publish!" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "[SUCCESS] Application published!" -ForegroundColor Green
Write-Host ""

# STEP 4: Copy configuration files
Write-Host "STEP 4: Copying configuration files..." -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host ""

# Copy all necessary config files to publish folder
$ConfigFiles = @(
    "tally-export-config.yaml",
    "appsettings.json",
    "appsettings.Development.json",
    "appsettings.Production.json"
)

foreach ($configFile in $ConfigFiles) {
    $sourcePath = "$ProjectDir\$configFile"
    if (Test-Path $sourcePath) {
        Copy-Item -Path $sourcePath -Destination $PublishDir -Force
        Write-Host "[OK] Copied: $configFile" -ForegroundColor Green
    } else {
        Write-Host "[SKIP] Not found: $configFile" -ForegroundColor Gray
    }
}

Write-Host ""

# STEP 5: Verify executable
Write-Host "STEP 5: Verifying published files..." -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

$ExeFile = Get-ChildItem -Path $PublishDir -Filter "*.exe" | Select-Object -First 1
if (-not $ExeFile) {
    Write-Host "[ERROR] No .exe file found in publish directory!" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

$ExePath = $ExeFile.FullName
Write-Host "[OK] Executable: $($ExeFile.Name)" -ForegroundColor Green
Write-Host "     Size: $([math]::Round($ExeFile.Length / 1MB, 2)) MB" -ForegroundColor Gray
Write-Host "     Path: $ExePath" -ForegroundColor Gray

Write-Host ""

# STEP 6: Create hidden launcher
Write-Host "STEP 6: Creating hidden launcher..." -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

$VbsPath = "$PublishDir\run-hidden.vbs"
$VbsContent = @"
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run """$ExePath""", 0, False
Set WshShell = Nothing
"@
$VbsContent | Out-File -FilePath $VbsPath -Encoding ASCII -Force
Write-Host "[OK] Launcher created" -ForegroundColor Green

Write-Host ""

# STEP 7: Create scheduled task
Write-Host "STEP 7: Creating scheduled task..." -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan
Write-Host ""

# Double-check no task exists
$CheckTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($CheckTask) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
    Start-Sleep -Seconds 2
}

# Create the action
$Action = New-ScheduledTaskAction `
    -Execute "wscript.exe" `
    -Argument ""$VbsPath"" `
    -WorkingDirectory "$PublishDir"

# Create the trigger (at user login)
$Trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME

# Create settings
$Settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Hours 0) `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

# Create the principal
$Principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -LogonType Interactive `
    -RunLevel Limited

# Register the task
Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $Action `
    -Trigger $Trigger `
    -Settings $Settings `
    -Principal $Principal `
    -Description "TallySyncService - Auto-sync Tally data (standalone, no .NET required)" -ErrorAction Stop | Out-Null

Write-Host "[OK] Scheduled task created" -ForegroundColor Green
Write-Host ""

# STEP 8: Start the service
Write-Host "STEP 8: Starting the service..." -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

Start-ScheduledTask -TaskName $TaskName
Write-Host "Waiting for service to start..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

# Check if it's running
$ExeName = $ExeFile.BaseName
$ServiceProcess = Get-Process -Name $ExeName -ErrorAction SilentlyContinue

Write-Host ""
if ($ServiceProcess) {
    Write-Host "===============================================" -ForegroundColor Green
    Write-Host "[SUCCESS] Service is running!" -ForegroundColor Green
    Write-Host "===============================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Process Details:" -ForegroundColor Cyan
    Write-Host "  Name: $ExeName" -ForegroundColor White
    Write-Host "  PID:  $($ServiceProcess.Id)" -ForegroundColor White
    Write-Host "  Path: $ExePath" -ForegroundColor White
    Write-Host ""
    Write-Host "The service is now running HIDDEN in the background!" -ForegroundColor Cyan
    Write-Host "It will sync every 15 minutes and auto-start on login." -ForegroundColor Cyan
} else {
    Write-Host "[WARNING] Service process not found in Task Manager" -ForegroundColor Yellow
    Write-Host "This might be normal - the service may have started and is waiting." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To verify, check Task Manager for '$ExeName.exe'" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host "Setup Complete!" -ForegroundColor Green
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Installation Summary:" -ForegroundColor Yellow
Write-Host "  Location:      $PublishDir" -ForegroundColor White
Write-Host "  Executable:    TallySyncService.exe" -ForegroundColor White
Write-Host "  Task Name:     $TaskName" -ForegroundColor White
Write-Host "  Auto-start:    Enabled (on login)" -ForegroundColor White
Write-Host "  Runs Hidden:   Yes (no console)" -ForegroundColor White
Write-Host "  Needs .NET:    No (self-contained)" -ForegroundColor White
Write-Host ""
Write-Host "For Customer Deployment:" -ForegroundColor Yellow
Write-Host "  1. Copy the entire 'publish' folder to customer machine" -ForegroundColor White
Write-Host "  2. Make sure config files are in the publish folder" -ForegroundColor White
Write-Host "  3. Run this script as Administrator" -ForegroundColor White
Write-Host "  4. No .NET installation needed!" -ForegroundColor White
Write-Host ""
Write-Host "Management Commands:" -ForegroundColor Yellow
Write-Host "  Start:    Start-ScheduledTask -TaskName $TaskName" -ForegroundColor White
Write-Host "  Stop:     Stop-ScheduledTask -TaskName $TaskName" -ForegroundColor White
Write-Host "  Status:   Get-Process $ExeName -ErrorAction SilentlyContinue" -ForegroundColor White
Write-Host "  Remove:   Unregister-ScheduledTask -TaskName $TaskName" -ForegroundColor White
Write-Host ""

Read-Host "Press Enter to close"
