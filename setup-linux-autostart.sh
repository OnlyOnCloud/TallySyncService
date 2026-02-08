#!/bin/bash

# TallySyncService - Linux Auto-Start Setup Script
# This script sets up the service to auto-start on user login using systemd user service

set -e

echo "╔═══════════════════════════════════════════════╗"
echo "║   TallySyncService - Linux Auto-Start Setup  ║"
echo "╚═══════════════════════════════════════════════╝"
echo ""

# Get the absolute path to the project directory
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVICE_FILE="tallysync-user.service"
SERVICE_NAME="tallysync-user.service"

# Create log directory
LOG_DIR="$HOME/.local/share/tallysync"
mkdir -p "$LOG_DIR"
echo "✓ Created log directory: $LOG_DIR"

# Create systemd user directory if it doesn't exist
SYSTEMD_USER_DIR="$HOME/.config/systemd/user"
mkdir -p "$SYSTEMD_USER_DIR"
echo "✓ Created systemd user directory: $SYSTEMD_USER_DIR"

# Create a temporary service file with correct paths
TEMP_SERVICE="/tmp/tallysync-user.service"
cat > "$TEMP_SERVICE" << EOF
[Unit]
Description=Tally Sync Service - Auto sync Tally data to backend
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
WorkingDirectory=$PROJECT_DIR
ExecStart=/usr/bin/dotnet run --project $PROJECT_DIR/TallySyncService.csproj
Restart=on-failure
RestartSec=30
StandardOutput=append:$LOG_DIR/output.log
StandardError=append:$LOG_DIR/error.log

# Environment variables (if needed)
Environment="DOTNET_CLI_TELEMETRY_OPTOUT=1"

[Install]
WantedBy=default.target
EOF

# Copy service file to systemd user directory
cp "$TEMP_SERVICE" "$SYSTEMD_USER_DIR/$SERVICE_NAME"
echo "✓ Installed service file to: $SYSTEMD_USER_DIR/$SERVICE_NAME"

# Reload systemd user daemon
systemctl --user daemon-reload
echo "✓ Reloaded systemd user daemon"

# Enable the service to start on login
systemctl --user enable "$SERVICE_NAME"
echo "✓ Enabled service to start on user login"

# Start the service now
systemctl --user start "$SERVICE_NAME"
echo "✓ Started service"

echo ""
echo "═══════════════════════════════════════════════"
echo "✅ Auto-start setup complete!"
echo "═══════════════════════════════════════════════"
echo ""
echo "Service Commands:"
echo "  • Check status:  systemctl --user status $SERVICE_NAME"
echo "  • View logs:     journalctl --user -u $SERVICE_NAME -f"
echo "  • Stop service:  systemctl --user stop $SERVICE_NAME"
echo "  • Restart:       systemctl --user restart $SERVICE_NAME"
echo "  • Disable:       systemctl --user disable $SERVICE_NAME"
echo ""
echo "Log files:"
echo "  • Output: $LOG_DIR/output.log"
echo "  • Errors: $LOG_DIR/error.log"
echo ""
