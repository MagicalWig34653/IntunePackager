using System;

namespace IntunePackageBuilder.Core.Versions
{
    /// <summary>
    /// Numeric dotted version (1 to 4 parts). Comparison is numeric and ignores trailing zero parts,
    /// so "1.0" equals "1.0.0" and "1.10" is greater than "1.9".
    /// </summary>
    public sealed class VersionNumber : IComparable<VersionNumber>, IEquatable<VersionNumber>
    {
        private const int MaxParts = 4;
        private const int MaxDigits = 9;

        private readonly int[] _parts;

        private VersionNumber(string text, int[] parts)
        {
            Text = text;
            _parts = parts;
        }

        /// <summary>The trimmed text the version was parsed from.</summary>
        public string Text { get; private set; }

        public static bool TryParse(string text, out VersionNumber result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            var pieces = trimmed.Split('.');
            if (pieces.Length > MaxParts)
            {
                return false;
            }

            var parts = new int[pieces.Length];
            for (var i = 0; i < pieces.Length; i++)
            {
                var piece = pieces[i];
                if (piece.Length == 0 || piece.Length > MaxDigits)
                {
                    return false;
                }

                var value = 0;
                foreach (var c in piece)
                {
                    if (c < '0' || c > '9')
                    {
                        return false;
                    }

                    value = value * 10 + (c - '0');
                }

                parts[i] = value;
            }

            result = new VersionNumber(trimmed, parts);
            return true;
        }

        public int CompareTo(VersionNumber other)
        {
            if (other == null)
            {
                return 1;
            }

            for (var i = 0; i < MaxParts; i++)
            {
                var left = Part(i);
                var right = other.Part(i);
                if (left != right)
                {
                    return left < right ? -1 : 1;
                }
            }

            return 0;
        }

        public bool Equals(VersionNumber other)
        {
            return other != null && CompareTo(other) == 0;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as VersionNumber);
        }

        public override int GetHashCode()
        {
            var hash = 17;
            for (var i = 0; i < MaxParts; i++)
            {
                hash = hash * 31 + Part(i);
            }

            return hash;
        }

        public override string ToString()
        {
            return Text;
        }

        private int Part(int index)
        {
            return index < _parts.Length ? _parts[index] : 0;
        }
    }
}
