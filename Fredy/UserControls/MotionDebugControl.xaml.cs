using HAL;
using System;
using System.ComponentModel;
using System.Windows.Controls;

namespace Fredy.Drilling.Holes.UserControls
{
    public partial class MotionDebugControl : UserControl
    {
        private readonly MotionDebugViewModel? _viewModel;

        public MotionDebugControl()
        {
            InitializeComponent();

            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                _viewModel = new MotionDebugViewModel();
                DataContext = _viewModel;
                Unloaded += MotionDebugControl_Unloaded;
            }
        }

        public IMoton? CurrentMotion => _viewModel?.CurrentMotion;

        private void MotionDebugControl_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            Unloaded -= MotionDebugControl_Unloaded;
            _viewModel?.Dispose();
        }
    }
}
