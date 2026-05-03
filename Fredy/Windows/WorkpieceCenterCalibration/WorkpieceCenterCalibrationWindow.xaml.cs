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
using System;
using System.Windows;
using Fredy.Drilling.Holes.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fredy.Drilling.Holes.Views
{
    public partial class WorkpieceCenterCalibrationWindow : Window
    {
        public WorkpieceCenterCalibrationWindow()
        {
            InitializeComponent();
            DataContext = App.ServiceProvider.GetRequiredService<WorkpieceCenterCalibrationViewModel>();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
