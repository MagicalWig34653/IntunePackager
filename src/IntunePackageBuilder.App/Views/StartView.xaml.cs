using System.Windows;
using System.Windows.Controls;
using IntunePackageBuilder.App.ViewModels;

namespace IntunePackageBuilder.App.Views
{
    public partial class StartView : UserControl
    {
        public StartView()
        {
            InitializeComponent();
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        /// <summary>A dropped file or folder goes through the same check as the file picker (SPEC 5.1).</summary>
        private void OnDrop(object sender, DragEventArgs e)
        {
            var model = DataContext as StartViewModel;
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (model != null && paths != null)
            {
                model.HandleDrop(paths);
            }

            e.Handled = true;
        }
    }
}
