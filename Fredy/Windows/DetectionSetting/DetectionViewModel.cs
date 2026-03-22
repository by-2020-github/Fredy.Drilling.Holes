using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class DetectionViewModel : ObservableObject
    {
        private readonly ILogger<DetectionViewModel> _logger;

        // 探测项目列表
        public ObservableCollection<DetectionRingItem> RingItems { get; } = new();

        // 探测偏移量阈值
        [ObservableProperty]
        private int _offsetThreshold;
        public DetectionViewModel() { }
        public DetectionViewModel(ILogger<DetectionViewModel> logger)
        {
            _logger = logger;

            // 初始化数据，假设32圈
            for (int i = 1; i <= 32; i++)
            {
                RingItems.Add(new DetectionRingItem { Index = i, DetectionCount = 0 });
            }

            _logger.LogInformation("头道探测视图模型已初始化");
        }

        [RelayCommand]
        private void Reset()
        {
            // 重置所有探测次数和阈值
            foreach (var item in RingItems)
            {
                item.DetectionCount = 0;
            }
            OffsetThreshold = 0;
            _logger.LogInformation("头道探测参数已重置");
        }

        [RelayCommand]
        private void Confirm()
        {
            _logger.LogInformation("头道探测参数已确认，阈值: {OffsetThreshold}", OffsetThreshold);
        }
    }
}