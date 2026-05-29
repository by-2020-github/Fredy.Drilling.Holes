using HAL;
using Serilog;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
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
                _viewModel = new MotionDebugViewModel(Log.Logger);
                DataContext = _viewModel;
                _viewModel.Logs.CollectionChanged += Logs_CollectionChanged;
                Unloaded += MotionDebugControl_Unloaded;
            }
        }

        public IMoton? CurrentMotion => _viewModel?.CurrentMotion;

        private void Logs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null || e.NewItems.Count == 0)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => LogsListBox.ScrollIntoView(LogsListBox.Items.Count > 0 ? LogsListBox.Items[^1] : null)));
        }

        private void MotionDebugControl_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            Unloaded -= MotionDebugControl_Unloaded;

            if (_viewModel is not null)
            {
                _viewModel.Logs.CollectionChanged -= Logs_CollectionChanged;
            }

            _viewModel?.Dispose();
        }
    }
}
