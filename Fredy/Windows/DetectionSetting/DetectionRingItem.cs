using CommunityToolkit.Mvvm.ComponentModel;

namespace Fredy.Drilling.Holes.Models
{
    public partial class DetectionRingItem : ObservableObject
    {
        [ObservableProperty]
        private int _index; // 圈数索引

        [ObservableProperty]
        private int _detectionCount; // 探测次数
    }
}