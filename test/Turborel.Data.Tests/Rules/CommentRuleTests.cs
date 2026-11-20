// The comment rules a tool can hold (RULES.md R-DOC-3, R-DOC-5): no
// marker of work left undone, no citation of the list of open work, and a
// comment heading every file.

using System;
using System.Linq;
using System.Text.RegularExpressions;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Rules;

/// <summary>Tests that sources keep to the comment rules.</summary>
public sealed partial class CommentRuleTests
{
    /// <summary>
    /// No source holds a marker of work left undone, nor names the file of
    /// open work: open work is listed there, and code never points at it.
    /// </summary>
    [Fact]
    [Verifies("[TOOL] R-DOC-3")]
    public void NoSourceMarksOpenWork()
    {
        var marked = Repository.CSharpSources
            .Where(path => OpenWork().IsMatch(Repository.Read(path)))
            .ToList();

        Assert.Empty(marked);
    }

    /// <summary>Every source begins with a comment of what it holds and why.</summary>
    [Fact]
    [Verifies("[TOOL] R-DOC-5")]
    public void EverySourceBeginsWithAComment()
    {
        var bare = Repository.CSharpSources
            .Where(path => !Repository.Read(path).StartsWith("// ", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(bare);
    }

    // The markers are written as classes of one letter each, so this file
    // does not match its own pattern.
    [GeneratedRegex(@"\b([T]ODO|[F]IXME|[X]XX|[H]ACK)\b")]
    private static partial Regex OpenWork();
}
