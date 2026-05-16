using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Services;
using Serilog;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class CenterRoiBinarySettingsViewModel : ObservableObject
    {
        private readonly ConfigService _configService;
        private readonly ILogger _logger;
        private int _roiWidth;
        private int _roiHeight;
        private int _threshold;
        private bool _invert;

        public int RoiWidth
        {
            get => _roiWidth;
            set
            {
                var normalized = Math.Max(1, value);
                if (SetProperty(ref _roiWidth, normalized))
                {
                    SettingsChanged?.Invoke();
                }
            }
        }

        public int RoiHeight
        {
            get => _roiHeight;
            set
            {
                var normalized = Math.Max(1, value);
                if (SetProperty(ref _roiHeight, normalized))
                {
                    SettingsChanged?.Invoke();
                }
            }
        }

        public int Threshold
        {
            get => _threshold;
            set
            {
                var normalized = Math.Clamp(value, 0, 255);
                if (SetProperty(ref _threshold, normalized))
                {
                    SettingsChanged?.Invoke();
                }
            }
        }

        public bool Invert
        {
            get => _invert;
            set
            {
                if (SetProperty(ref _invert, value))
                {
                    SettingsChanged?.Invoke();
                }
            }
        }

        public IRelayCommand SaveConfigCommand { get; }

        public event Action? SettingsChanged;

        public CenterRoiBinarySettingsViewModel(ConfigService configService, ILogger logger)
        {
            _configService = configService;
            _logger = (logger ?? Log.Logger).ForContext<CenterRoiBinarySettingsViewModel>();

            var config = _configService.CurrentConfig;
            RoiWidth = config.CenterRoiWidth;
            RoiHeight = config.CenterRoiHeight;
            Threshold = config.CenterRoiThreshold;
            Invert = config.CenterRoiBinaryInvert;
            SaveConfigCommand = new RelayCommand(SaveConfig);
        }

        private void SaveConfig()
        {
            var config = _configService.CurrentConfig;
            config.CenterRoiWidth = Math.Max(1, RoiWidth);
            config.CenterRoiHeight = Math.Max(1, RoiHeight);
            config.CenterRoiThreshold = Math.Clamp(Threshold, 0, 255);
            config.CenterRoiBinaryInvert = Invert;
            _configService.SaveWithArchive(config);
            _logger.Information("中心ROI二值化参数已保存");
        }
    }
}
