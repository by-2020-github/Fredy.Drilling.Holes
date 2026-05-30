using System;

namespace Fredy.Drilling.Holes.Models
{
    public static class AxisHomingDefaults
    {
        public const double DefaultFastHomeSearchSpeed = 3.0d;
        public const double DefaultSlowHomeSearchSpeed = 0.5d;
        public const int DefaultHomeTimeoutMs = 10000;
        public const int DefaultHomeMaxRetryCount = 3;

        public static void ApplyDefaults(AppConfig config, double legacyFastHomeSearchSpeed = 0d, double legacySlowHomeSearchSpeed = 0d, int legacyHomeTimeoutMs = 0)
        {
            ArgumentNullException.ThrowIfNull(config);

            config.XAxis = NormalizeAxisConfig(config.XAxis, 1, legacyFastHomeSearchSpeed, legacySlowHomeSearchSpeed, legacyHomeTimeoutMs);
            config.YAxis = NormalizeAxisConfig(config.YAxis, 2, legacyFastHomeSearchSpeed, legacySlowHomeSearchSpeed, legacyHomeTimeoutMs);
            config.ZAxis = NormalizeAxisConfig(config.ZAxis, 3, legacyFastHomeSearchSpeed, legacySlowHomeSearchSpeed, legacyHomeTimeoutMs);
        }

        public static AxisParamConfig NormalizeAxisConfig(AxisParamConfig? axisConfig, int axisNo, double legacyFastHomeSearchSpeed = 0d, double legacySlowHomeSearchSpeed = 0d, int legacyHomeTimeoutMs = 0)
        {
            axisConfig ??= new AxisParamConfig();
            axisConfig.AxisNo = axisConfig.AxisNo > 0 ? axisConfig.AxisNo : axisNo;
            axisConfig.PulsesPerMillimeter = axisConfig.PulsesPerMillimeter > 0d ? axisConfig.PulsesPerMillimeter : 1d;
            axisConfig.FastHomeSearchSpeed = ResolvePositiveDouble(axisConfig.FastHomeSearchSpeed, legacyFastHomeSearchSpeed, DefaultFastHomeSearchSpeed);
            axisConfig.SlowHomeSearchSpeed = ResolvePositiveDouble(axisConfig.SlowHomeSearchSpeed, legacySlowHomeSearchSpeed, DefaultSlowHomeSearchSpeed);
            axisConfig.HomeTimeoutMs = ResolvePositiveInt(axisConfig.HomeTimeoutMs, legacyHomeTimeoutMs, DefaultHomeTimeoutMs);
            axisConfig.HomeMaxRetryCount = ResolvePositiveInt(axisConfig.HomeMaxRetryCount, DefaultHomeMaxRetryCount);
            return axisConfig;
        }

        public static double ResolveSharedFastHomeSearchSpeed(AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return ResolvePositiveDouble(config.XAxis?.FastHomeSearchSpeed ?? 0d, config.YAxis?.FastHomeSearchSpeed ?? 0d, config.ZAxis?.FastHomeSearchSpeed ?? 0d, DefaultFastHomeSearchSpeed);
        }

        public static double ResolveSharedSlowHomeSearchSpeed(AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return ResolvePositiveDouble(config.XAxis?.SlowHomeSearchSpeed ?? 0d, config.YAxis?.SlowHomeSearchSpeed ?? 0d, config.ZAxis?.SlowHomeSearchSpeed ?? 0d, DefaultSlowHomeSearchSpeed);
        }

        public static int ResolveSharedHomeTimeoutMs(AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return ResolvePositiveInt(config.XAxis?.HomeTimeoutMs ?? 0, config.YAxis?.HomeTimeoutMs ?? 0, config.ZAxis?.HomeTimeoutMs ?? 0, DefaultHomeTimeoutMs);
        }

        private static double ResolvePositiveDouble(params double[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] > 0d)
                {
                    return values[i];
                }
            }

            return DefaultFastHomeSearchSpeed;
        }

        private static int ResolvePositiveInt(params int[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] > 0)
                {
                    return values[i];
                }
            }

            return DefaultHomeTimeoutMs;
        }
    }
}