// How a test cites what it verifies (RULES.md R-TRACE-1): an xUnit trait,
// so a runner can list and filter tests by it, and TraceRuleTests can
// check every test has one.

using System;
using System.Collections.Generic;
using Xunit.v3;

namespace Turborel.Data.Tests.Support;

/// <summary>
/// Says what a test verifies: a section of the protocol, as
/// <c>[PROTOCOL Frames]</c>; of a client chapter, as
/// <c>[CLIENT Pipelines]</c>; a decision, as <c>[DESIGN] D12</c>; or a rule
/// a repository check holds, as <c>[TOOL] R-SIZE-1</c>.
/// </summary>
/// <param name="citation">The citation, its tag first.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class VerifiesAttribute(string citation) : Attribute, ITraitAttribute
{
    /// <summary>The name of the trait each citation is given as.</summary>
    public const string Trait = "Verifies";

    /// <summary>Gets the citation, its tag first.</summary>
    public string Citation { get; } = citation;

    /// <inheritdoc/>
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() => [new(Trait, Citation)];
}
