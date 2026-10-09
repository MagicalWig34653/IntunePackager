using System;
using IntunePackageBuilder.App.Infrastructure;

namespace IntunePackageBuilder.App.ViewModels
{
    /// <summary>One entry of the form: its text, its problem (if any) and whether it is shown.</summary>
    public sealed class FormField : ViewModelBase
    {
        private string _value = string.Empty;
        private string _error;
        private bool _isVisible = true;
        private bool _isReadOnly;

        public FormField(string key)
        {
            Key = key;
        }

        /// <summary>Name of the field; the view uses it to move the focus to the field with a problem.</summary>
        public string Key { get; private set; }

        /// <summary>The label above the entry, from the resources.</summary>
        public string Label { get; set; }

        /// <summary>A short note under the entry; null or empty when there is none.</summary>
        public string Hint { get; set; }

        public bool HasHint
        {
            get { return !string.IsNullOrEmpty(Hint); }
        }

        /// <summary>Several lines (lists and texts); the other entries are one line.</summary>
        public bool IsMultiline { get; set; }

        public string Value
        {
            get { return _value; }
            set
            {
                if (Set(ref _value, value ?? string.Empty))
                {
                    Error = null;
                    var handler = Edited;
                    if (handler != null)
                    {
                        handler(this, EventArgs.Empty);
                    }
                }
            }
        }

        public string Error
        {
            get { return _error; }
            set
            {
                if (Set(ref _error, value))
                {
                    Raise("HasError");
                }
            }
        }

        public bool HasError
        {
            get { return !string.IsNullOrEmpty(_error); }
        }

        public bool IsVisible
        {
            get { return _isVisible; }
            set { Set(ref _isVisible, value); }
        }

        public bool IsReadOnly
        {
            get { return _isReadOnly; }
            set { Set(ref _isReadOnly, value); }
        }

        /// <summary>Raised when the user (or the view) changes the text; not raised by <see cref="Assign"/>.</summary>
        public event EventHandler Edited;

        /// <summary>Sets the text from the program, for example a suggestion. It is not an edit by the user.</summary>
        public void Assign(string value)
        {
            Set(ref _value, value ?? string.Empty);
            Raise("Value");
        }
    }
}
