using Fredy.Drilling.Holes.ViewModels;
using System.Windows;

namespace Fredy.Drilling.Holes.Windows.PunchAudit
{
    /// <summary>
    /// PunchAuditWindow.xaml 的交互逻辑
    /// </summary>
    public partial class PunchAuditWindow : Window
    {
        public PunchAuditWindow(PunchProcessAuditViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
