using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Fredy.Drilling.Holes.Views
{
    /// <summary>
    /// MainView.xaml 的交互逻辑
    /// </summary>
    public partial class MainView : UserControl
    {
public MainView()
        {
            InitializeComponent();
        }

        private void LogListBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (LogListBox.Items is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged -= LogItems_CollectionChanged;
                collection.CollectionChanged += LogItems_CollectionChanged;
            }

            ScrollLogsToEnd();
        }

        private void LogListBox_Unloaded(object sender, RoutedEventArgs e)
        {
            if (LogListBox.Items is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged -= LogItems_CollectionChanged;
            }
        }

        private void LogItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
            {
                ScrollLogsToEnd();
            }
        }

        private void ScrollLogsToEnd()
        {
            if (LogListBox.Items.Count == 0)
            {
                return;
            }

            var lastItem = LogListBox.Items[^1];
            Dispatcher.BeginInvoke(() => LogListBox.ScrollIntoView(lastItem), DispatcherPriority.Background);
        }

        private async void NavigationButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is not Button button)
            {
                return;
            }

            var existingWindows = Application.Current?.Windows.OfType<Window>().ToHashSet() ?? new HashSet<Window>();
            bool shouldResumeContinuousGrab = MainCameraViewer.IsContinuousGrabActive;

            await MainCameraViewer.StopContinuousGrabForNavigationAsync();

            ICommand? command = DataContext is ViewModels.MainViewModel viewModel
                ? viewModel.NavigateCommand
                : null;
            object? parameter = button.CommandParameter;
            if (command?.CanExecute(parameter) == true)
            {
                command.Execute(parameter);
            }

            if (!shouldResumeContinuousGrab)
            {
                return;
            }

            var openedWindow = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(window => !existingWindows.Contains(window));

            if (openedWindow is null)
            {
                await MainCameraViewer.StartContinuousGrabForNavigationAsync();
                return;
            }

            // 子窗口可能包含 CameraViewerControl，关闭时其 Unloaded 会停相机；
            // 设置此标志让主界面的 CameraViewerControl 忽略 Unloaded 时的自动停止。
            MainCameraViewer.SuppressAutoStopOnUnload = true;

            async void HandleWindowClosed(object? windowSender, EventArgs args)
            {
                openedWindow.Closed -= HandleWindowClosed;
                MainCameraViewer.SuppressAutoStopOnUnload = false;
                // 等待 UI 消息队列完成，确保子窗口可视树已完全卸载
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                await MainCameraViewer.StartContinuousGrabForNavigationAsync();
            }

            openedWindow.Closed += HandleWindowClosed;
        }
    }
}
