using System;
using System.Globalization;
using System.Text;

namespace AudioDataPlugIn
{
    internal static class TitleCaseTransformer
    {
        private static readonly string[,] CommonReplacements =
        {
            { " By A ", " by a " },
            { " For A ", " for a " },
            { " In A ", " in a " },
            { " Of A ", " of a " },
            { " On A ", " on a " },
            { " A ", " a " },
            { " To An ", " to an " },
            { " An ", " an " },
            { " And The ", " and the " },
            { " And ", " and " },
            { " As ", " as " },
            { " At The ", " at the " },
            { " At ", " at " },
            { " By The ", " by the " },
            { " By ", " by " },
            { " But ", " but " },
            { " For The ", " for the " },
            { " For ", " for " },
            { " From ", " from " },
            { " In The ", " in the " },
            { " In ", " in " },
            { " Into ", " into " },
            { " Nor ", " nor " },
            { " Out Of The ", " out of the " },
            { " Out Of ", " out of " },
            { " Of The ", " of the " },
            { " Of ", " of " },
            { " On The ", " on the " },
            { " On ", " on " },
            { " Or ", " or " },
            { " To The ", " to the " },
            { " To ", " to " },
            { " With The ", " with the " },
            { " With ", " with " },
            { " The ", " the " }
        };

        private static readonly string[,] FinalReplacements =
        {
            { " - the ", " - The " },
            { " : the ", " : The " },
            { " & the ", " & The " },
            { " [Feat. ", " [ft. " },
            { " [Ft. ", " [ft. " },
            { " (Feat. ", " (ft. " },
            { " (Ft. ", " (ft. " },
            { " Vs ", " vs. " },
            { " Vs, ", " vs. " },
            { " E.p.", " E.P." },
            { " Cd", " CD" }
        };

        internal static string TransformTrackTitle(string value)
        {
            return Transform(value, false);
        }

        internal static string TransformAlbumTitle(string value)
        {
            return Transform(value, true);
        }

        private static string Transform(string value, bool album)
        {
            if (String.IsNullOrEmpty(value))
                return value ?? String.Empty;

            string transformed = value.ToLowerInvariant();
            transformed = PicardTitleCase(transformed);
            transformed = PicardTitleCase(transformed).Replace("  ", " ");
            transformed = ApplyReplacements(transformed, CommonReplacements);
            transformed = transformed.Replace(
                " Best of ",
                album ? " Best of " : " Best Of ");
            transformed = ApplyReplacements(transformed, FinalReplacements);

            if (album)
            {
                transformed = transformed.Replace(" Ii ", " II ");
                transformed = transformed.Replace(" Iii ", " III ");
                transformed = transformed.Replace(" Iv ", " IV ");
            }

            return transformed;
        }

        private static string ApplyReplacements(string value, string[,] replacements)
        {
            for (int index = 0; index < replacements.GetLength(0); index++)
                value = value.Replace(replacements[index, 0], replacements[index, 1]);
            return value;
        }

        // Mirrors MusicBrainz Picard's $title() word-boundary behavior. In
        // particular, apostrophes within a word do not begin a new word.
        private static string PicardTitleCase(string value)
        {
            if (String.IsNullOrEmpty(value))
                return value;

            StringBuilder result = new StringBuilder(value.Length);
            result.Append(Char.ToUpperInvariant(value[0]));
            bool capitalizeNextLetter = false;

            for (int index = 1; index < value.Length; index++)
            {
                char current = value[index];
                if ((current == '\'' || current == '\u2019') &&
                    Char.IsLetter(value[index - 1]))
                {
                    capitalizeNextLetter = false;
                }
                else if (IsPicardWordBoundary(current))
                {
                    capitalizeNextLetter = true;
                }
                else if (capitalizeNextLetter && Char.IsLetter(current))
                {
                    capitalizeNextLetter = false;
                    current = Char.ToUpperInvariant(current);
                }
                else
                {
                    capitalizeNextLetter = false;
                }

                result.Append(current);
            }

            return result.ToString();
        }

        private static bool IsPicardWordBoundary(char value)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(value);
            return category == UnicodeCategory.SpaceSeparator ||
                category == UnicodeCategory.ModifierSymbol ||
                category == UnicodeCategory.ConnectorPunctuation ||
                category == UnicodeCategory.DashPunctuation ||
                category == UnicodeCategory.OpenPunctuation ||
                category == UnicodeCategory.ClosePunctuation ||
                category == UnicodeCategory.InitialQuotePunctuation ||
                category == UnicodeCategory.FinalQuotePunctuation ||
                category == UnicodeCategory.OtherPunctuation;
        }
    }
}
