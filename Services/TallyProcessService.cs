using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TallySyncService.Services;

public class TallyProcessService
{
    private readonly string _tallyPath;

    public TallyProcessService(string tallyPath)
    {
        _tallyPath = tallyPath;
    }

    public bool IsTallyRunning()
    {
        try
        {
            // Check if any process named "tally" or "Tally.exe" is running
            var processes = Process.GetProcesses();
            return processes.Any(p => 
                p.ProcessName.Equals("tally", StringComparison.OrdinalIgnoreCase) ||
                p.ProcessName.Equals("Tally.exe", StringComparison.OrdinalIgnoreCase) ||
                p.ProcessName.Contains("tally", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Error checking if Tally is running: {ex.Message}");
            return false;
        }
    }

    public bool LaunchTally()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_tallyPath))
            {
                Console.WriteLine("✗ Tally path not configured. Please run setup again.");
                return false;
            }

            if (!File.Exists(_tallyPath))
            {
                Console.WriteLine($"✗ Tally executable not found at: {_tallyPath}");
                return false;
            }

            Console.WriteLine($"🚀 Launching Tally from: {_tallyPath}");
            
            ProcessStartInfo psi;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Native Windows execution
                psi = new ProcessStartInfo
                {
                    FileName = _tallyPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(_tallyPath)
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux with Wine
                Console.WriteLine("🍷 Detected Linux platform, using Wine to launch Tally...");
                
                // Check if wine is available
                if (!IsWineAvailable())
                {
                    Console.WriteLine("✗ Wine is not installed or not in PATH. Please install Wine to run Tally on Linux.");
                    return false;
                }
                
                psi = new ProcessStartInfo
                {
                    FileName = "wine",
                    Arguments = $"\"{_tallyPath}\"",
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(_tallyPath),
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
            }
            else
            {
                Console.WriteLine("✗ Unsupported platform. Tally can only run on Windows or Linux (with Wine).");
                return false;
            }

            Process.Start(psi);
            Console.WriteLine("✓ Tally launched successfully");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to launch Tally: {ex.Message}");
            return false;
        }
    }

    private bool IsWineAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "wine",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;
            
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
