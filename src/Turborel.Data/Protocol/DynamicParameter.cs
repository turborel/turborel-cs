// A statement's `?` with the mode and ordinal position of the parameter it
// stands in [PROTOCOL Parameter modes], as turborel-client's
// DynamicParameter is: of a CALL, as a server granting parameter-modes
// tells it; of another statement, or told none, an input of no position.

namespace Turborel.Data.Protocol;

/// <summary>A statement's <c>?</c>: its parameter's mode, its ordinal position and its type.</summary>
/// <param name="Mode">The mode of the parameter it stands in.</param>
/// <param name="Ordinal">The parameter's ordinal position, from 1, of a CALL; null of another statement.</param>
/// <param name="Parameter">Its type.</param>
public sealed record DynamicParameter(ParameterMode Mode, uint? Ordinal, Parameter Parameter)
{
    /// <summary>Gets whether an execution gives it a value: an IN or INOUT one.</summary>
    public bool TakesValue => Mode != ParameterMode.Out;

    /// <summary>Gets whether the CALL's row gives its value back: an INOUT or OUT one.</summary>
    public bool GivesValue => Mode != ParameterMode.In;

    /// <summary>An input of no position, as every <c>?</c> is of a statement told no modes.</summary>
    /// <param name="parameter">Its type.</param>
    /// <returns>The parameter.</returns>
    internal static DynamicParameter Input(Parameter parameter) => new(ParameterMode.In, null, parameter);
}
