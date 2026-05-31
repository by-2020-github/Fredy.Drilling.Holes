using Common.Tools;
using Fredy.Drilling.Holes.Services;
using Fredy.Drilling.Holes.ViewModels;
using Fredy.Drilling.Holes.Views;
using Fredy.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HAL;
using BLL;

namespace Fredy.Drilling.Holes
{
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; } = null!;
        private static bool _hardwareShutdown;
        private static readonly TimeSpan ExistingInstanceCloseTimeout = TimeSpan.FromSeconds(5);

        protected override void OnStartup(StartupEventArgs e)
        {
            if (!TryClosePreviousInstances())
            {
                MessageBox.Show("检测到已运行实例且未能自动关闭，请手动结束旧实例后重试。", "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            InitializePathManager();

            var logStore = new AppLogStore();
            ConfigureLogging(logStore);
            RegisterGlobalExceptionHandlers();

            try
            {
                var serviceCollection = new ServiceCollection();
                var configService = new ConfigService();
                var hardwareInitializationResult = EvaluateHardwareInitialization(configService.CurrentConfig);
                ConfigureServices(serviceCollection, logStore, configService, hardwareInitializationResult);

                ServiceProvider = serviceCollection.BuildServiceProvider();
                InitializeMotionService();
                InitializeRecipeService();

                LogStartupInformation();
                Log.Information("应用程序启动完成");

                base.OnStartup(e);

                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                MainWindow = mainWindow;
                mainWindow.Show();

                ShowHardwareInitializationMessage(hardwareInitializationResult);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "应用程序启动失败");
                throw;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ShutdownHardware();
            Log.Information("应用程序正在退出");
            Log.CloseAndFlush();
            base.OnExit(e);
        }

        public static void ShutdownHardware()
        {
            if (_hardwareShutdown || ServiceProvider is null)
            {
                return;
            }

            _hardwareShutdown = true;

            var hardwareStateService = ServiceProvider.GetService<IHardwareStateService>();
            var camera = ServiceProvider.GetService<ICamera>();
            var hardwareController = ServiceProvider.GetService<IHardwareController>();
            var motionService = ServiceProvider.GetService<IMotionService>();

            try
            {
                if (Current?.MainWindow?.DataContext is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch
            {
            }

            try
            {
                hardwareStateService?.Dispose();
            }
            catch
            {
            }

            try
            {
                camera?.Close();
                camera?.Dispose();
            }
            catch
            {
            }

            try
            {
                hardwareController?.Close();
            }
            catch
            {
            }

            try
            {
                motionService?.DisableAll();
            }
            catch
            {
            }

            try
            {
                if (motionService?.Hardware is IDisposable motionDisposable)
                {
                    motionDisposable.Dispose();
                }
            }
            catch
            {
            }
        }

        private static void ConfigureLogging(IAppLogStore logStore)
        {
            var logDirectory = PathManager.LogPath;
            var logFilePath = Path.Combine(logDirectory, $"fredy-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            Directory.CreateDirectory(logDirectory);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.FromLogContext()
                .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}", restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Verbose)
                .WriteTo.File(
                    logFilePath,
                    retainedFileCountLimit: 14,
                    shared: true,
                    restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Verbose,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(new SerilogObservableSink(logStore), restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Verbose)
                .CreateLogger();
        }

        private static bool TryClosePreviousInstances()
        {
            using var currentProcess = Process.GetCurrentProcess();
            string? currentExecutablePath = TryGetProcessExecutablePath(currentProcess);
            bool allClosed = true;

            foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
            {
                using (process)
                {
                    if (!IsSameExecutableProcess(process, currentProcess.Id, currentProcess.SessionId, currentExecutablePath))
                    {
                        continue;
                    }

                    if (!TryCloseExistingProcess(process))
                    {
                        allClosed = false;
                    }
                }
            }

            return allClosed;
        }

        private static bool IsSameExecutableProcess(Process process, int currentProcessId, int currentSessionId, string? currentExecutablePath)
        {
            if (process.Id == currentProcessId)
            {
                return false;
            }

            if (process.SessionId != currentSessionId)
            {
                return false;
            }

            string? processExecutablePath = TryGetProcessExecutablePath(process);
            if (!string.IsNullOrWhiteSpace(currentExecutablePath) && !string.IsNullOrWhiteSpace(processExecutablePath))
            {
                return string.Equals(currentExecutablePath, processExecutablePath, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private static bool TryCloseExistingProcess(Process process)
        {
            try
            {
                if (process.HasExited)
                {
                    return true;
                }

                if (process.CloseMainWindow())
                {
                    if (process.WaitForExit((int)ExistingInstanceCloseTimeout.TotalMilliseconds))
                    {
                        return true;
                    }
                }

                process.Kill(entireProcessTree: true);
                return process.WaitForExit((int)ExistingInstanceCloseTimeout.TotalMilliseconds);
            }
            catch
            {
                return false;
            }
        }

        private static string? TryGetProcessExecutablePath(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        private void ConfigureServices(
            IServiceCollection services,
            IAppLogStore logStore,
            ConfigService configService,
            HardwareInitializationResult hardwareInitializationResult)
        {
            services.AddSingleton(configService);
            var config = configService.CurrentConfig;

            // 注册硬件 - 相机
            ICamera baseCamera;
            if (config.Camera.CameraType == "模拟相机" || hardwareInitializationResult.UseSimulatedCamera)
            {
                baseCamera = new CameraSimulator();
            }
            else
            {
                baseCamera = new HkCamera();
            }

            services.AddSingleton<ICamera>(baseCamera);

            // 注册硬件 - 运动控制与 IO
            if (config.MotionController.ControllerType == "ADT8940" && !hardwareInitializationResult.UseSimulatedMotion)
            {
                services.AddSingleton<MotionAdt8940>(sp => new MotionAdt8940(sp.GetRequiredService<Serilog.ILogger>()));
                services.AddSingleton<IMoton>(sp => sp.GetRequiredService<MotionAdt8940>());
                services.AddSingleton<IIOCard>(sp => sp.GetRequiredService<MotionAdt8940>());
                services.AddSingleton<IHardwareController, Adt8940Controller>();
            }
            else
            {
                services.AddSingleton<IMoton>(sp => new MotionSimulator(sp.GetRequiredService<Serilog.ILogger>()));
                services.AddSingleton<IIOCard, IOCardSimulator>();
                services.AddSingleton<IHardwareController, MockHardwareController>();
            }

            // 注册 BLL 的相关服务
            services.AddSingleton<IMotionService, MotionManager>();
            services.AddSingleton<IHardwareStateService, HardwareStateService>();
            services.AddSingleton<ISecondPassAlignmentContext, SecondPassAlignmentContext>();
            services.AddSingleton(_ => new PathManager(AppContext.BaseDirectory));
            services.AddSingleton<RecipeService>();
            services.AddSingleton<CoordinateService>();

            services.AddSingleton(logStore);
            services.AddSingleton<IAppLogStore>(logStore);
            services.AddSingleton<IAppLogExportService, AppLogExportService>();
            services.AddSingleton<Serilog.ILogger>(_ => Log.Logger);
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(Log.Logger, dispose: false);
            });

            // Windows
            services.AddTransient<ScanViewModel>();
            services.AddTransient<ScanWindow>(s => new ScanWindow()
            {
                DataContext = s.GetRequiredService<ScanViewModel>()
            });

            services.AddTransient<ConfigViewModel>();
            services.AddTransient<ConfigWindow>(); // 每次打开配置页面都创建一个新的实例

            services.AddSingleton<MainViewModel>();
            services.AddTransient<CameraPunchOffsetCalibrationViewModel>();
            services.AddTransient<DetectionViewModel>();
            services.AddTransient<PunchingCompensationViewModel>();
            services.AddTransient<SecondPassDetectionViewModel>();
            services.AddTransient<WorkpieceCenterCalibrationViewModel>();

            services.AddSingleton<MainWindow>();
        }

        private static HardwareInitializationResult EvaluateHardwareInitialization(Models.AppConfig config)
        {
            bool useSimulatedCamera = config.Camera.CameraType == "模拟相机";
            bool useSimulatedMotion = config.MotionController.ControllerType != "ADT8940";
            string? cameraFallbackReason = null;
            string? motionFallbackReason = null;

            if (!useSimulatedCamera)
            {
                try
                {
                    using ICamera camera = new HkCamera();
                    if (!camera.Open())
                    {
                        useSimulatedCamera = true;
                        cameraFallbackReason = "真实相机初始化失败，已自动切换为模拟相机。";
                    }
                }
                catch (Exception ex)
                {
                    useSimulatedCamera = true;
                    cameraFallbackReason = $"真实相机初始化失败，已自动切换为模拟相机。原因：{ex.Message}";
                }
            }

            if (!useSimulatedMotion)
            {
                try
                {
                    var motion = new MotionAdt8940(Log.Logger);
                    var motionService = new MotionManager(motion);
                    motionService.EnableAll();
                    motionService.DisableAll();
                }
                catch (Exception ex)
                {
                    useSimulatedMotion = true;
                    motionFallbackReason = $"真实运动控制器初始化失败，已自动切换为模拟运动与 IO 设备。原因：{ex.Message}";
                }
            }

            if (cameraFallbackReason is not null)
            {
                Log.Warning(cameraFallbackReason);
            }

            if (motionFallbackReason is not null)
            {
                Log.Warning(motionFallbackReason);
            }

            return new HardwareInitializationResult(
                useSimulatedCamera,
                useSimulatedMotion,
                cameraFallbackReason,
                motionFallbackReason);
        }

        private static void ShowHardwareInitializationMessage(HardwareInitializationResult result)
        {
            if (string.IsNullOrWhiteSpace(result.CameraFallbackReason) && string.IsNullOrWhiteSpace(result.MotionFallbackReason))
            {
                return;
            }

            var message = string.Join(
                Environment.NewLine,
                new[] { result.CameraFallbackReason, result.MotionFallbackReason }.Where(static x => !string.IsNullOrWhiteSpace(x)));

            MessageBox.Show(message, "硬件初始化提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static void InitializePathManager()
        {
            PathManager.SetRuntimePath(AppContext.BaseDirectory);
            PathManager.EnsureDirectories();
        }

        private static void InitializeRecipeService()
        {
            var recipeService = ServiceProvider.GetRequiredService<RecipeService>();
            var recipeCount = recipeService.Recipes.Count;
            Log.Information("RecipeService 初始化完成，已加载 {RecipeCount} 个配方", recipeCount);
        }

        private static void InitializeMotionService()
        {
            var config = ServiceProvider.GetRequiredService<ConfigService>().CurrentConfig;
            var motionService = ServiceProvider.GetRequiredService<IMotionService>();

            motionService.ConfigureAxes(
                BuildAxisParam(config.XAxis),
                BuildAxisParam(config.YAxis),
                BuildAxisParam(config.ZAxis));

            if (motionService is MotionManager motionManager)
            {
                motionManager.ConfigureZHomeLift(config.AdtHoming?.ZHomeLiftMm ?? 0d);
            }

            if (motionService.Hardware is MotionAdt8940 adt8940)
            {
                adt8940.ConfigureHoming(BuildAdtHomingOptions(config));
            }
        }

        private static AxisParam BuildAxisParam(Models.AxisParamConfig axisConfig)
        {
            return new AxisParam(
                axisConfig.AxisNo,
                axisConfig.Velocity,
                axisConfig.Acceleration,
                axisConfig.Deceleration,
                axisConfig.LeftLimit,
                axisConfig.RightLimit,
                axisConfig.PulsesPerMillimeter > 0 ? axisConfig.PulsesPerMillimeter : 1d,
                axisConfig.UseActualPositionFeedback,
                axisConfig.InPositionTolerance,
                axisConfig.FastHomeSearchSpeed,
                axisConfig.SlowHomeSearchSpeed,
                axisConfig.HomeTimeoutMs,
                axisConfig.HomeMaxRetryCount);
        }

        private static MotionAdt8940.HomingOptions BuildAdtHomingOptions(Models.AppConfig config)
        {
            var homing = config.AdtHoming ?? new Models.AdtHomingConfig();
            return new MotionAdt8940.HomingOptions(
                ResolveSharedFastHomeSearchSpeed(config),
                config.IsIoHome,
                config.IsLatch,
                config.IsGratingHome,
                BuildHomingPort(config.XLimitPort),
                BuildHomingPort(config.YLimitPort),
                BuildHomingPort(homing.ZLimitPort),
                BuildHomingPort(homing.XGratingPort),
                BuildHomingPort(homing.YGratingPort),
                ResolveSharedHomeTimeoutMs(config),
                homing.HomeBackoffMm,
                homing.ZHomeTowardPositiveDirection,
                homing.SlowHomeStartSpeed,
                ResolveSharedSlowHomeSearchSpeed(config),
                homing.SlowHomeAcceleration,
                homing.GratingHomeStartSpeed,
                homing.GratingHomeSpeed,
                homing.GratingHomeAcceleration);
        }

        private static double ResolveSharedFastHomeSearchSpeed(Models.AppConfig config)
        {
            return ResolveFirstPositive(
                config.XAxis?.FastHomeSearchSpeed ?? 0d,
                config.YAxis?.FastHomeSearchSpeed ?? 0d,
                config.ZAxis?.FastHomeSearchSpeed ?? 0d,
                3.0d);
        }

        private static double ResolveSharedSlowHomeSearchSpeed(Models.AppConfig config)
        {
            return ResolveFirstPositive(
                config.XAxis?.SlowHomeSearchSpeed ?? 0d,
                config.YAxis?.SlowHomeSearchSpeed ?? 0d,
                config.ZAxis?.SlowHomeSearchSpeed ?? 0d,
                0.5d);
        }

            private static int ResolveSharedHomeTimeoutMs(Models.AppConfig config)
            {
                return ResolveFirstPositive(
                config.XAxis?.HomeTimeoutMs ?? 0,
                config.YAxis?.HomeTimeoutMs ?? 0,
                config.ZAxis?.HomeTimeoutMs ?? 0,
                10000);
            }

        private static double ResolveFirstPositive(double first, double second, double third, double fallback)
        {
            if (first > 0d)
            {
                return first;
            }

            if (second > 0d)
            {
                return second;
            }

            if (third > 0d)
            {
                return third;
            }

            return fallback;
        }

        private static int ResolveFirstPositive(int first, int second, int third, int fallback)
        {
            if (first > 0)
            {
                return first;
            }

            if (second > 0)
            {
                return second;
            }

            if (third > 0)
            {
                return third;
            }

            return fallback;
        }

        private static MotionAdt8940.HomingPort BuildHomingPort(Models.PortItem port)
        {
            return new MotionAdt8940.HomingPort(port.PortIndex, port.IsNegative ?? port.IsLowLevelActive, port.IsLowLevelActive);
        }

        private static void LogStartupInformation()
        {
            var assembly = Assembly.GetExecutingAssembly().GetName();
            var version = assembly.Version?.ToString() ?? "Unknown";

            Log.Information(
                "启动信息 Machine={MachineName} User={UserName} OS={OSDescription} Framework={FrameworkDescription} Architecture={ProcessArchitecture} BaseDirectory={BaseDirectory} Version={Version}",
                Environment.MachineName,
                Environment.UserName,
                RuntimeInformation.OSDescription,
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.ProcessArchitecture,
                AppContext.BaseDirectory,
                version);
        }

        private void RegisterGlobalExceptionHandlers()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error(e.Exception, "捕获到 UI 线程未处理异常");
        }

        private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
            {
                Log.Fatal(exception, "捕获到 AppDomain 未处理异常");
                return;
            }

            Log.Fatal("捕获到 AppDomain 未处理异常，异常对象类型: {ExceptionObjectType}", e.ExceptionObject?.GetType().FullName ?? "Unknown");
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Log.Error(e.Exception, "捕获到未观察的任务异常");
            e.SetObserved();
        }

        private sealed record HardwareInitializationResult(
            bool UseSimulatedCamera,
            bool UseSimulatedMotion,
            string? CameraFallbackReason,
            string? MotionFallbackReason);
    }
}