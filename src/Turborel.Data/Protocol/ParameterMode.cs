// The mode of a procedure's parameter a CALL's `?` stands in, by the code
// Prepared's tail 20 carries it [PROTOCOL Parameter modes], as
// turborel-client's DynamicParameter gives it.

namespace Turborel.Data.Protocol;

/// <summary>The mode of the parameter a <c>?</c> stands in.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1027:Mark enums with FlagsAttribute",
    Justification = "Table 33's codes are 1, 2 and 4, and a ? has one mode, never several.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "No mode's code is 0: a ? of code 0 fails the connection.")]
public enum ParameterMode
{
    /// <summary>IN: an execution gives it a value.</summary>
    In = 1,

    /// <summary>INOUT: an execution gives it a value, and the CALL's row gives its value back.</summary>
    InOut = 2,

    /// <summary>OUT: given no value, the CALL's row gives its value.</summary>
    Out = 4,
}
