using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class SecondPassDetectionViewModel : ObservableObject
    {
     
        private readonly ILogger<SecondPassDetectionViewModel>? _logger;

        public ObservableCollection<SecondPassDetectionItem> DetectionItems { get; } = new();

        [ObservableProperty]
        private int _offsetThreshold = 35; // 对应截图中的初始值 35

        public SecondPassDetectionViewModel() : this(null)
        {
        }

        public SecondPassDetectionViewModel(ILogger<SecondPassDetectionViewModel>? logger)
        {
            _logger = logger;

            // 默认初始化 32 圈数据
            for (int i = 1; i <= 32; i++)
            {
                DetectionItems.Add(new SecondPassDetectionItem { Index = i, DetectionCount = 0 });
            }

            _logger?.LogInformation("二道探测视图模型已初始化");
        }

        [RelayCommand]
        private void Reset()
        {
            foreach (var item in DetectionItems) item.DetectionCount = 0;
            OffsetThreshold = 0;
            _logger?.LogInformation("二道探测参数已重置");
        }

        [RelayCommand]
        private void Confirm()
        {
            _logger?.LogInformation("二道探测参数已确认，阈值: {OffsetThreshold}", OffsetThreshold);
        }
    }
}