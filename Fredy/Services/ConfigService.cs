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

                var normalized = Normalize(Clone(config));
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
                var defaultConfig = Normalize(new AppConfig());
                var json = JsonSerializer.Serialize(defaultConfig, JsonOptions);
                File.WriteAllText(configFilePath, json);
                return defaultConfig;
            }

            try
            {
                var json = File.ReadAllText(configFilePath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
                var normalized = Normalize(config, json);
                var normalizedJson = JsonSerializer.Serialize(normalized, JsonOptions);
                if (!string.Equals(json, normalizedJson, StringComparison.Ordinal))
                {
                    File.WriteAllText(configFilePath, normalizedJson);
                }

                return normalized;
            }
            catch
            {
                return Normalize(new AppConfig());
            }
        }

        private static AppConfig Normalize(AppConfig config, string? originalJson = null)
        {
            ArgumentNullException.ThrowIfNull(config);

            var legacyFastHomeSearchSpeed = 0d;
            var legacySlowHomeSearchSpeed = 0d;
            var legacyHomeTimeoutMs = 0;

            if (!string.IsNullOrWhiteSpace(originalJson))
            {
                using var document = JsonDocument.Parse(originalJson);
                var root = document.RootElement;

                if (TryGetPositiveDouble(root, nameof(AppConfig.HomeSearchSpeed), out var homeSearchSpeed))
                {
                    legacyFastHomeSearchSpeed = homeSearchSpeed;
                }

                if (root.TryGetProperty(nameof(AppConfig.AdtHoming), out var adtHoming))
                {
                    if (TryGetPositiveInt(adtHoming, nameof(AdtHomingConfig.HomeTimeoutMs), out var homeTimeoutMs))
                    {
                        legacyHomeTimeoutMs = homeTimeoutMs;
                    }

                    if (TryGetPositiveDouble(adtHoming, nameof(AdtHomingConfig.SlowHomeSpeed), out var slowHomeSearchSpeed))
                    {
                        legacySlowHomeSearchSpeed = slowHomeSearchSpeed;
                    }
                }
            }

            AxisHomingDefaults.ApplyDefaults(config, legacyFastHomeSearchSpeed, legacySlowHomeSearchSpeed, legacyHomeTimeoutMs);
            config.HomeSearchSpeed = 0d;
            config.AdtHoming ??= new AdtHomingConfig();
            config.AdtHoming.HomeTimeoutMs = 0;
            config.AdtHoming.SlowHomeSpeed = 0d;
            config.CameraPunchOffsetCalibrationTestPunch ??= new CameraPunchOffsetCalibrationTestPunchConfig();
            if (config.FastToSafeZSpeed < 0d)
            {
                config.FastToSafeZSpeed = 0d;
            }

            if (config.PunchDownSpeed < 0d)
            {
                config.PunchDownSpeed = 0d;
            }

            if (string.IsNullOrWhiteSpace(config.SurfaceDetectionMode))
            {
                config.SurfaceDetectionMode = "Latch";
            }

            if (config.SurfaceDetectPollIntervalMs <= 0)
            {
                config.SurfaceDetectPollIntervalMs = 10;
            }

            if (string.IsNullOrWhiteSpace(config.CameraPunchOffsetCalibrationTestPunch.SurfaceDetectionMode))
            {
                config.CameraPunchOffsetCalibrationTestPunch.SurfaceDetectionMode = config.SurfaceDetectionMode;
            }

            if (config.CameraPunchOffsetCalibrationTestPunch.SurfaceDetectPollIntervalMs <= 0)
            {
                config.CameraPunchOffsetCalibrationTestPunch.SurfaceDetectPollIntervalMs = config.SurfaceDetectPollIntervalMs;
            }

            return config;
        }

        private static bool TryGetPositiveDouble(JsonElement element, string propertyName, out double value)
        {
            value = 0d;
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number) && number > 0d)
            {
                value = number;
                return true;
            }

            return false;
        }

        private static bool TryGetPositiveInt(JsonElement element, string propertyName, out int value)
        {
            value = 0;
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number) && number > 0)
            {
                value = number;
                return true;
            }

            return false;
        }

        private static string GetConfigFilePath()
        {
            return PathManager.GetConfigFilePath(ConfigFileName);
        }
    }
}
