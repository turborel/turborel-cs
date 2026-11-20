// A value of a plug-in's type, which the driver need not know: its type's
// name, its text and its bytes, kept and sent back unchanged [PROTOCOL Values].

using System;

namespace Turborel.Data.Types;

/// <summary>A value of a type a plug-in of the server's defines.</summary>
/// <param name="TypeName">Its type's name.</param>
/// <param name="Text">Its text, how it is shown.</param>
/// <param name="Bytes">Its bytes, as its type writes them.</param>
internal sealed record TurborelPluginValue(string TypeName, string Text, ReadOnlyMemory<byte> Bytes);
