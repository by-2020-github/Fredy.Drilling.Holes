using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using System.Collections.Generic;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class ProcessViewModel : ObservableObject
    {
        [ObservableProperty] private ProcessParams _data = new();

        // 下拉框选项
        public List<string> WorkpieceTypes { get; } = new() { "PS60-6X500-0.05X0.075X10", "Other-Type-01" };

        public ProcessViewModel()
        {
            // 初始化右侧 8 个深度项
            int[] defaultValues = { 400, 200, 300, 200, 100, 0, 0, 0 };
            for (int i = 0; i < 8; i++)
            {
                Data.PunchDepths.Add(new DepthItem
                {
                    Label = $"No.{i + 1}",
                    Value = defaultValues[i]
                });
            }
        }

        [RelayCommand]
        private void ApplyAndClose(System.Windows.Window window)
        {
            // 保存逻辑...
            window?.Close();
        }
    }
}