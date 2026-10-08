using System.Windows;
using IntunePackageBuilder.Core;

namespace IntunePackageBuilder.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            VersionText.Text = AppInfo.ProductName + " " + AppInfo.Version;
        }
    }
}
