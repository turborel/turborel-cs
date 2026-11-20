// The sample frames of The Native Protocol, Fixtures/05-protocol-frames.tsv,
// copied from Turborel's doc/client/05-protocol-frames.tsv at 045e42a9
// (RULES.md D21), read as its page lays it out [PROTOCOL Sample frames]:
// one frame a line, five fields apart by tabs, a line of # a comment.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Turborel.Data.Tests.Support;

/// <summary>A sample frame: who sends it, its name, the capabilities it needs, the whole frame, and what it means.</summary>
/// <param name="Direction">client or server.</param>
/// <param name="Name">Its name, unique in the file.</param>
/// <param name="Needs">The capabilities it needs granted.</param>
/// <param name="Frame">The whole frame, its header too.</param>
/// <param name="Means">What it decodes to, in words.</param>
internal sealed record SampleFrame(string Direction, string Name, IReadOnlyList<string> Needs, byte[] Frame, string Means)
{
    /// <summary>The fixture, relative to the repository.</summary>
    internal const string Path = "test/Turborel.Data.Tests/Fixtures/05-protocol-frames.tsv";

    /// <summary>Gets every frame of the file, in its order.</summary>
    internal static IReadOnlyList<SampleFrame> All { get; } = Read(Repository.Read(Path));

    /// <summary>Gets the frame body, after its five-byte header.</summary>
    internal byte[] Body => Frame[5..];

    /// <summary>Gets the frame's kind.</summary>
    internal byte Kind => Frame[0];

    /// <summary>Gives the frame of a name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The frame.</returns>
    internal static SampleFrame Of(string name) => All.Single(f => f.Name == name);

    /// <summary>Reads the file's lines: each frame, five fields apart by one tab, comments left out.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The frames.</returns>
    /// <exception cref="FormatException">A line not of five fields, or a hex field not of upper-case digits.</exception>
    internal static IReadOnlyList<SampleFrame> Read(string text)
    {
        var frames = new List<SampleFrame>();
        foreach (string line in text.Split('\n').Where(l => l.Length > 0 && l[0] != '#'))
        {
            string[] fields = line.Split('\t');
            if (fields.Length != 5 || fields[3].Any(c => !Uri.IsHexDigit(c) || char.IsLower(c)))
            {
                throw new FormatException($"not a sample frame: {line}");
            }

            string[] needs = fields[2] == "-" ? [] : fields[2].Split(',');
            frames.Add(new SampleFrame(fields[0], fields[1], needs, Convert.FromHexString(fields[3]), fields[4]));
        }

        return frames;
    }
}
