// The traceability rules (RULES.md R-TRACE-1, R-TRACE-2): every test says
// what it verifies, by a citation whose section, decision or rule exists.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Rules;

/// <summary>Tests that every test cites what it verifies, and cites it rightly.</summary>
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
    /// Every citation begins with a tag, and names a heading of the manual,
    /// or a decision or rule RULES.md defines.
    /// </summary>
    [Fact]
    [Verifies("[TOOL] R-TRACE-2")]
    public void EveryCitationNamesWhatExists()
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

    private static List<MethodInfo> Tests()
    {
        return
        [
            .. typeof(TraceRuleTests).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                .Where(method => method.GetCustomAttributes<FactAttribute>().Any()),
        ];
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
            "DESIGN" => Defined(named, rules),
            "TOOL" => Defined(named, rules),
            _ => false,
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
}
