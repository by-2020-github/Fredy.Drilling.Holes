using System;
using System.IO;

namespace Common.Tools
{
    public class PathManager
    {
        public PathManager()
            : this(AppContext.BaseDirectory)
        {
        }

        public PathManager(string runtimePath)
        {
            SetRuntimePath(runtimePath);
        }

        public string RuntimePath { get; private set; }

        public string ConfigPath { get; private set; }

        public string ResultPath { get; private set; }

        public string LogPath { get; private set; }

        public void SetRuntimePath(string runtimePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimePath);

            RuntimePath = Path.GetFullPath(runtimePath);
            ConfigPath = Path.Combine(RuntimePath, "Config");
            ResultPath = Path.Combine(RuntimePath, "Result");
            LogPath = Path.Combine(RuntimePath, "Logs");
        }

        public void EnsureDirectories()
        {
            Directory.CreateDirectory(RuntimePath);
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(ResultPath);
            Directory.CreateDirectory(LogPath);
        }

        public string GetConfigFilePath(string fileName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            return Path.Combine(ConfigPath, fileName);
        }

        public string GetResultFilePath(string fileName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            return Path.Combine(ResultPath, fileName);
        }

        public string GetLogFilePath(string fileName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            return Path.Combine(LogPath, fileName);
        }
    }
}
