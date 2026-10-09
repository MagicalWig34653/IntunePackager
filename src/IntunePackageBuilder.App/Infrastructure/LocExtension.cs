using System;
using System.Windows.Markup;

namespace IntunePackageBuilder.App.Infrastructure
{
    /// <summary>XAML access to the resource texts: <c>{infra:Loc Start_Heading}</c>. XAML never contains user-visible text itself.</summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class LocExtension : MarkupExtension
    {
        public LocExtension()
        {
        }

        public LocExtension(string key)
        {
            Key = key;
        }

        [ConstructorArgument("key")]
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return Loc.Get(Key);
        }
    }
}
