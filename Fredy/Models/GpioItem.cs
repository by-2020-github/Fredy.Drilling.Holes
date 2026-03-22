using CommunityToolkit.Mvvm.ComponentModel;

namespace Fredy.Drilling.Holes.Models
{
    public partial class GpioItem : ObservableObject
    {
        [ObservableProperty] private int _id;
        [ObservableProperty] private bool _isActive; // 亮起状态
    }
}