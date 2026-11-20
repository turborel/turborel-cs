// The repository's own files, for the tests that hold its rules
// (RULES.md D10): found from the test's directory, read from disk alone.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Turborel.Data.Tests.Support;

/// <summary>The repository the tests were built from, and its files.</summary>
internal static class Repository
{
    /// <summary>The file that marks the repository's root.</summary>
    private const string Marker = "Turborel.Data.slnx";

    /// <summary>The directories of C# sources the rules hold for.</summary>
    private static readonly string[] SourceDirectories = ["src", "test"];

    /// <summary>The build's own directories, never sources.</summary>
    private static readonly string[] BuildDirectories = ["bin", "obj"];

    /// <summary>Gets the repository's root directory.</summary>
    internal static string Root { get; } = FindRoot();

    /// <summary>
    /// Gets every C# source of the library and its tests, as paths relative
    /// to the root with <c>/</c> between directories, in ordinal order.
    /// </summary>
    internal static IReadOnlyList<string> CSharpSources { get; } = FindSources();

    /// <summary>Reads a file of the repository.</summary>
    /// <param name="relative">Its path from the root.</param>
    /// <returns>Its text.</returns>
    internal static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    private static string FindRoot()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, Marker)))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException($"no {Marker} above {AppContext.BaseDirectory}");
    }

    private static string[] FindSources()
    {
        return
        [
            .. SourceDirectories
                .SelectMany(directory => Directory.EnumerateFiles(
                    Path.Combine(Root, directory), "*.cs", SearchOption.AllDirectories))
                .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
                .Where(path => !path.Split('/').Any(part => BuildDirectories.Contains(part, StringComparer.Ordinal)))
                .Order(StringComparer.Ordinal),
        ];
    }
}
