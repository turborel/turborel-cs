// The size rules (RULES.md R-SIZE-1 to R-SIZE-3), held over every C#
// source of the library and its tests by reading its syntax tree, since no
// analyzer of the SDK counts a method's lines or parameters (RULES.md D10).

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Rules;

/// <summary>Tests that every source keeps to the size rules.</summary>
public sealed class SizeRuleTests
{
    /// <summary>The most lines of code a function may have (R-SIZE-1).</summary>
    private const int MaxFunctionLines = 50;

    /// <summary>The most lines a source file may have (R-SIZE-2).</summary>
    private const int MaxFileLines = 600;

    /// <summary>The most parameters a function may take (R-SIZE-3).</summary>
    private const int MaxParameters = 5;

    /// <summary>
    /// No method, constructor, accessor, operator or local function has
    /// more than 50 lines of code, blank lines and comments not counted.
    /// </summary>
    [Fact]
    [Verifies("[TOOL] R-SIZE-1")]
    public void FunctionsHaveAtMostFiftyLines()
    {
        var tooLong = Functions()
            .Select(function => (function.Where, Lines: CodeLines(function.Body)))
            .Where(function => function.Lines > MaxFunctionLines)
            .Select(function => $"{function.Where}: {function.Lines} lines")
            .ToList();

        Assert.Empty(tooLong);
    }

    /// <summary>No source file has more than 600 lines.</summary>
    [Fact]
    [Verifies("[TOOL] R-SIZE-2")]
    public void FilesHaveAtMostSixHundredLines()
    {
        var tooLong = Repository.CSharpSources
            .Select(path => (Path: path, Lines: Repository.Read(path).Split('\n').Length))
            .Where(file => file.Lines > MaxFileLines)
            .Select(file => $"{file.Path}: {file.Lines} lines")
            .ToList();

        Assert.Empty(tooLong);
    }

    /// <summary>
    /// No method, constructor, delegate, indexer, local function, lambda or
    /// primary constructor takes more than 5 parameters.
    /// </summary>
    [Fact]
    [Verifies("[TOOL] R-SIZE-3")]
    public void FunctionsTakeAtMostFiveParameters()
    {
        var tooMany = Trees()
            .SelectMany(tree => tree.Root.DescendantNodes().OfType<BaseParameterListSyntax>()
                .Where(list => list.Parameters.Count > MaxParameters)
                .Select(list => $"{Where(tree.Path, list)}: {list.Parameters.Count} parameters"))
            .ToList();

        Assert.Empty(tooMany);
    }

    private static IEnumerable<(string Path, SyntaxNode Root)> Trees()
    {
        return Repository.CSharpSources.Select(path =>
            (path, CSharpSyntaxTree.ParseText(Repository.Read(path), path: path).GetRoot()));
    }

    private static IEnumerable<(string Where, SyntaxNode Body)> Functions()
    {
        foreach ((string path, SyntaxNode root) in Trees())
        {
            foreach (SyntaxNode node in root.DescendantNodes())
            {
                if (BodyOf(node) is { } body)
                {
                    yield return (Where(path, node), body);
                }
            }
        }
    }

    private static SyntaxNode? BodyOf(SyntaxNode node)
    {
        return node switch
        {
            BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
            AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody,
            LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody,
            _ => null,
        };
    }

    private static int CodeLines(SyntaxNode body)
    {
        return body.DescendantTokens()
            .Select(token => token.GetLocation().GetLineSpan().StartLinePosition.Line)
            .Distinct()
            .Count();
    }

    private static string Where(string path, SyntaxNode node)
    {
        int line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        return $"{path}:{line}";
    }
}
