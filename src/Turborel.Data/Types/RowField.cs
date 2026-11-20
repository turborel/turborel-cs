// A field of a row type: its name and its type [PROTOCOL Data types].

namespace Turborel.Data.Types;

/// <summary>A field of a row type.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Type">Its data type.</param>
internal sealed record RowField(string Name, DataType Type);
