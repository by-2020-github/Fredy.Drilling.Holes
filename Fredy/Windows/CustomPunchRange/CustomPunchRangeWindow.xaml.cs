using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Windows;

namespace Fredy.Drilling.Holes.Windows.CustomPunchRange
{
    public partial class CustomPunchRangeWindow : Window
    {
        private readonly CustomPunchRangeViewModel _viewModel;

        public CustomPunchRangeWindow(int maxIndex, int startIndex, int endIndex)
        {
            InitializeComponent();
            _viewModel = new CustomPunchRangeViewModel(maxIndex, startIndex, endIndex);
            DataContext = _viewModel;
        }

        public int SelectedStartIndex => _viewModel.SelectedStartIndex;

        public int SelectedEndIndex => _viewModel.SelectedEndIndex;

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedEndIndex < _viewModel.SelectedStartIndex)
            {
                MessageBox.Show(this, "终止 Index 不能小于起始 Index。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                this,
                $"确认按范围 [{_viewModel.SelectedStartIndex}, {_viewModel.SelectedEndIndex}] 开始冲孔吗？",
                "开始确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                _viewModel.RestoreSelection();
                DialogResult = false;
                return;
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    internal partial class CustomPunchRangeViewModel : ObservableObject
    {
        private readonly int _initialStartIndex;
        private readonly int _initialEndIndex;
        private int _selectedStartIndex;
        private int _selectedEndIndex;

        public CustomPunchRangeViewModel(int maxIndex, int startIndex, int endIndex)
        {
            MaxIndex = Math.Max(1, maxIndex);
            _initialStartIndex = Math.Clamp(startIndex, 1, MaxIndex);
            _initialEndIndex = Math.Clamp(endIndex, _initialStartIndex, MaxIndex);
            _selectedStartIndex = _initialStartIndex;
            _selectedEndIndex = _initialEndIndex;
        }

        public int MaxIndex { get; }

        public int SelectedStartIndex
        {
            get => _selectedStartIndex;
            set
            {
                int clampedValue = Math.Clamp(value, 1, MaxIndex);
                if (SetProperty(ref _selectedStartIndex, clampedValue) && _selectedEndIndex < clampedValue)
                {
                    SelectedEndIndex = clampedValue;
                }

                OnPropertyChanged(nameof(RangeHint));
            }
        }

        public int SelectedEndIndex
        {
            get => _selectedEndIndex;
            set
            {
                int clampedValue = Math.Clamp(value, 1, MaxIndex);
                SetProperty(ref _selectedEndIndex, Math.Max(clampedValue, SelectedStartIndex));
                OnPropertyChanged(nameof(RangeHint));
            }
        }

        public string RangeHint => $"当前将执行 [{SelectedStartIndex}, {SelectedEndIndex}]，共 {SelectedEndIndex - SelectedStartIndex + 1} 个孔位。";

        public void RestoreSelection()
        {
            SelectedStartIndex = _initialStartIndex;
            SelectedEndIndex = _initialEndIndex;
        }
    }
}
