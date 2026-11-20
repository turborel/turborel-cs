// The statements of an SQL-agent's connections [CLIENT The SQL-agent],
// read as turborel's parser reads them [ISO/IEC 9075:1992 §15]: CONNECT
// TO server [AS name] [USER user] or TO DEFAULT; SET CONNECTION name or
// DEFAULT; DISCONNECT name, DEFAULT, ALL or CURRENT; each name a simple
// value specification, which a client's agent takes as a character
// string literal. A statement that does not begin as one is none.

using System.Collections.Generic;
using System.Text;

namespace Turborel.Data.Agent;

/// <summary>What a connection statement does.</summary>
internal enum ConnectionVerb
{
    /// <summary>CONNECT TO a server.</summary>
    Connect,

    /// <summary>CONNECT TO DEFAULT.</summary>
    ConnectDefault,

    /// <summary>SET CONNECTION.</summary>
    Set,

    /// <summary>DISCONNECT.</summary>
    Disconnect,
}

/// <summary>What a DISCONNECT ends.</summary>
internal enum DisconnectObject
{
    /// <summary>A connection by its name.</summary>
    Named,

    /// <summary>The default connection.</summary>
    Default,

    /// <summary>Every connection.</summary>
    All,

    /// <summary>The current connection.</summary>
    Current,
}

/// <summary>A connection statement read: its verb, and its names, each a value as written.</summary>
/// <param name="Verb">What it does.</param>
/// <param name="Server">CONNECT's server.</param>
/// <param name="Name">CONNECT's AS name, SET CONNECTION's name, or DISCONNECT's named connection; null for DEFAULT.</param>
/// <param name="User">CONNECT's USER.</param>
/// <param name="Object">What DISCONNECT ends.</param>
internal sealed record ConnectionStatement(ConnectionVerb Verb, SimpleValue? Server, SimpleValue? Name, SimpleValue? User, DisconnectObject Object)
{
    private const int SyntaxSqlCode = -102;
    private const string SyntaxState = "2A000";

    /// <summary>Reads <paramref name="sql"/> as a connection statement, if it begins as one.</summary>
    /// <param name="sql">The statement.</param>
    /// <returns>The statement; null if it is another.</returns>
    /// <exception cref="TurborelException">SQLCODE -102, SQLSTATE 2A000, if it begins as one and is not one.</exception>
    internal static ConnectionStatement? Read(string sql)
    {
        List<Token> tokens = Lex(sql);
        bool connection = tokens is [{ Word: "CONNECT" or "DISCONNECT" }, ..] or [{ Word: "SET" }, { Word: "CONNECTION" }, ..];
        if (!connection)
        {
            return null;
        }

        var reader = new Reader(tokens);
        ConnectionStatement statement = reader.Statement();
        reader.End();
        return statement;
    }

    /// <summary>The failure of a statement that is not one.</summary>
    /// <param name="message">Why.</param>
    /// <returns>The failure.</returns>
    internal static TurborelException Syntax(string message) => new(SyntaxSqlCode, SyntaxState, message);

    // The tokens of a statement: key words and other words in upper case,
    // character string literals, and anything else as written.
    private static List<Token> Lex(string sql)
    {
        var tokens = new List<Token>();
        int at = 0;
        while (at < sql.Length)
        {
            char c = sql[at];
            if (char.IsWhiteSpace(c) || (c == ';' && sql[(at + 1)..].Trim().Length == 0))
            {
                at++;
            }
            else if (c == '\'')
            {
                (string text, at) = Literal(sql, at);
                tokens.Add(new Token(null, text, $"'{text}'"));
            }
            else if (char.IsLetter(c))
            {
                int start = at;
                while (at < sql.Length && (char.IsLetterOrDigit(sql[at]) || sql[at] == '_'))
                {
                    at++;
                }

                tokens.Add(new Token(sql[start..at].ToUpperInvariant(), null, sql[start..at]));
            }
            else
            {
                tokens.Add(new Token(null, null, c.ToString()));
                at++;
            }
        }

        return tokens;
    }

    // A character string literal from its quote, a quote in it written twice.
    private static (string Text, int End) Literal(string sql, int at)
    {
        var text = new StringBuilder();
        for (int i = at + 1; i < sql.Length; i++)
        {
            if (sql[i] != '\'')
            {
                _ = text.Append(sql[i]);
            }
            else if (i + 1 < sql.Length && sql[i + 1] == '\'')
            {
                _ = text.Append('\'');
                i++;
            }
            else
            {
                return (text.ToString(), i + 1);
            }
        }

        throw Syntax("[X3.135-1986 §5.2] a character string literal is not closed");
    }

    // A token: a word in upper case, a literal's text, and how it was written.
    private sealed record Token(string? Word, string? Literal, string Written);

    // Reads a statement's tokens in turn.
    private sealed class Reader(List<Token> tokens)
    {
        private int _at;

        internal ConnectionStatement Statement()
        {
            switch (Take().Word)
            {
                case "CONNECT":
                    Expect("TO");
                    if (Accept("DEFAULT"))
                    {
                        return new ConnectionStatement(ConnectionVerb.ConnectDefault, null, null, null, DisconnectObject.Named);
                    }

                    SimpleValue server = Value();
                    SimpleValue? name = Accept("AS") ? Value() : null;
                    SimpleValue? user = Accept("USER") ? Value() : null;
                    return new ConnectionStatement(ConnectionVerb.Connect, server, name, user, DisconnectObject.Named);
                case "SET":
                    Expect("CONNECTION");
                    return new ConnectionStatement(ConnectionVerb.Set, null, Accept("DEFAULT") ? null : Value(), null, DisconnectObject.Named);
                default:
                    return Accept("DEFAULT") ? Disconnect(DisconnectObject.Default)
                        : Accept("ALL") ? Disconnect(DisconnectObject.All)
                        : Accept("CURRENT") ? Disconnect(DisconnectObject.Current)
                        : new ConnectionStatement(ConnectionVerb.Disconnect, null, Value(), null, DisconnectObject.Named);
            }
        }

        internal void End()
        {
            if (_at < tokens.Count)
            {
                throw Syntax($"expected end of statement, found {tokens[_at].Written}");
            }
        }

        private static ConnectionStatement Disconnect(DisconnectObject what) => new(ConnectionVerb.Disconnect, null, null, null, what);

        private Token Take() => tokens[_at++];

        private bool Accept(string word)
        {
            bool found = _at < tokens.Count && tokens[_at].Word == word;
            _at += found ? 1 : 0;
            return found;
        }

        private void Expect(string word)
        {
            if (!Accept(word))
            {
                throw Syntax($"expected {word}, found {(_at < tokens.Count ? tokens[_at].Written : "end of statement")}");
            }
        }

        // A simple value specification: a character string literal, or
        // another value, which a client's agent refuses as it runs.
        private SimpleValue Value()
        {
            if (_at >= tokens.Count)
            {
                throw Syntax("expected a value, found end of statement");
            }

            Token token = Take();
            return new SimpleValue(token.Literal);
        }
    }
}

/// <summary>A simple value specification as written: a character string literal's text, or null of another value.</summary>
/// <param name="Literal">The literal's text.</param>
internal sealed record SimpleValue(string? Literal)
{
    /// <summary>Gives the character string the value is, its spaces trimmed [ISO/IEC 9075:1992 §15.1 GR2, GR6].</summary>
    /// <returns>The text.</returns>
    /// <exception cref="TurborelException">SQLCODE -103 if it is no character string literal.</exception>
    internal string Text() => Literal?.Trim(' ')
        ?? throw new TurborelException(-103, "2A000", "[DESIGN] a client's connection statement names by character string literals");
}
