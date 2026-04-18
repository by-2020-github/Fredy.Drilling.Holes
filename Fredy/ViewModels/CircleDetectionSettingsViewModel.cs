using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Services;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class CircleDetectionSettingsViewModel : ObservableObject
    {
        private readonly ConfigService _configService;
        
        [ObservableProperty]
        private double _minRadius;
        
        [ObservableProperty]
        private double _maxRadius;
        
        [ObservableProperty]
        private double _param1;
        
        [ObservableProperty]
        private double _param2;
        
        [ObservableProperty]
        private bool _isDarkHoleTarget;
        
        public event Action OnSettingsChanged;

        public CircleDetectionSettingsViewModel(ConfigService configService)
        {
            _configService = configService;
            var config = _configService.CurrentConfig;
            
            MinRadius = config.CircleMinRadius;
            MaxRadius = config.CircleMaxRadius;
            Param1 = config.CircleParam1;
            Param2 = config.CircleParam2;
            IsDarkHoleTarget = config.CircleIsDarkTarget;
        }
        
        protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            
            if (e.PropertyName != nameof(MinRadius) &&
                e.PropertyName != nameof(MaxRadius) &&
                e.PropertyName != nameof(Param1) &&
                e.PropertyName != nameof(Param2) &&
                e.PropertyName != nameof(IsDarkHoleTarget))
                    return;

            OnSettingsChanged?.Invoke();
        }

        [RelayCommand]
        private void SaveConfig()
        {
            var config = _configService.CurrentConfig;
            config.CircleMinRadius = MinRadius;
            config.CircleMaxRadius = MaxRadius;
            config.CircleParam1 = Param1;
            config.CircleParam2 = Param2;
            config.CircleIsDarkTarget = IsDarkHoleTarget;
            
            _configService.SaveWithArchive(config);
        }
    }
}
