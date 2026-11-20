// The traceability rules (RULES.md D6, R-TRACE-1, R-TRACE-2): every test
// says what it verifies, and every citation, of a test or of a source,
// names a heading, decision or rule that exists.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Rules;

/// <summary>Tests that every test cites what it verifies, and that every citation is right.</summary>
public sealed partial class TraceRuleTests
{
    /// <summary>The file whose decisions and rules a citation may name.</summary>
    private const string RulesFile = "RULES.md";

    /// <summary>Every test, a method marked as a fact or a theory, carries a citation.</summary>
    [Fact]
    [Verifies("[TOOL] R-TRACE-1")]
    public void EveryTestCitesWhatItVerifies()
    {
        var uncited = Tests()
            .Where(test => !test.GetCustomAttributes<VerifiesAttribute>().Any())
            .Select(test => $"{test.DeclaringType?.Name}.{test.Name}")
            .ToList();

        Assert.NotEmpty(Tests());
        Assert.Empty(uncited);
    }

    /// <summary>
    /// Every test's citation begins with a tag of D6, and names a heading of
    /// the document its tag names, or a decision or rule RULES.md defines.
    /// </summary>
    [Fact]
    [Verifies("[TOOL] R-TRACE-2")]
    public void EveryTestCitationNamesWhatExists()
    {
        string rules = Repository.Read(RulesFile);
        var wrong = Tests()
            .SelectMany(test => test.GetCustomAttributes<VerifiesAttribute>())
            .Select(verifies => verifies.Citation)
            .Where(citation => !Exists(citation, rules))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(wrong);
    }

    /// <summary>
    /// Every heading a source cites, every decision it names as
    /// <c>RULES.md D&lt;n&gt;</c> or <c>[DESIGN] D&lt;n&gt;</c>, and every rule
    /// it names exists.
    /// </summary>
    [Fact]
    [Verifies("[TOOL] R-TRACE-2")]
    public void EverySourceCitationNamesWhatExists()
    {
        string rules = Repository.Read(RulesFile);
        var wrong = Repository.CSharpSources
            .SelectMany(path => SourceCitations(Repository.Read(path)).Select(cited => (Path: path, Cited: cited)))
            .Where(found => !Exists(found.Cited, rules))
            .Select(found => $"{found.Path}: {found.Cited}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(wrong);
    }

    private static List<MethodInfo> Tests()
    {
        return
        [
            .. typeof(TraceRuleTests).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                .Where(method => method.GetCustomAttributes<FactAttribute>().Any()),
        ];
    }

    // Each citation a source makes, written as a test's would be: a
    // heading's, or a decision's or rule's after the tag DESIGN.
    private static IEnumerable<string> SourceCitations(string text)
    {
        foreach (Match heading in CitedHeading().Matches(text))
        {
            yield return heading.Value;
        }

        foreach (Match id in CitedId().Matches(text))
        {
            yield return "[DESIGN] " + id.Groups["id"].Value;
        }
    }

    private static bool Exists(string citation, string rules)
    {
        Match match = Citation().Match(citation);
        if (!match.Success)
        {
            return false;
        }

        string named = match.Groups["named"].Value;
        return match.Groups["tag"].Value switch
        {
            "PROTOCOL" => Citations.Protocol.Contains(named),
            "CLIENT" => Citations.Client.Contains(named),
            _ => Defined(named, rules),
        };
    }

    // A decision is defined by its heading, "#### D12. ..."; a rule by its
    // row of a table, "| R-SIZE-1 | ...".
    private static bool Defined(string id, string rules)
    {
        return rules.Contains($"#### {id}. ", StringComparison.Ordinal)
            || rules.Contains($"| {id} |", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^\[(?<tag>PROTOCOL|CLIENT) (?<named>[^\]]+)\]|^\[(?<tag>DESIGN|TOOL)\] (?<named>D\d+|R-[A-Z]+-\d+)\b")]
    private static partial Regex Citation();

    [GeneratedRegex(@"\[(PROTOCOL|CLIENT) [^\]\r\n]+\]")]
    private static partial Regex CitedHeading();

    // Turborel's own RULES.md, cited as "Turborel's RULES.md D89", is not
    // this file, and its decisions are not checked here.
    [GeneratedRegex(@"(?:(?<![Tt]urborel's )RULES\.md |\[DESIGN\] )(?<id>D\d+)\b|\b(?<id>R-[A-Z]+-\d+)\b")]
    private static partial Regex CitedId();
}
