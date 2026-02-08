using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TallySyncService.Services;

public class NotificationService
{
    public void SendNotification(string title, string message)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SendWindowsNotification(title, message);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                SendLinuxNotification(title, message);
            }
            else
            {
                Console.WriteLine($"📢 {title}: {message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Failed to send notification: {ex.Message}");
            Console.WriteLine($"📢 {title}: {message}");
        }
    }

    private void SendWindowsNotification(string title, string message)
    {
        // Use PowerShell to show a Windows toast notification
        var escapedMessage = message.Replace("'", "''").Replace("\"", "`\"");
        var escapedTitle = title.Replace("'", "''").Replace("\"", "`\"");
        
        var script = $@"
            [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
            [Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
            [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null

            $template = @""
            <toast>
                <visual>
                    <binding template='ToastGeneric'>
                        <text>{escapedTitle}</text>
                        <text>{escapedMessage}</text>
                    </binding>
                </visual>
            </toast>
            ""@

            $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
            $xml.LoadXml($template)
            $toast = New-Object Windows.UI.Notifications.ToastNotification $xml
            [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('TallySyncService').Show($toast)
        ";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        process?.WaitForExit(5000); // Wait max 5 seconds
    }

    private void SendLinuxNotification(string title, string message)
    {
        // Use notify-send on Linux
        var psi = new ProcessStartInfo
        {
            FileName = "notify-send",
            Arguments = $"\"{title}\" \"{message}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        process?.WaitForExit(5000); // Wait max 5 seconds
    }
}
