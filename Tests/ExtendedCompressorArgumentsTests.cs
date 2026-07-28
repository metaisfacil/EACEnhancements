using System;
using System.IO;
using System.Reflection;
using System.Text;
using AudioDataPlugIn;

internal static class ExtendedCompressorArgumentsTests
{
    private static int Main()
    {
        Assert(
            !EnhancementRuntime.RequiresExtendedCompressorArguments(
                new string('a', 500)),
            "The native 500-character boundary should remain native.");
        Assert(
            EnhancementRuntime.RequiresExtendedCompressorArguments(
                new string('a', 501)),
            "A 501-character template should use extended storage.");
        Assert(
            EnhancementRuntime.ExtendedExternalEncoderOptionsLimit == 1000,
            "The extended limit should stay within EAC's tested downstream command capacity.");

        string marker = EnhancementRuntime.ExtendedExternalEncoderOptionsMarker;
        string expanded = EnhancementRuntime.ExpandExtendedCompressorArguments(
            "before " + marker.ToUpperInvariant() + " after",
            "-T \"LONG=%title%\" %source% -o %dest%");
        Assert(
            expanded ==
                "before -T \"LONG=%title%\" %source% -o %dest% after",
            "Extended argument expansion should be case-insensitive and preserve EAC tokens.");

        Assert(
            EnhancementRuntime.MatchExtendedCompressorArgumentsToken(
                "x" + marker + "y",
                1) == marker.Length,
            "The replacement-tag lexer should accept the extension marker.");
        Assert(
            EnhancementRuntime.MatchExtendedCompressorArgumentsToken(
                marker,
                marker.Length) == 0,
            "Token matching should reject an index beyond the marker.");
        Assert(
            EnhancementRuntime.ExpandExtendedCompressorArguments(
                marker,
                null) == String.Empty,
            "A missing extended value should remove the private marker.");

        const string nativeFallback = "-8 %source% -o %dest%";
        const string savedExtended =
            "-8 -T \"LONG=%title%\" %source% -o %dest%";
        Assert(
            EnhancementRuntime.ResolveExtendedCompressorArguments(
                nativeFallback,
                true,
                savedExtended,
                String.Empty) == savedExtended,
            "The saved EAC argument buffer should resolve to the INI-only extended value.");
        Assert(
            EnhancementRuntime.ResolveExtendedCompressorArguments(
                nativeFallback,
                false,
                savedExtended,
                String.Empty) == nativeFallback,
            "An unrelated template must not be replaced merely because an extended value exists.");
        Assert(
            EnhancementRuntime.ResolveExtendedCompressorArguments(
                marker,
                false,
                savedExtended,
                "temporary arguments") == "temporary arguments",
            "Temporary validation and Test Encoder expansion should take precedence over the saved value.");
        Assert(
            EnhancementRuntime.ResolveExtendedCompressorArguments(
                nativeFallback,
                true,
                savedExtended,
                String.Empty,
                false) == nativeFallback,
            "Disabling the feature should retain but not use the saved extended value.");
        Assert(
            EnhancementRuntime.ResolveExtendedCompressorArguments(
                marker,
                false,
                savedExtended,
                String.Empty,
                false) == marker,
            "Disabling the feature should not expand a private marker.");
        Assert(
            EnhancementRuntime.MatchExtendedCompressorArgumentsToken(
                marker,
                0,
                false) == 0,
            "Disabling the feature should remove its marker from token validation.");

        AssertDisabledPreferenceRetainsSavedArguments();

        Console.WriteLine("Extended compressor argument tests passed.");
        return 0;
    }

    private static void AssertDisabledPreferenceRetainsSavedArguments()
    {
        string iniPath = EnhancementRuntime.GetSettingsFilePath();
        bool existed = File.Exists(iniPath);
        byte[] original = existed ? File.ReadAllBytes(iniPath) : null;
        string retained = new string('x', 700);
        try
        {
            if (!NativeMethods.WritePrivateProfileStringW(
                    "ExternalCompression",
                    "ExtendedEncoderOptions",
                    retained,
                    iniPath))
            {
                throw new InvalidOperationException(
                    "The retention test could not write its extended value.");
            }

            EnhancementRuntime.UpdateExtendedCompressorArgumentsPreference(
                false);
            Assert(
                !EnhancementRuntime.IsExtendedCompressorArgumentsEnabled(),
                "The external-compressor argument feature did not disable.");

            MethodInfo persist = typeof(EnhancementRuntime).GetMethod(
                "PersistExtendedCompressorArguments",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert(
                persist != null,
                "The extended argument persistence path was not found.");
            persist.Invoke(null, new object[] { String.Empty });

            StringBuilder value = new StringBuilder(1001);
            NativeMethods.GetPrivateProfileStringW(
                "ExternalCompression",
                "ExtendedEncoderOptions",
                String.Empty,
                value,
                value.Capacity,
                iniPath);
            Assert(
                value.ToString() == retained,
                "Disabling the feature should retain the saved extended arguments.");
        }
        finally
        {
            EnhancementRuntime.UpdateExtendedCompressorArgumentsPreference(
                true);
            if (File.Exists(iniPath))
                File.Delete(iniPath);
            if (existed)
                File.WriteAllBytes(iniPath, original);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
