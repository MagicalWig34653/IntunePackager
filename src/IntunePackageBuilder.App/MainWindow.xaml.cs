using System.ComponentModel;
using System.Windows;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.App.ViewModels;
using IntunePackageBuilder.Core;

namespace IntunePackageBuilder.App
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _model;

        public MainWindow(MainViewModel model)
        {
            _model = model;
            InitializeComponent();
            DataContext = model;
            ProductText.Text = AppInfo.ProductName + " " + AppInfo.Version;
        }

        /// <summary>Closing while a build runs needs a confirmation (SPEC 7.2).</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_model.CanCloseWithoutAsking
                && !_model.Services.Dialogs.Confirm(Loc.Get("Dialog_CloseTitle"), Loc.Get("Dialog_CloseText")))
            {
                e.Cancel = true;
            }

            base.OnClosing(e);
        }
    }
}
