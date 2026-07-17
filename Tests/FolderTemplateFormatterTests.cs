using System;
using AudioDataPlugIn;

internal static class FolderTemplateFormatterTests
{
    private static void Main()
    {
        const string template = "%albumartist% - %albumtitle% (((%year%))) [FLAC]%comment%";
        AssertEqual(
            FolderTemplateFormatter.ResolveConditionalParentheses(template, true),
            "%albumartist% - %albumtitle% (%year%) [FLAC]%comment%");
        AssertEqual(
            FolderTemplateFormatter.ResolveConditionalParentheses(template, false),
            "%albumartist% - %albumtitle% [FLAC]%comment%");
        AssertEqual(
            FolderTemplateFormatter.ResolveConditionalParentheses("(((%year%))) Album", false),
            "Album");
        AssertEqual(
            FolderTemplateFormatter.ResolveConditionalParentheses("Album (((%year%)))", false),
            "Album");
        AssertEqual(
            FolderTemplateFormatter.ResolveConditionalCurlyBraces("[FLAC] {{{%comment%}}}", true),
            "[FLAC] {%comment%}");
        AssertEqual(
            FolderTemplateFormatter.ResolveConditionalCurlyBraces("[FLAC] {{{%comment%}}}", false),
            "[FLAC]");
        AssertEqual(
            FolderTemplateFormatter.ResolveConditionalCurlyBraces("[FLAC]{{{%comment%}}}catalog", true),
            "[FLAC] {%comment%} catalog");
        Console.WriteLine("FolderTemplateFormatter tests passed.");
    }

    private static void AssertEqual(string actual, string expected)
    {
        if (!String.Equals(actual, expected, StringComparison.Ordinal))
            throw new Exception("Expected '" + expected + "' but got '" + actual + "'.");
    }
}
