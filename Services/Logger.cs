using System;
using System.IO;
using System.Text;

namespace TallySyncService.Services
{
    public interface ILogger
    {
        void LogMessage(string message, params object[] args);
        void LogError(string functionName, Exception ex);
        void LogError(string functionName, string message);
        void Close();
    }

    public class FileLogger : ILogger
    {
        private readonly StreamWriter _messageStream;
        private readonly StreamWriter _errorStream;
        private bool _errorLogged = false;

        public FileLogger(string logDirectory = ".")
        {
            // Ensure directory exists
            Directory.CreateDirectory(logDirectory);

            string messagePath = Path.Combine(logDirectory, "import-log.txt");
            string errorPath = Path.Combine(logDirectory, "error-log.txt");

            // Delete existing log files
            if (File.Exists(messagePath)) File.Delete(messagePath);
            if (File.Exists(errorPath)) File.Delete(errorPath);

            _messageStream = new StreamWriter(messagePath, false, Encoding.UTF8) { AutoFlush = true };
            _errorStream = new StreamWriter(errorPath, false, Encoding.UTF8) { AutoFlush = true };
        }

        public void LogMessage(string message, params object[] args)
        {
            try
            {
                string formattedMessage = string.Format(message, args);
                Console.WriteLine(formattedMessage);
                _messageStream.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {formattedMessage}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error writing log: {ex.Message}");
            }
        }

        public void LogError(string functionName, Exception ex)
        {
            if (!_errorLogged)
            {
                _errorLogged = true;
                string errorLog = $"Error from {(functionName.EndsWith(")") ? functionName : functionName + "()")} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n";
                errorLog += $"{ex.Message}\r\n";
                if (ex.InnerException != null)
                    errorLog += $"Inner Exception: {ex.InnerException.Message}\r\n";
                errorLog += $"Stack Trace: {ex.StackTrace}\r\n";
                errorLog += new string('-', 80) + "\r\n\r\n";

                Console.Error.WriteLine(errorLog);
                _errorStream.Write(errorLog);
            }
        }

        public void LogError(string functionName, string message)
        {
            if (!_errorLogged)
            {
                _errorLogged = true;
                string errorLog = $"Error from {(functionName.EndsWith(")") ? functionName : functionName + "()")} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n";
                errorLog += $"{message}\r\n";
                errorLog += new string('-', 80) + "\r\n\r\n";

                Console.Error.WriteLine(errorLog);
                _errorStream.Write(errorLog);
            }
        }

        public void Close()
        {
            _messageStream?.Dispose();
            _errorStream?.Dispose();
        }
    }
}
