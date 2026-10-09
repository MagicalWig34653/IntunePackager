using System.Text;

namespace IntunePackageBuilder.Generation.Guide
{
    /// <summary>HTML escaping for text and attribute values. Everything that comes from metadata or configuration goes through here.</summary>
    public static class HtmlText
    {
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 16);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '&':
                        builder.Append("&amp;");
                        break;
                    case '<':
                        builder.Append("&lt;");
                        break;
                    case '>':
                        builder.Append("&gt;");
                        break;
                    case '"':
                        builder.Append("&quot;");
                        break;
                    case '\'':
                        builder.Append("&#39;");
                        break;
                    default:
                        if (c < ' ' && c != '\n' && c != '\r' && c != '\t')
                        {
                            builder.Append('�');
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            return builder.ToString();
        }
    }
}
