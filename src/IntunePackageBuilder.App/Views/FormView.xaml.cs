using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using IntunePackageBuilder.App.ViewModels;

namespace IntunePackageBuilder.App.Views
{
    public partial class FormView : UserControl
    {
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(1) };
        private FormViewModel _model;

        public FormView()
        {
            InitializeComponent();
            _timer.Tick += (sender, args) =>
            {
                if (_model != null)
                {
                    _model.UpdateElapsed();
                }
            };
            Loaded += (sender, args) => _timer.Start();
            Unloaded += (sender, args) => _timer.Stop();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_model != null)
            {
                _model.FocusRequested -= OnFocusRequested;
            }

            _model = e.NewValue as FormViewModel;
            if (_model != null)
            {
                _model.FocusRequested += OnFocusRequested;
            }
        }

        /// <summary>Moves the focus to the entry with a problem (SPEC 5.2).</summary>
        private void OnFocusRequested(object sender, string key)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new System.Action(() =>
                {
                    var box = Find(this, key);
                    if (box != null)
                    {
                        box.BringIntoView();
                        box.Focus();
                    }
                }));
        }

        private static TextBox Find(DependencyObject parent, string key)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var box = child as TextBox;
                if (box != null && (box.Tag as string) == key)
                {
                    return box;
                }

                var inner = Find(child, key);
                if (inner != null)
                {
                    return inner;
                }
            }

            return null;
        }
    }
}
