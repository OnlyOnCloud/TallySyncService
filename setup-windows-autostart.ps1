# TallySyncService - Windows Auto-Start Setup Script
# This script creates a scheduled task to auto-start the service on user login

Write-Host "╔═══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  TallySyncService - Windows Auto-Start Setup ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Get the absolute path to the project directory
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$TaskName = "TallySyncService"

# Create log directory
$LogDir = "$env:LOCALAPPDATA\TallySyncService\Logs"
if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
    Write-Host "✓ Created log directory: $LogDir" -ForegroundColor Green
}

# Check if task already exists
$ExistingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($ExistingTask) {
    Write-Host "⚠️  Task '$TaskName' already exists. Removing old task..." -ForegroundColor Yellow
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

# Create the action (what to run)
$Action = New-ScheduledTaskAction `
    -Execute "dotnet" `
    -Argument "run --project `"$ProjectDir\TallySyncService.csproj`"" `
    -WorkingDirectory $ProjectDir

# Create the trigger (when to run - at user login)
$Trigger = New-ScheduledTaskTrigger -AtLogOn

# Create settings
$Settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

# Create the principal (run as current user)
$Principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -LogonType Interactive `
    -RunLevel Limited

# Register the scheduled task
Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $Action `
    -Trigger $Trigger `
    -Settings $Settings `
    -Principal $Principal `
    -Description "Automatically syncs Tally data to backend on user login" | Out-Null

Write-Host "✓ Created scheduled task: $TaskName" -ForegroundColor Green

# Start the task now
Start-ScheduledTask -TaskName $TaskName
Write-Host "✓ Started task" -ForegroundColor Green

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
