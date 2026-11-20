// A reference to a row of a typed table: its octets [PROTOCOL Values].

using System;

namespace Turborel.Data.Types;

/// <summary>A value of a reference type.</summary>
/// <param name="Bytes">Its octets, as the server made them.</param>
public sealed record TurborelReference(ReadOnlyMemory<byte> Bytes);
