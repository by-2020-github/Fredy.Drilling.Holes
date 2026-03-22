using CommunityToolkit.Mvvm.ComponentModel;

namespace Fredy.Drilling.Holes.Models
{
    public partial class PunchingCompensationItem : ObservableObject
    {
        [ObservableProperty]
        private int _index; // 圈数索引

        [ObservableProperty]
        private int _firstPassComp; // 头道补偿

        [ObservableProperty]
        private int _secondPassComp; // 二道补偿

        [ObservableProperty]
        private int _surfaceReduction; // 二道表面降低
    }
}