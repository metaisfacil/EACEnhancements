using System;
using System.Collections.Generic;

namespace AudioDataPlugIn
{
    // EAC's external-compressor page rejects unknown %tag% values with
    // "Invalid replacement tag found !" (translated string 6063). The custom
    // CD Label, CD Barcode, and CD Catalog # tags require hooks in both the
    // metadata replacement-tag lexer and metadata template formatter.
    internal static class MetadataTokenHookLayoutTests
    {
        private static int Main()
        {
            AssertKnownAddresses();
            AssertAddressesAreVersionSpecific();
            AssertEveryLayoutIsPopulated();

            Console.WriteLine("Metadata token hook layout tests passed.");
            return 0;
        }

        private static void AssertKnownAddresses()
        {
            AssertLayout("EAC 1.8", 0x0050C1E0u, 0x0050CF80u);
            AssertLayout("EAC 1.6", 0x00508260u, 0x00509000u);
        }

        private static void AssertLayout(
            string name,
            uint expectedLexerVa,
            uint expectedFormatterVa)
        {
            EacVersionLayout layout = FindLayout(name);
            if (layout.MetadataTokenLexerVa != expectedLexerVa)
            {
                throw new Exception(
                    name + " metadata replacement-tag lexer address is 0x" +
                    layout.MetadataTokenLexerVa.ToString("X8") +
                    "; expected 0x" + expectedLexerVa.ToString("X8") + ".");
            }
            if (layout.MetadataTemplateFormatterVa != expectedFormatterVa)
            {
                throw new Exception(
                    name + " metadata template formatter address is 0x" +
                    layout.MetadataTemplateFormatterVa.ToString("X8") +
                    "; expected 0x" + expectedFormatterVa.ToString("X8") + ".");
            }
        }

        private static void AssertAddressesAreVersionSpecific()
        {
            Dictionary<uint, string> lexers = new Dictionary<uint, string>();
            Dictionary<uint, string> formatters = new Dictionary<uint, string>();
            foreach (EacVersionLayout layout in EacVersionLayout.KnownLayouts)
            {
                AssertUnique(
                    lexers,
                    layout.MetadataTokenLexerVa,
                    layout.Name,
                    "metadata replacement-tag lexer");
                AssertUnique(
                    formatters,
                    layout.MetadataTemplateFormatterVa,
                    layout.Name,
                    "metadata template formatter");
            }
        }

        private static void AssertUnique(
            Dictionary<uint, string> seen,
            uint address,
            string layoutName,
            string description)
        {
            string owner;
            if (seen.TryGetValue(address, out owner))
            {
                throw new Exception(
                    layoutName + " and " + owner + " share one " + description +
                    " address (0x" + address.ToString("X8") +
                    "). One of them cannot be hooked, which silently disables " +
                    "the custom compressor tags on that executable.");
            }

            seen.Add(address, layoutName);
        }

        private static void AssertEveryLayoutIsPopulated()
        {
            foreach (EacVersionLayout layout in EacVersionLayout.KnownLayouts)
            {
                if (layout.MetadataTokenLexerVa == 0)
                    throw new Exception(layout.Name + " has no metadata replacement-tag lexer address.");
                if (layout.MetadataTemplateFormatterVa == 0)
                    throw new Exception(layout.Name + " has no metadata template formatter address.");

                // Both routines live in .text, which starts at 0x00401000 and
                // is the only region the jump patches may target.
                AssertInCodeSection(layout, layout.MetadataTokenLexerVa, "metadata replacement-tag lexer");
                AssertInCodeSection(layout, layout.MetadataTemplateFormatterVa, "metadata template formatter");
            }
        }

        private static void AssertInCodeSection(
            EacVersionLayout layout,
            uint address,
            string description)
        {
            const uint codeStart = 0x00401000u;
            uint imageEnd = 0x00400000u + (uint)layout.ImageSize;
            if (address < codeStart || address >= imageEnd)
            {
                throw new Exception(
                    layout.Name + " " + description + " address 0x" +
                    address.ToString("X8") + " is outside the mapped image.");
            }
        }

        private static EacVersionLayout FindLayout(string name)
        {
            foreach (EacVersionLayout layout in EacVersionLayout.KnownLayouts)
            {
                if (String.Equals(layout.Name, name, StringComparison.Ordinal))
                    return layout;
            }

            throw new Exception("No layout is defined for " + name + ".");
        }
    }
}
