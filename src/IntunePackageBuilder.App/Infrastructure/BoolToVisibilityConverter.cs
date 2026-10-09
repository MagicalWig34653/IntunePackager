using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IntunePackageBuilder.App.Infrastructure
{
    /// <summary>True is visible; with <see cref="Invert"/> false is visible. Hidden entries take no space.</summary>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var flag = value is bool && (bool)value;
            return flag != Invert ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
