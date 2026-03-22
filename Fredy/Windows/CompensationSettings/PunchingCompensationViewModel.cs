using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Linq;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class PunchingCompensationViewModel : ObservableObject
    {
        private readonly ILogger<PunchingCompensationViewModel> _logger;

        public ObservableCollection<PunchingCompensationItem> CompensationItems { get; } = new();
        public PunchingCompensationViewModel() { }
        public PunchingCompensationViewModel(ILogger<PunchingCompensationViewModel> logger)
        {
            _logger = logger;

            // 初始化数据，假设有32圈（根据截图滚动条推测）
            for (int i = 1; i <= 32; i++)
            {
                CompensationItems.Add(new PunchingCompensationItem { Index = i });
            }

            _logger.LogInformation("冲孔补偿视图模型已初始化");
        }

        [RelayCommand]
        private void ResetFirstPass()
        {
            foreach (var item in CompensationItems) item.FirstPassComp = 0;
            _logger.LogInformation("头道补偿参数已重置");
        }

        [RelayCommand]
        private void ResetSecondPass()
        {
            foreach (var item in CompensationItems) item.SecondPassComp = 0;
            _logger.LogInformation("二道补偿参数已重置");
        }

        [RelayCommand]
        private void ResetSurfaceReduction()
        {
            foreach (var item in CompensationItems) item.SurfaceReduction = 0;
            _logger.LogInformation("磨面量参数已重置");
        }

        [RelayCommand]
        private void Confirm()
        {
            _logger.LogInformation("冲孔补偿参数已确认，共 {Count} 项", CompensationItems.Count);
        }
    }
}