// The parts of an import that need no server: a table's name read as SQL
// reads one and delimited, a heading read as an identifier, a string
// assigned to its character column as INSERT assigns it, and a large
// object writer's character cut [CLIENT Importing] [CLIENT Large objects].

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Turborel.Data.Import;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Import;

/// <summary>Tests of <see cref="SqlNames"/>, <see cref="Assignment"/> and <see cref="LobWriteStream.CharacterCut"/>.</summary>
public sealed class ImportPartsTests
{
    private static readonly string[] Headings = ["name", "order", "Full Name", "\"Quoted\"", "a.b"];

    /// <summary>A table's name is read as SQL reads a table name, its parts delimited, or refused.</summary>
    /// <param name="text">The name as written.</param>
    /// <param name="sql">Its SQL; null if it is no table name.</param>
    [Theory]
    [Verifies("[CLIENT Importing]")]
    [InlineData("staff", "\"STAFF\"")]
    [InlineData(" shop . Orders ", "\"SHOP\".\"ORDERS\"")]
    [InlineData("c.s.t", "\"C\".\"S\".\"T\"")]
    [InlineData("\"Mixed \"\"Case\"\"\"", "\"Mixed \"\"Case\"\"\"")]
    [InlineData("é_1", "\"É_1\"")]
    [InlineData("a.b.c.d", null)]
    [InlineData("select", null)]
    [InlineData("a.", null)]
    [InlineData(".a", null)]
    [InlineData("a b", null)]
    [InlineData("1a", null)]
    [InlineData("\"open", null)]
    [InlineData("\"\"", null)]
    [InlineData("", null)]
    [InlineData("a;", null)]
    public void TableNamesAreReadAsSqlReadsThem(string text, string? sql) => Assert.Equal(sql, SqlNames.TableName(text));

    /// <summary>A heading names the identifier it is, a regular one or a key word in upper case, else itself as written.</summary>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public void HeadingsNameColumns()
    {
        Assert.Equal(["NAME", "ORDER", "Full Name", "Quoted", "a.b"], Headings.Select(SqlNames.ColumnName));
        Assert.Equal(new string('A', 128), SqlNames.ColumnName(new string('a', 128)));
        Assert.Equal(new string('a', 129), SqlNames.ColumnName(new string('a', 129)));
    }

    /// <summary>
    /// A string for a character string column is assigned as INSERT assigns
    /// it: padded of a fixed type, cut of spaces beyond its length, refused,
    /// SQLSTATE 22001, if more than spaces is beyond it; for a character large
    /// object, held as one; any other value as it is.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public void StringsAreAssignedAsInsertAssigns()
    {
        Parameter[] types =
        [
            new(false, new CharacterType(4, CharacterUnits.Characters, Varying: false)),
            new(false, new CharacterType(3, CharacterUnits.Characters, Varying: true)),
            new(false, new CharacterType(3, CharacterUnits.Octets, Varying: true)),
            new(false, new LargeObjectType(LargeObjectKind.Character, 4, CharacterUnits.Characters)),
            new(false, new ExactType(10, 0)),
        ];

        IReadOnlyList<object?> values = Assignment.Assigned(["ab", "xyz   ", "é", "abc", "7"], types);
        Assert.Equal(["ab  ", "xyz", "é"], values.Take(3));
        Assert.Equal("abc", Encoding.UTF8.GetString(Assert.IsType<TurborelLargeObject>(values[3]).Data!.Value.Span));
        Assert.Equal("7", values[4]);
        Assert.Equal([null, 5.5], Assignment.Assigned([null, 5.5], types));
        TurborelException varying = Assert.Throws<TurborelException>(() => Assignment.Assigned([null, "abcd"], types));
        Assert.Equal((-301, "22001"), (varying.SqlCode, varying.SqlState));
        Assert.Equal("[ISO/IEC 9075:1992 §13.8 GR4b iii, §9.2 GR3e ii] a value of 4 characters, not spaces beyond 3, does not fit CHARACTER VARYING(3)", varying.Message);
        Assert.EndsWith("a value of 5 characters, not spaces beyond 4, does not fit CHARACTER(4)", Assert.Throws<TurborelException>(() => Assignment.Assigned(["abcde"], types)).Message, System.StringComparison.Ordinal);
        Assert.EndsWith("a value of 4 octets, not spaces beyond 3, does not fit CHARACTER VARYING(3 OCTETS)", Assert.Throws<TurborelException>(() => Assignment.Assigned([null, null, "éé"], types)).Message, System.StringComparison.Ordinal);
        Assert.EndsWith("a large object longer than CHARACTER LARGE OBJECT(4), not padding beyond it, does not fit it", Assert.Throws<TurborelException>(() => Assignment.Assigned([null, null, null, "abcde"], types)).Message, System.StringComparison.Ordinal);
    }

    /// <summary>A character large object's chunk is cut where a character ends, a character the end splits left for the next.</summary>
    [Fact]
    [Verifies("[CLIENT Large objects]")]
    public void ChunksAreCutWhereACharacterEnds()
    {
        byte[] text = Encoding.UTF8.GetBytes("aé€\U0001F600");

        Assert.Equal(1, LobWriteStream.CharacterCut(text, 2));
        Assert.Equal(3, LobWriteStream.CharacterCut(text, 3));
        Assert.Equal(3, LobWriteStream.CharacterCut(text, 5));
        Assert.Equal(text.Length, LobWriteStream.CharacterCut(text, 100));
        Assert.Equal(6, LobWriteStream.CharacterCut(text[..8], 100));
        Assert.Equal(0, LobWriteStream.CharacterCut([0xC3], 1));
    }
}
