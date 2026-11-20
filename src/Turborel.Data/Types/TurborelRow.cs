// A row value: a value for each of its fields [PROTOCOL Values].

using System.Collections.Generic;

namespace Turborel.Data.Types;

/// <summary>A value of a row type.</summary>
/// <param name="Fields">A value for each field, in order.</param>
public sealed record TurborelRow(IReadOnlyList<object?> Fields);
