using Fredy.Drilling.Holes.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace Fredy.Drilling.Holes.Views
{
    /// <summary>
    /// Window1.xaml 的交互逻辑
    /// </summary>
    public partial class ConfigWindow : Window
    {
        public ConfigWindow()
        {
            InitializeComponent();
            DataContext = App.ServiceProvider.GetRequiredService<ConfigViewModel>();
        }
    }
}
