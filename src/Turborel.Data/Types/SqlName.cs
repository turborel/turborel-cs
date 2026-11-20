// A schema object's qualified name, as structured and reference types
// write them [PROTOCOL Data types].

namespace Turborel.Data.Types;

/// <summary>A name in a schema.</summary>
/// <param name="Schema">The schema.</param>
/// <param name="Name">The name within it.</param>
public readonly record struct SqlName(string Schema, string Name);
