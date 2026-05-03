using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Fredy.Drilling.Holes.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace Fredy.Drilling.Holes.Views
{
    /// <summary>
    /// CameraPunchOffsetCalibrationWindow.xaml 的交互逻辑
    /// </summary>
    public partial class CameraPunchOffsetCalibrationWindow : Window
    {
        public CameraPunchOffsetCalibrationWindow()
        {
            InitializeComponent();

            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext ??= App.ServiceProvider.GetRequiredService<CameraPunchOffsetCalibrationViewModel>();
                Closed += CameraPunchOffsetCalibrationWindow_Closed;
            }
        }

        private void CameraPunchOffsetCalibrationWindow_Closed(object? sender, EventArgs e)
        {
            Closed -= CameraPunchOffsetCalibrationWindow_Closed;
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
