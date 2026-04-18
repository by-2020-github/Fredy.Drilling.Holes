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

        public static string RuntimePath { get; private set; } = string.Empty;
        public static string RecipePath { get; private set; } = string.Empty;

        public static string ConfigPath { get; private set; } = string.Empty;

        public static string ResultPath { get; private set; } = string.Empty;

        public static string LogPath { get; private set; } = string.Empty;

        public static void SetRuntimePath(string runtimePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimePath);

            RuntimePath = Path.GetFullPath(runtimePath);
            ConfigPath = Path.Combine(RuntimePath, "Config");
            ResultPath = Path.Combine(RuntimePath, "Result");
            LogPath = Path.Combine(RuntimePath, "Logs");
            RecipePath = Path.Combine(RuntimePath, "Recipe");
        }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(RuntimePath);
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(ResultPath);
            Directory.CreateDirectory(LogPath);
        }

        public static string GetConfigFilePath(string fileName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            return Path.Combine(ConfigPath, fileName);
        }

        public static string GetResultFilePath(string fileName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            return Path.Combine(ResultPath, fileName);
        }

        public static string GetLogFilePath(string fileName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            return Path.Combine(LogPath, fileName);
        }
    }
}
