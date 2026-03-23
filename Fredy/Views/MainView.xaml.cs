using System;
using System.Collections.Specialized;
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
    }
}
