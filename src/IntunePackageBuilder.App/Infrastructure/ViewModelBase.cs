using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IntunePackageBuilder.App.Infrastructure
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            Raise(name);
            return true;
        }

        protected void Raise([CallerMemberName] string name = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
