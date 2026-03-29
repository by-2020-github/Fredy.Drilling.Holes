using Common.Tools;
using Fredy.Drilling.Holes.Models;
using System;
using System.IO;
using System.Text.Json;

namespace Fredy.Drilling.Holes.Services
{
    public class ConfigService
    {
        private const string ConfigFileName = "config.json";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly object _syncRoot = new();
        private AppConfig _currentConfig;

        public ConfigService()
        {
            _currentConfig = LoadOrCreate();
        }

        public AppConfig CurrentConfig
        {
            get
            {
                lock (_syncRoot)
                {
                    return Clone(_currentConfig);
                }
            }
        }

        public AppConfig Reload()
        {
            lock (_syncRoot)
            {
                _currentConfig = LoadOrCreate();
                return Clone(_currentConfig);
            }
        }

        public void SaveWithArchive(AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            lock (_syncRoot)
            {
                var configFilePath = GetConfigFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)!);

                if (File.Exists(configFilePath))
                {
                    var archiveDirectory = Path.Combine(PathManager.ConfigPath, "Archive");
                    Directory.CreateDirectory(archiveDirectory);
                    var archiveFileName = $"config-{DateTime.Now:yyyyMMdd-HHmmss}.json";
                    var archivePath = Path.Combine(archiveDirectory, archiveFileName);
                    File.Copy(configFilePath, archivePath, overwrite: true);
                }

                var normalized = Clone(config);
                var json = JsonSerializer.Serialize(normalized, JsonOptions);
                File.WriteAllText(configFilePath, json);
                _currentConfig = normalized;
            }
        }

        private static AppConfig Clone(AppConfig config)
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }

        private static AppConfig LoadOrCreate()
        {
            var configFilePath = GetConfigFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)!);

            if (!File.Exists(configFilePath))
            {
                var defaultConfig = new AppConfig();
                var json = JsonSerializer.Serialize(defaultConfig, JsonOptions);
                File.WriteAllText(configFilePath, json);
                return defaultConfig;
            }

            try
            {
                var json = File.ReadAllText(configFilePath);
                return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        private static string GetConfigFilePath()
        {
            return PathManager.GetConfigFilePath(ConfigFileName);
        }
    }
}
