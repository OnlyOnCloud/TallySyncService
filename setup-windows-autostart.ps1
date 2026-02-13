# TallySyncService - Windows Auto-Start Setup Script
# This script creates a scheduled task to auto-start the service on user login

Write-Host "╔═══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  TallySyncService - Windows Auto-Start Setup ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# 1. Validation: Must be Windows
if ($IsLinux -or $IsMacOS) {
    Write-Host "❌ Error: This script is for Windows only." -ForegroundColor Red
    exit 1
}

# 2. Get absolute paths
$ScriptPath = $MyInvocation.MyCommand.Path
if (-not $ScriptPath) {
    $ScriptPath = (Get-Item $PSCommandPath).FullName
}
$ProjectDir = Split-Path -Parent $ScriptPath
$TaskName = "TallySyncService"

# 3. Find Dotnet Executable
$DotnetPath = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $DotnetPath) {
    Write-Host "❌ Error: 'dotnet' command not found. Please install .NET SDK/Runtime." -ForegroundColor Red
    exit 1
}

# 4. Locate DLL
$DllPath = "$ProjectDir\bin\Debug\net8.0\TallySyncService.dll"
if (-not (Test-Path $DllPath)) {
    # Try Release if Debug not found
    $DllPath = "$ProjectDir\bin\Release\net8.0\TallySyncService.dll"
}

if (-not (Test-Path $DllPath)) {
    Write-Host "⚠️  Executable DLL not found. Attempting to build project..." -ForegroundColor Yellow
    Push-Location $ProjectDir
    dotnet build -c Debug
    Pop-Location
    $DllPath = "$ProjectDir\bin\Debug\net8.0\TallySyncService.dll"
}

if (-not (Test-Path $DllPath)) {
    Write-Host "❌ Error: Could not find or build TallySyncService.dll." -ForegroundColor Red
    exit 1
}

Write-Host "✓ Using Dotnet: $DotnetPath" -ForegroundColor Gray
Write-Host "✓ Using DLL:    $DllPath" -ForegroundColor Gray

# 5. Create log directory
$LogDir = "$env:LOCALAPPDATA\TallySyncService\Logs"
if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
    Write-Host "✓ Created log directory: $LogDir" -ForegroundColor Green
}

# 6. Check for Admin Privileges
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "⚠️  Warning: This script should ideally be run as Administrator to create scheduled tasks." -ForegroundColor Yellow
}

# 7. Check if task already exists
$ExistingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($ExistingTask) {
    Write-Host "⚠️  Task '$TaskName' already exists. Removing old task..." -ForegroundColor Yellow
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

# 8. Create the action
# We use 'dotnet exec' to run the DLL directly, which is faster and more reliable than 'dotnet run'
$Action = New-ScheduledTaskAction `
    -Execute "`"$DotnetPath`"" `
    -Argument "exec `"$DllPath`"" `
    -WorkingDirectory "`"$ProjectDir`""

# 9. Create the trigger (at user login)
$Trigger = New-ScheduledTaskTrigger -AtLogOn

# 10. Create settings
$Settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

# 11. Create the principal (run with highest privileges for the current user)
$Principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -LogonType Interactive `
    -RunLevel Highest

# 12. Register the scheduled task
try {
    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $Action `
        -Trigger $Trigger `
        -Settings $Settings `
        -Principal $Principal `
        -Description "Automatically syncs Tally data to backend on user login" -ErrorAction Stop | Out-Null
    
    Write-Host "✓ Created scheduled task: $TaskName" -ForegroundColor Green

    # 13. Start the task now
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "✓ Started task" -ForegroundColor Green
}
catch {
    Write-Host "❌ Failed to register scheduled task: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   Make sure you are running PowerShell as Administrator." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "✅ Auto-start setup complete!" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Task Management Commands:" -ForegroundColor Yellow
Write-Host "  • View task:      Get-ScheduledTask -TaskName '$TaskName'" -ForegroundColor White
Write-Host "  • Start task:     Start-ScheduledTask -TaskName '$TaskName'" -ForegroundColor White
Write-Host "  • Stop task:      Stop-ScheduledTask -TaskName '$TaskName'" -ForegroundColor White
Write-Host "  • Disable task:   Disable-ScheduledTask -TaskName '$TaskName'" -ForegroundColor White
Write-Host "  • Remove task:    Unregister-ScheduledTask -TaskName '$TaskName'" -ForegroundColor White
Write-Host ""
Write-Host "Log directory: $LogDir" -ForegroundColor Yellow
Write-Host ""
Write-Host "Note: The service will automatically start on your next login." -ForegroundColor Cyan
Write-Host ""
