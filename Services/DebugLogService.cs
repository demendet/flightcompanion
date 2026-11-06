using System;
using System.IO;
using System.Threading;

namespace FlightCompanion.Services
{
    public interface IDebugLogService
    {
        void Log(string message, string category = "General");
        void LogError(string message, Exception? exception = null, string category = "Error");
        void LogInfo(string message, string category = "Info");
        void LogWarning(string message, string category = "Warning");
        void LogSimConnect(string message);
        void LogPhoneCall(string message);
        void LogVoice(string message);
        void LogMap(string message);
        void LogMultiplayer(string message);
    }

    public class DebugLogService : IDebugLogService
    {
        private readonly string _logDirectory;
        private readonly object _lockObject = new();

        public DebugLogService()
        {
            _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlightCompanion", "Logs");
            Directory.CreateDirectory(_logDirectory);
        }

        public void Log(string message, string category = "General")
        {
            WriteLog(message, category);
            Console.WriteLine($"[{category}] {message}");
        }

        public void LogError(string message, Exception? exception = null, string category = "Error")
        {
            var fullMessage = exception != null ? $"{message} - Exception: {exception.Message}\nStackTrace: {exception.StackTrace}" : message;
            WriteLog(fullMessage, category);
            Console.WriteLine($"[ERROR-{category}] {fullMessage}");
        }

        public void LogInfo(string message, string category = "Info")
        {
            WriteLog(message, category);
            Console.WriteLine($"[INFO-{category}] {message}");
        }

        public void LogWarning(string message, string category = "Warning")
        {
            WriteLog(message, category);
            Console.WriteLine($"[WARN-{category}] {message}");
        }

        public void LogSimConnect(string message)
        {
            Log(message, "SimConnect");
        }

        public void LogPhoneCall(string message)
        {
            Log(message, "PhoneCall");
        }

        public void LogVoice(string message)
        {
            Log(message, "Voice");
        }

        public void LogMap(string message)
        {
            Log(message, "Map");
        }

        public void LogMultiplayer(string message)
        {
            Log(message, "Multiplayer");
        }

        private void WriteLog(string message, string category)
        {
            lock (_lockObject)
            {
                try
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var logEntry = $"[{timestamp}] [{category}] [{Thread.CurrentThread.ManagedThreadId}] {message}";
                    
                    var logFile = Path.Combine(_logDirectory, $"FlightCompanion_{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(logFile, logEntry + Environment.NewLine);
                }
                catch
                {
                    // Ignore logging errors to prevent infinite loops
                }
            }
        }
    }
}