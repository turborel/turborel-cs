// The manual's layout, which turborel.com's documentation site reads
// (RULES.md R-DOC-7): each page's title and Contents line, each volume's
// list of its pages, and links that resolve, within doc/ relative and out
// of it full URLs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Rules;

/// <summary>Tests that the manual and every Markdown file keep to R-DOC-7.</summary>
public sealed partial class ManualRuleTests
{
    /// <summary>The manual's folder.</summary>
    private const string Manual = "doc";

    /// <summary>A volume's, or the set's, list of its pages.</summary>
    private const string Index = "README.md";

    /// <summary>The line a page holds after its title, which the site replaces by its sidebar.</summary>
    private const string ContentsLine = "[Contents](README.md)";

    /// <summary>Every page but a README opens with its title, then a line holding only its Contents link.</summary>
    [Fact]
    [Verifies("[TOOL] R-DOC-7")]
    public void EveryPageOpensWithItsTitleAndContents()
    {
        var wrong = Pages()
            .Where(page => Path.GetFileName(page) != Index)
            .Where(page => !OpensRight(Repository.Read(page)))
            .ToList();

        Assert.NotEmpty(Pages());
        Assert.Empty(wrong);
    }

    /// <summary>Each volume's README lists every page of its folder.</summary>
    [Fact]
    [Verifies("[TOOL] R-DOC-7")]
    public void EveryVolumeListsItsPages()
    {
        var unlisted = Pages()
            .Where(page => Path.GetFileName(page) != Index)
            .Where(page => !Repository.Read(Path.Combine(Path.GetDirectoryName(page)!, Index))
                .Contains($"]({Path.GetFileName(page)})", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(unlisted);
    }

    /// <summary>
    /// Every relative link of every Markdown file resolves; one of the
    /// manual's stays within <c>doc/</c>, a link out of it being a full URL.
    /// </summary>
    [Fact]
    [Verifies("[TOOL] R-DOC-7")]
    public void EveryLinkResolves()
    {
        string manual = Path.GetFullPath(Path.Combine(Repository.Root, Manual)) + Path.DirectorySeparatorChar;
        var broken = Repository.Files("*.md")
            .SelectMany(file => RelativeLinks(Repository.Read(file)).Select(link => (File: file, Link: link)))
            .Where(found => !Resolves(found.File, found.Link, manual))
            .Select(found => $"{found.File}: {found.Link}")
            .ToList();

        Assert.Empty(broken);
    }

    private static List<string> Pages() =>
        [.. Repository.Files("*.md").Where(file => file.StartsWith(Manual + "/", StringComparison.Ordinal))];

    private static bool OpensRight(string text)
    {
        string[] lines = text.Split('\n');
        return lines.Length > 2 && lines[0].StartsWith("# ", StringComparison.Ordinal)
            && lines[1].Length == 0 && lines[2] == ContentsLine;
    }

    private static IEnumerable<string> RelativeLinks(string text)
    {
        string prose = Fenced().Replace(text, string.Empty);
        return Link().Matches(prose)
            .Select(match => match.Groups["target"].Value)
            .Where(target => !target.StartsWith('#') && !Scheme().IsMatch(target));
    }

    private static bool Resolves(string file, string link, string manual)
    {
        string target = link.Split('#')[0];
        string from = Path.GetDirectoryName(Path.Combine(Repository.Root, file))!;
        string resolved = Path.GetFullPath(Path.Combine(from, target));
        bool exists = File.Exists(resolved) || Directory.Exists(resolved);
        bool inManual = file.StartsWith(Manual + "/", StringComparison.Ordinal);
        return exists && (!inManual || resolved.StartsWith(manual, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"```.*?```", RegexOptions.Singleline)]
    private static partial Regex Fenced();

    [GeneratedRegex(@"\]\((?<target>[^)\s]+)\)|(?:src|srcset|href)=""(?<target>[^""]+)""")]
    private static partial Regex Link();

    [GeneratedRegex(@"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase)]
    private static partial Regex Scheme();
}
