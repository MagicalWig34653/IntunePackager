using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace IntunePackageBuilder.App.Services
{
    /// <summary>The Windows dialogs behind <see cref="IUserDialogs"/>.</summary>
    public sealed class DialogsImpl : IUserDialogs
    {
        public string PickFile(string title, string filter, string initialFolder)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                CheckFileExists = true,
                Multiselect = false
            };
            if (!string.IsNullOrEmpty(initialFolder))
            {
                dialog.InitialDirectory = initialFolder;
            }

            return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
        }

        public string PickFolder(string title, string initialFolder)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = title;
                dialog.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(initialFolder) && System.IO.Directory.Exists(initialFolder))
                {
                    dialog.SelectedPath = initialFolder;
                }

                return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        public bool Confirm(string title, string message)
        {
            return MessageBox.Show(Owner(), message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private static Window Owner()
        {
            return Application.Current == null ? null : Application.Current.MainWindow;
        }
    }
}
