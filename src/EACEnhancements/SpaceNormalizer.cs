using System;

namespace AudioDataPlugIn
{
    internal static class SpaceNormalizer
    {
        internal static string Transform(string value)
        {
            if (String.IsNullOrEmpty(value))
                return value ?? String.Empty;

            char[] result = null;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                // Only Unicode space separators: preserve line breaks, tabs,
                // zero-width formatting, and letters. An explicit list avoids
                // older .NET Unicode tables treating U+180E as a space.
                if (current == '\u00A0' || current == '\u1680' ||
                    (current >= '\u2000' && current <= '\u200A') ||
                    current == '\u202F' || current == '\u205F' ||
                    current == '\u3000')
                {
                    if (result == null)
                        result = value.ToCharArray();
                    result[index] = ' ';
                }
            }
            return result == null ? value : new string(result);
        }
    }
}
