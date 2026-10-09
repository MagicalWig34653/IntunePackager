namespace IntunePackageBuilder.App.Services
{
    /// <summary>Dialogs the view models need. The real implementation shows Windows dialogs; tests use a fake.</summary>
    public interface IUserDialogs
    {
        /// <summary>Lets the user pick a file; returns its full path or null when cancelled.</summary>
        string PickFile(string title, string filter, string initialFolder);

        /// <summary>Lets the user pick a folder; returns its full path or null when cancelled.</summary>
        string PickFolder(string title, string initialFolder);

        bool Confirm(string title, string message);
    }

    /// <summary>Actions on the surrounding system. The real implementation opens Explorer and the browser; tests use a fake.</summary>
    public interface IShell
    {
        void OpenFolder(string path);

        void OpenFile(string path);

        void OpenUrl(string url);

        void CopyText(string text);
    }
}
