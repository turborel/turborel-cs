// Every data type, tags 1 to 27, written and read back byte for byte, and
// malformed ones failing the connection [PROTOCOL Data types].

using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>Tests of <see cref="DataTypeCodec"/>.</summary>
public sealed class DataTypeTests
{
    /// <summary>Each type's tag and what it writes after it, as the page's table gives them.</summary>
    /// <returns>Each type and its bytes.</returns>
    public static TheoryData<string, string> Types() => new()
    {
        { "character", "01 0A000000 00" },
        { "exact", "02 12000000 02000000" },
        { "approximate", "03 35000000" },
        { "plugin", "04 03000000 474556 02000000 01000000 02000000" },
        { "varying", "05 FFFFFFFF 01" },
        { "binary", "06 10000000" },
        { "binary varying", "07 00010000" },
        { "date", "08 00000000" },
        { "time", "09 03000000" },
        { "time zoned", "0A 06000000" },
        { "timestamp", "0B 06000000" },
        { "timestamp zoned", "0C 09000000" },
        { "interval", "0D 00000000 01000000 04000000 00000000" },
        { "boolean", "0E" },
        { "decfloat", "0F 22000000" },
        { "array", "10 0A000000 0E" },
        { "row", "11 02000000 01000000 41 02 05000000 01000000 01000000 42 0E" },
        { "clob", "12 0000000001000000 00" },
        { "blob", "13 FFFFFFFFFFFFFFFF 01" },
        { "multiset", "16 01 0A000000 00" },
        { "structured", "17 01000000 53 01000000 54 01000000 01000000 53 01000000 50" },
        { "reference", "18 01000000 53 01000000 54 00000000 01 01000000 53 01000000 58" },
        { "json", "19" },
        { "xml", "1A 00 01" },
        { "xml schema", "1A 01 02 01000000 53 01000000 4E 01 02000000 6E73 00" },
        { "uuid", "1B" },
    };

    /// <summary>Every type reads back as written, byte for byte.</summary>
    /// <param name="name">The type, for the test's name.</param>
    /// <param name="hex">Its bytes.</param>
    [Theory]
    [Verifies("[PROTOCOL Data types]")]
    [MemberData(nameof(Types))]
    public void EveryTypeReadsBackAsWritten(string name, string hex)
    {
        byte[] bytes = Wire.Hex(hex);
        var r = new WireReader(bytes, name);
        DataType type = DataTypeCodec.Read(r);
        r.Finish();

        Assert.Equal(bytes, Wire.Written(w => DataTypeCodec.Write(w, type)));
    }

    /// <summary>A type's parameters are read as the page names them.</summary>
    [Fact]
    [Verifies("[PROTOCOL Data types]")]
    public void ParametersAreReadByName()
    {
        Assert.Equal(new CharacterType(10, CharacterUnits.Characters, Varying: false), Read("01 0A000000 00"));
        Assert.Equal(new ExactType(18, 2), Read("02 12000000 02000000"));
        Assert.Equal(new DatetimeType(DatetimeMoment.Time, Zoned: true, 6), Read("0A 06000000"));
        Assert.Equal(new LargeObjectType(LargeObjectKind.Binary, ulong.MaxValue, CharacterUnits.Octets), Read("13 FFFFFFFFFFFFFFFF 01"));
        PluginType plugin = Assert.IsType<PluginType>(Read("04 03000000 474556 01000000 07000000"));
        Assert.Equal("GEV", plugin.Name);
        Assert.Equal([7u], plugin.Parameters);
        ReferenceType reference = Assert.IsType<ReferenceType>(Read("18 01000000 53 01000000 54 00000000 00"));
        Assert.Null(reference.Scope);
    }

    /// <summary>An interval's fields are numbered 0 YEAR to 5 SECOND; another fails the connection.</summary>
    [Fact]
    [Verifies("[PROTOCOL Data types]")]
    public void IntervalFieldsAreNumbered()
    {
        IntervalType day = Assert.IsType<IntervalType>(Read("0D 02000000 05000000 02000000 06000000"));

        Assert.Equal(new IntervalQualifier(IntervalField.Day, IntervalField.Second, 2, 6), day.Qualifier);
        Assert.Equal((0, 5), ((int)IntervalField.Year, (int)IntervalField.Second));
        Wire.Fails(() => Read("0D 00000000 06000000 02000000 00000000"), Sections.DataTypes);
    }

    /// <summary>Malformed types fail the connection.</summary>
    /// <param name="hex">The bytes.</param>
    [Theory]
    [Verifies("[PROTOCOL Data types]")]
    [InlineData("14")]
    [InlineData("1C")]
    [InlineData("10 00000000 0E")]
    [InlineData("10 00000100 0E")]
    [InlineData("11 00000000")]
    [InlineData("04 01000000 41 03000000 01000000 02000000 03000000")]
    [InlineData("04 01000000 41 01000000 FFFFFFFF")]
    [InlineData("18 01000000 53 01000000 54 00000000 02")]
    [InlineData("1A 03 00")]
    [InlineData("1A 02 01")]
    [InlineData("1A 00 03")]
    public void MalformedTypesFail(string hex)
    {
        Wire.Fails(() => Read(hex), Sections.DataTypes);
    }

    /// <summary>A length's units other than characters or octets is a value the client cannot take: SQLCODE -916, the connection kept.</summary>
    [Fact]
    [Verifies("[PROTOCOL Data types]")]
    [Verifies("[PROTOCOL Values and fields]")]
    public void UnitsOtherThanCharactersOrOctetsFailTheRequest()
    {
        Wire.FailsValue(() => Read("01 0A000000 02"), Sections.DataTypes);
        Wire.FailsValue(() => Read("12 0A00000000000000 02"), Sections.DataTypes);
    }

    /// <summary>Arrays nest at most 16 deep, as turborel reads them; deeper fails the connection.</summary>
    [Fact]
    [Verifies("[PROTOCOL Data types]")]
    public void NestingIsBounded()
    {
        Assert.IsType<MultisetType>(Read(Deep(WireReader.MaxNesting)));
        Wire.Fails(() => Read(Deep(WireReader.MaxNesting + 1)), Sections.Values);
    }

    /// <summary>A plug-in's type the client does not have is read by its name and parameters.</summary>
    [Fact]
    [Verifies("[PROTOCOL Data types]")]
    public void PluginTypeIsReadByName()
    {
        PluginType plugin = Assert.IsType<PluginType>(Read("04 04000000 4A554C45 00000000"));

        Assert.Equal("JULE", plugin.Name);
        Assert.Empty(plugin.Parameters);
    }

    private static string Deep(int multisets) => string.Concat(System.Linq.Enumerable.Repeat("16 ", multisets)) + "0E";

    private static DataType Read(string hex)
    {
        var r = new WireReader(Wire.Hex(hex), "type");
        DataType type = DataTypeCodec.Read(r);
        r.Finish();
        return type;
    }
}
