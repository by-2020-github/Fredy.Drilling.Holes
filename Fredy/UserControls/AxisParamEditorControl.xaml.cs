using Fredy.Drilling.Holes.Models;
using System.Windows;
using System.Windows.Controls;

namespace Fredy.Drilling.Holes.UserControls
{
    public partial class AxisParamEditorControl : UserControl
    {
        public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
            nameof(Header),
            typeof(string),
            typeof(AxisParamEditorControl),
            new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty AxisConfigProperty = DependencyProperty.Register(
            nameof(AxisConfig),
            typeof(AxisParamConfig),
            typeof(AxisParamEditorControl),
            new PropertyMetadata(null));

        public AxisParamEditorControl()
        {
            InitializeComponent();
        }

        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public AxisParamConfig? AxisConfig
        {
            get => (AxisParamConfig?)GetValue(AxisConfigProperty);
            set => SetValue(AxisConfigProperty, value);
        }
    }
}
