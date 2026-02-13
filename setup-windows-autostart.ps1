# TallySyncService - Windows Auto-Start Setup Script
# This script creates a scheduled task to auto-start the service on user login

Write-Host "===============================================" -ForegroundColor Cyan
Write-Host "  TallySyncService - Windows Auto-Start Setup" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host ""

# 1. Validation: Must be Windows
Write-Host "Checking if the OS is Windows..." -ForegroundColor Yellow
if ($env:OS -notlike "Windows*") {
    Write-Host "[ERROR] This script is for Windows only." -ForegroundColor Red
    exit 1
}

# 2. Get absolute paths
Write-Host "Getting absolute paths..." -ForegroundColor Yellow
$ScriptPath = $MyInvocation.MyCommand.Path
if (-not $ScriptPath) {
    Write-Host "[ERROR] Cannot determine script path." -ForegroundColor Red
    exit 1
}
$ProjectDir = Split-Path -Parent $ScriptPath
$TaskName = "TallySyncService"

# 3. Find Dotnet Executable
Write-Host "Finding the dotnet executable..." -ForegroundColor Yellow
$DotnetPath = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $DotnetPath) {
    Write-Host "[ERROR] 'dotnet' command not found. Please install .NET SDK/Runtime." -ForegroundColor Red
    exit 1
}

# 4. Locate DLL
Write-Host "Locating TallySyncService.dll..." -ForegroundColor Yellow
$DllPath = "$ProjectDir\bin\Debug\net8.0\TallySyncService.dll"
if (-not (Test-Path $DllPath)) {
    # Try Release if Debug not found
    $DllPath = "$ProjectDir\bin\Release\net8.0\TallySyncService.dll"
}

if (-not (Test-Path $DllPath)) {
    Write-Host "[ERROR] Could not find TallySyncService.dll in either Debug or Release folder." -ForegroundColor Red
    exit 1
}

Write-Host "[OK] Using Dotnet: $DotnetPath" -ForegroundColor Gray
Write-Host "[OK] Using DLL:    $DllPath" -ForegroundColor Gray

# 5. Create log directory
Write-Host "Creating log directory..." -ForegroundColor Yellow
$LogDir = "$env:LOCALAPPDATA\TallySyncService\Logs"
if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

# 6. Create a wrapper VBS script to run without console window
Write-Host "Creating hidden launcher script..." -ForegroundColor Yellow
$VbsPath = "$ProjectDir\run-hidden.vbs"
$VbsContent = @"
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run """$DotnetPath"" exec ""$DllPath""", 0, False
Set WshShell = Nothing
"@
$VbsContent | Out-File -FilePath $VbsPath -Encoding ASCII -Force

# 7. Check for Admin Privileges
Write-Host "Checking if the script is running as Administrator..." -ForegroundColor Yellow
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "[WARNING] This script should ideally be run as Administrator to create scheduled tasks." -ForegroundColor Yellow
}

# 8. Check if task already exists
Write-Host "Checking if scheduled task already exists..." -ForegroundColor Yellow
$ExistingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($ExistingTask) {
    Write-Host "[WARNING] Task TallySyncService already exists. Removing old task..." -ForegroundColor Yellow
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

# 9. Create the action (using VBS script to hide window)
Write-Host "Creating scheduled task action..." -ForegroundColor Yellow
$Action = New-ScheduledTaskAction `
    -Execute "wscript.exe" `
    -Argument ""$VbsPath"" `
    -WorkingDirectory "$ProjectDir"

# Check if Action is created correctly
if (-not $Action) {
    Write-Host "[ERROR] Failed to create scheduled task action." -ForegroundColor Red
    exit 1
}

# 10. Create the trigger (at user login)
Write-Host "Creating scheduled task trigger..." -ForegroundColor Yellow
$Trigger = New-ScheduledTaskTrigger -AtLogOn

# 11. Create settings
Write-Host "Creating scheduled task settings..." -ForegroundColor Yellow
$Settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

# 12. Create the principal (run with highest privileges for the current user)
Write-Host "Creating scheduled task principal..." -ForegroundColor Yellow
$Principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -LogonType Interactive `
    -RunLevel Highest

# 13. Register the scheduled task
Write-Host "Registering the scheduled task..." -ForegroundColor Yellow
try {
    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $Action `
        -Trigger $Trigger `
        -Settings $Settings `
        -Principal $Principal `
        -Description "Automatically syncs Tally data to backend on user login (runs hidden)" -ErrorAction Stop | Out-Null
    
    Write-Host "[SUCCESS] Created scheduled task: TallySyncService" -ForegroundColor Green

    # 14. Start the task now
    Write-Host "Starting the task now (hidden)..." -ForegroundColor Yellow
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "[SUCCESS] Started task in background" -ForegroundColor Green
}
catch {
    Write-Host "[ERROR] Failed to register scheduled task: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "        Make sure you are running PowerShell as Administrator." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host "[SUCCESS] Auto-start setup complete!" -ForegroundColor Green
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Task Management Commands:" -ForegroundColor Yellow
Write-Host "  - View task:      Get-ScheduledTask -TaskName $TaskName" -ForegroundColor White
Write-Host "  - Start task:     Start-ScheduledTask -TaskName $TaskName" -ForegroundColor White
Write-Host "  - Stop task:      Stop-ScheduledTask -TaskName $TaskName" -ForegroundColor White
Write-Host "  - Disable task:   Disable-ScheduledTask -TaskName $TaskName" -ForegroundColor White
Write-Host "  - Remove task:    Unregister-ScheduledTask -TaskName $TaskName" -ForegroundColor White
Write-Host ""
Write-Host "Log directory: $LogDir" -ForegroundColor Yellow
Write-Host ""
Write-Host "Note: The service will automatically start HIDDEN on your next login." -ForegroundColor Cyan
Write-Host "      No console window will appear - check Task Manager to verify it's running." -ForegroundColor Cyan
Write-Host ""
