// A value of a structured type: its most specific type's names, then its
// attributes [PROTOCOL Values].

using System.Collections.Generic;

namespace Turborel.Data.Types;

/// <summary>A value of a structured type.</summary>
/// <param name="Schema">Its most specific type's schema.</param>
/// <param name="Name">Its most specific type's name.</param>
/// <param name="Attributes">Its attributes' values, in order.</param>
internal sealed record TurborelStructuredValue(string Schema, string Name, IReadOnlyList<object?> Attributes);
