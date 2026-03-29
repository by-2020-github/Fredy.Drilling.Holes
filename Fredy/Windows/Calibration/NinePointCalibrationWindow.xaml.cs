using Fredy.Drilling.Holes.ViewModels;
using System;
using System.Windows;

namespace Fredy.Drilling.Holes.Views
{
    public partial class NinePointCalibrationWindow : Window
    {
        public NinePointCalibrationWindow()
        {
            InitializeComponent();
            Loaded += NinePointCalibrationWindow_Loaded;
        }

        public NinePointCalibrationViewModel? ViewModel => DataContext as NinePointCalibrationViewModel;

        public NinePointCalibrationResult? CalibrationResult => ViewModel?.CalibrationResult;

        private void NinePointCalibrationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel is null)
            {
                return;
            }

            ViewModel.RequestClose -= ViewModel_RequestClose;
            ViewModel.RequestClose += ViewModel_RequestClose;
        }

        private void ViewModel_RequestClose(bool? dialogResult)
        {
            DialogResult = dialogResult;
            Close();
        }
    }
}
