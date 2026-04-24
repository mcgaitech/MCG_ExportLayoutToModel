using System;
using System.Diagnostics;
using System.IO;

namespace MCGCadPlugin.Utilities.DetailDesign
{
    /// <summary>
    /// Ghi log ra file cho module DetailDesign.
    /// Format: [LEVEL] yyyy-MM-dd HH:mm:ss | message
    /// Output: C:\CustomTools\Temp\MCG_{task}_{panel}_{timestamp}.log
    /// </summary>
    public class LogHelper : IDisposable
    {
        private const string LOG_PREFIX = "[LogHelper]";

        private readonly StreamWriter _writer;
        private readonly string _filePath;
        private bool _disposed;

        /// <summary>Đường dẫn file log hiện tại</summary>
        public string FilePath => _filePath;

        /// <summary>
        /// Khởi tạo LogHelper — tạo file log mới.
        /// </summary>
        /// <param name="task">Tên tác vụ (SCAN, SECTION, BOM, ...)</param>
        /// <param name="panelName">Tên panel đang xử lý</param>
        public LogHelper(string task, string panelName)
        {
            // Tạo thư mục nếu chưa có
            if (!Directory.Exists(DetailDesignConstants.LOG_PATH))
            {
                Directory.CreateDirectory(DetailDesignConstants.LOG_PATH);
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var safePanelName = SanitizeFileName(panelName);
            var fileName = $"MCG_{task}_{safePanelName}_{timestamp}.log";
            _filePath = Path.Combine(DetailDesignConstants.LOG_PATH, fileName);

            _writer = new StreamWriter(_filePath, append: false, encoding: System.Text.Encoding.UTF8);
            _writer.AutoFlush = true;

            Info($"Log started — Task: {task} | Panel: {panelName}");
            Debug.WriteLine($"{LOG_PREFIX} Log file created: {_filePath}");
        }

        /// <summary>Ghi log mức INFO</summary>
        public void Info(string message)
        {
            WriteLine("INFO", message);
        }

        /// <summary>Ghi log mức WARN</summary>
        public void Warn(string message)
        {
            WriteLine("WARN", message);
        }

        /// <summary>Ghi log mức ERROR</summary>
        public void Error(string message)
        {
            WriteLine("ERROR", message);
        }

        /// <summary>Ghi log mức ERROR kèm exception</summary>
        public void Error(string message, System.Exception ex)
        {
            WriteLine("ERROR", $"{message} | {ex.Message}");
            WriteLine("ERROR", $"Stack: {ex.StackTrace}");
        }

        /// <summary>Ghi log mức DEBUG</summary>
        public void LogDebug(string message)
        {
            WriteLine("DEBUG", message);
        }

        /// <summary>Giải phóng tài nguyên file</summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                Info("Log ended.");
                _writer?.Dispose();
                _disposed = true;
                Debug.WriteLine($"{LOG_PREFIX} Log file closed: {_filePath}");
            }
        }

        #region Private Methods

        /// <summary>Ghi 1 dòng log theo format chuẩn</summary>
        private void WriteLine(string level, string message)
        {
            if (_disposed) return;

            var line = $"[{level}] {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
            _writer.WriteLine(line);
            // Đồng thời ghi ra Debug output
            Debug.WriteLine($"{LOG_PREFIX} {line}");
        }

        /// <summary>Loại bỏ ký tự không hợp lệ trong tên file</summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "UNKNOWN";

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        #endregion
    }
}
