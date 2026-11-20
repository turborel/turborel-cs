# Conformance

The parity matrix: every part of turborel's native protocol, version 1,
and every feature of turborel-client, with whether turborel-cs has it.
The driver is done when every row is done. Rows follow their
documents in order: The Native Protocol, `doc/client/05-protocol.md`; The
Client Library, `doc/client/02-library.md`; and Settings, Pipelines and
Pools, `doc/client/02-library-connections.md`, at the turborel commit
VERSIONING.md names. Then the standard API, ADO.NET (RULES.md D2).

Each row names the section it is cited by (RULES.md D9), the milestone
that builds it (TODO.md), and its status: **not yet**, or **done** with
the choices it made. A row changes with the change that implements it
(R-TRACE-3).

[The protocol](#the-protocol) · [The client library](#the-client-library) ·
[Settings, pipelines and pools](#settings-pipelines-and-pools) ·
[The standard API](#the-standard-api)

---

## The protocol

### Frames

| Item | Section | Milestone | Status |
|---|---|---|---|
| The frame header: kind `u8`, length `u32` little-endian; its encoding and decoding exist, reading frames from a stream does not | [PROTOCOL Frames] | M1 | not yet |
| A kind the reader does not know, a body cut short, or bytes left over fail the connection (-919, 08006) | [PROTOCOL Frames] | M1 | not yet |
| Replies taken up to 16 MiB of body, or the bound the program sets | [PROTOCOL Frames] | M1 | not yet |
| Compressed frames (`z`), a zlib stream inflated to at most its length; nested or ungranted ones fail | [PROTOCOL Compressed frames] | M1 | not yet |
| A threshold of the client's own for compressing what it sends | [PROTOCOL Compressed frames] | M1 | not yet |

### Fields

| Item | Section | Milestone | Status |
|---|---|---|---|
| `u8`, `u32`, `u64`, little-endian; `i32 as u32` and `i64 as u64` | [PROTOCOL Values and fields] | M1 | not yet |
| `i128`, two's complement | [PROTOCOL Values and fields] | M1 | not yet |
| flag, any byte but 0 or 1 failing the connection | [PROTOCOL Values and fields] | M1 | not yet |
| string, bytes, list and strings, each count bounded by what follows | [PROTOCOL Values and fields] | M1 | not yet |
| key, 16 bytes | [PROTOCOL Values and fields] | M1 | not yet |

### Data types

| Item | Section | Milestone | Status |
|---|---|---|---|
| Type tag 1: CHARACTER, length and units | [PROTOCOL Data types] | M1 | not yet |
| Type tag 2: exact numeric, precision and scale | [PROTOCOL Data types] | M1 | not yet |
| Type tag 3: approximate numeric, precision | [PROTOCOL Data types] | M1 | not yet |
| Type tag 4: a plug-in's type, its name and parameters, shown by its values' text | [PROTOCOL Data types] | M1 | not yet |
| Type tag 5: CHARACTER VARYING, length and units | [PROTOCOL Data types] | M1 | not yet |
| Type tag 6: BINARY, length | [PROTOCOL Data types] | M1 | not yet |
| Type tag 7: BINARY VARYING, length | [PROTOCOL Data types] | M1 | not yet |
| Type tag 8: DATE, precision | [PROTOCOL Data types] | M1 | not yet |
| Type tag 9: TIME, precision | [PROTOCOL Data types] | M1 | not yet |
| Type tag 10: TIME WITH TIME ZONE, precision | [PROTOCOL Data types] | M1 | not yet |
| Type tag 11: TIMESTAMP, precision | [PROTOCOL Data types] | M1 | not yet |
| Type tag 12: TIMESTAMP WITH TIME ZONE, precision | [PROTOCOL Data types] | M1 | not yet |
| Type tag 13: INTERVAL, its fields and precisions | [PROTOCOL Data types] | M1 | not yet |
| Type tag 14: BOOLEAN | [PROTOCOL Data types] | M1 | not yet |
| Type tag 15: DECFLOAT, precision | [PROTOCOL Data types] | M1 | not yet |
| Type tag 16: array, maximum cardinality and element type | [PROTOCOL Data types] | M1 | not yet |
| Type tag 17: row, its fields' names and types | [PROTOCOL Data types] | M1 | not yet |
| Type tag 18: CHARACTER LARGE OBJECT, length and units | [PROTOCOL Data types] | M1 | not yet |
| Type tag 19: BINARY LARGE OBJECT, length and units | [PROTOCOL Data types] | M1 | not yet |
| Type tag 22: multiset, element type | [PROTOCOL Data types] | M1 | not yet |
| Type tag 23: structured type, its names and supertypes | [PROTOCOL Data types] | M1 | not yet |
| Type tag 24: reference type, its type and scope | [PROTOCOL Data types] | M1 | not yet |
| Type tag 25: JSON | [PROTOCOL Data types] | M1 | not yet |
| Type tag 26: XML, its modifiers and schema | [PROTOCOL Data types] | M1 | not yet |
| Type tag 27: UUID, no parameters (turborel's RULES.md D184) | [PROTOCOL Data types] | M1 | not yet |

### Values

| Item | Section | Milestone | Status |
|---|---|---|---|
| Value tag 0: null | [PROTOCOL Values] | M1 | not yet |
| Value tag 1: character string | [PROTOCOL Values] | M1 | not yet |
| Value tag 2: exact number, units `i128` and scale; a type of the driver's own over `BigInteger` | [PROTOCOL Values] | M1 | not yet |
| Value tag 3: approximate number, an IEEE 754 double's bits, NaN and −0.0 kept | [PROTOCOL Values] | M1 | not yet |
| Value tag 4: a plug-in's value, its type's name, text and bytes | [PROTOCOL Values] | M1 | not yet |
| Value tag 6: binary string | [PROTOCOL Values] | M1 | not yet |
| Value tag 8: date | [PROTOCOL Values] | M1 | not yet |
| Value tag 9: time | [PROTOCOL Values] | M1 | not yet |
| Value tag 11: timestamp: zoned, precision, displacement −12:59 to +14:00, instant in UTC microseconds | [PROTOCOL Values] | M1 | not yet |
| Value tag 13: interval, its qualifier and units | [PROTOCOL Values] | M1 | not yet |
| Value tag 14: truth value; unknown as null | [PROTOCOL Values] | M1 | not yet |
| Value tag 15: decimal floating point, coefficient and exponent | [PROTOCOL Values] | M1 | not yet |
| Value tag 16: array, nested | [PROTOCOL Values] | M1 | not yet |
| Value tag 17: row | [PROTOCOL Values] | M1 | not yet |
| Value tag 20: large object, whole | [PROTOCOL Values] | M1 | not yet |
| Value tag 21: large object, by locator | [PROTOCOL Values] | M1 | not yet |
| Value tag 22: multiset | [PROTOCOL Values] | M1 | not yet |
| Value tag 23: structured value | [PROTOCOL Values] | M1 | not yet |
| Value tag 24: reference | [PROTOCOL Values] | M1 | not yet |
| Value tag 25: JSON, as turborel's binary tree (its RULES.md D89): read to a JSON value, written from one | [PROTOCOL Values] | M1 | not yet |
| Value tag 26: XML, its serialization as CONTENT | [PROTOCOL Values] | M1 | not yet |
| Value tag 27: UUID, its 16 octets as bytes (turborel's RULES.md D184) | [PROTOCOL Values] | M1 | not yet |

### Composite fields

| Item | Section | Milestone | Status |
|---|---|---|---|
| row | [PROTOCOL Composite fields] | M1 | not yet |
| column: named, name, data type, not null | [PROTOCOL Composite fields] | M1 | not yet |
| parameter: varying, data type | [PROTOCOL Composite fields] | M1 | not yet |
| failure: SQLCODE, SQLSTATE, message, rule, position, active, tail 7 to 15 | [PROTOCOL Composite fields] | M1 | not yet |

### The tail

| Item | Section | Milestone | Status |
|---|---|---|---|
| Tags in increasing order; a tag the message does not take, or out of order, fails the connection | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 1: attributes, at most 32, names 1 to 64 bytes, values at most 1024 | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 2: multi | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 3: cursor | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 4: position | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 5: more | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 6: rows | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 7: severity | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 8: detail | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 9: hint | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 10: context | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 11: schema | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 12: table | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 13: column | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 14: data type | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 15: constraint | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 16: generated | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 17: conditions, each a failure as bytes | [PROTOCOL The tail] | M1 | not yet |
| Tail tag 18: time: total, queued, locks | [PROTOCOL The tail] | M1 | not yet |

### Opening a connection

| Item | Section | Milestone | Status |
|---|---|---|---|
| Hello, the Challenge and Answer steps, Status at login, Ready | [PROTOCOL Opening a connection] | M2 | not yet |
| A version other than 1 refused (08004), a user or mechanism refused (28000), told to the program | [PROTOCOL Opening a connection] | M2 | not yet |
| TLS begun on the same port, its first byte 22 | [PROTOCOL Opening a connection] | M2 | not yet |

### Mechanisms

| Item | Section | Milestone | Status |
|---|---|---|---|
| `trust` | [PROTOCOL Mechanisms] | M2 | not yet |
| `password`, only over TLS, a Unix socket or a named pipe | [PROTOCOL Mechanisms] | M2 | not yet |
| `certificate`, by the TLS client certificate | [PROTOCOL Mechanisms] | M2 | not yet |
| `scram-sha-256`, RFC 5802 and RFC 7677, the server's signature checked | [PROTOCOL Mechanisms] | M2 | not yet |
| `scram-sha-256-plus` by `tls-exporter` over TLS 1.3 | [PROTOCOL Mechanisms] | M2 | not yet |
| `scram-sha-256-plus` by `tls-server-end-point` over TLS 1.2 | [PROTOCOL Mechanisms] | M2 | not yet |

### Capabilities

| Item | Section | Milestone | Status |
|---|---|---|---|
| `notices` | [PROTOCOL Capabilities] | M3 | not yet |
| `status` | [PROTOCOL Capabilities] | M2 | not yet |
| `diagnostics` | [PROTOCOL Capabilities] | M3 | not yet |
| `describe` | [PROTOCOL Capabilities] | M3 | not yet |
| `cursors` | [PROTOCOL Capabilities] | M3 | not yet |
| `ping` | [PROTOCOL Capabilities] | M3 | not yet |
| `reset` | [PROTOCOL Capabilities] | M3 | not yet |
| `change-user` | [PROTOCOL Capabilities] | M3 | not yet |
| `query-attributes` | [PROTOCOL Capabilities] | M3 | not yet |
| `connection-attributes` | [PROTOCOL Capabilities] | M2 | not yet |
| `multi-statement` | [PROTOCOL Capabilities] | M3 | not yet |
| `compression` | [PROTOCOL Capabilities] | M1 | not yet |
| `commit-position` | [PROTOCOL Capabilities] | M3 | not yet |
| `copy` | [PROTOCOL Capabilities] | M4 | not yet |
| `row-counts` | [PROTOCOL Capabilities] | M3 | not yet |
| `generated-values` | [PROTOCOL Capabilities] | M3 | not yet |
| `fetch-ends` | [PROTOCOL Capabilities] | M3 | not yet |
| `conditions` | [PROTOCOL Capabilities] | M3 | not yet |
| No request or tail field of a capability not granted is sent | [PROTOCOL Capabilities] | M2 | not yet |

### Requests

| Item | Section | Milestone | Status |
|---|---|---|---|
| Hello 'H' | [PROTOCOL Hello 'H'] | M2 | not yet |
| Answer 'A' | [PROTOCOL Answer 'A'] | M2 | not yet |
| Query 'Q' | [PROTOCOL Query 'Q'] | M3 | not yet |
| Prepare 'P' | [PROTOCOL Prepare 'P'] | M3 | not yet |
| Execute 'E' | [PROTOCOL Execute 'E'] | M3 | not yet |
| Fetch 'F' | [PROTOCOL Fetch 'F'] | M3 | not yet |
| Close 'C' | [PROTOCOL Close 'C'] | M3 | not yet |
| Deallocate 'D' | [PROTOCOL Deallocate 'D'] | M3 | not yet |
| Lob 'L' | [PROTOCOL Lob 'L'] | M4 | not yet |
| Cancel 'Z' | [PROTOCOL Cancel 'Z'] | M2 | not yet |
| Terminate 'X' | [PROTOCOL Terminate 'X'] | M2 | not yet |
| Describe 'Y' | [PROTOCOL Describe 'Y'] | M3 | not yet |
| Ping 'G' | [PROTOCOL Ping 'G'] | M3 | not yet |
| Reset 'W' | [PROTOCOL Reset 'W'] | M3 | not yet |
| ChangeUser 'U' | [PROTOCOL ChangeUser 'U'] | M3 | not yet |
| CopyIn 'I' | [PROTOCOL CopyIn 'I'] | M4 | not yet |
| CopyData 'J' | [PROTOCOL CopyData 'J'] | M4 | not yet |
| CopyEnd 'N' | [PROTOCOL CopyEnd 'N'] | M4 | not yet |
| Requests sent before the last is answered, replies read in turn | [PROTOCOL Requests] | M6 | not yet |

### Replies

| Item | Section | Milestone | Status |
|---|---|---|---|
| Challenge 'C' | [PROTOCOL Challenge 'C'] | M2 | not yet |
| Ready 'R' | [PROTOCOL Ready 'R'] | M2 | not yet |
| Prepared 'P' | [PROTOCOL Prepared 'P'] | M3 | not yet |
| Description 'D' | [PROTOCOL Description 'D'] | M3 | not yet |
| Rows 'B' | [PROTOCOL Rows 'B'] | M3 | not yet |
| Suspended 'S' | [PROTOCOL Suspended 'S'] | M3 | not yet |
| Complete 'K' | [PROTOCOL Complete 'K'] | M3 | not yet |
| Error 'E' | [PROTOCOL Error 'E'] | M3 | not yet |
| Notice 'N' | [PROTOCOL Notice 'N'] | M3 | not yet |
| Status 'T' | [PROTOCOL Status 'T'] | M2 | not yet |
| Pong 'G' | [PROTOCOL Pong 'G'] | M3 | not yet |
| Lob 'L', data or locator | [PROTOCOL Lob 'L'] | M4 | not yet |

### Status names

| Item | Section | Milestone | Status |
|---|---|---|---|
| AUTHORIZATION, SCHEMA, READ_ONLY and SERVER_VERSION | [PROTOCOL Status 'T'] | M2 | not yet |
| SERVER_READ_ONLY (YES or NO), CATALOG, and OWNER (YES or NO), which the server sends though the page does not list them (turborel's `server/src/native/status.rs`) | [PROTOCOL Status 'T'] | M2 | not yet |
| Every setting a session may set for itself, and a setting reset told again | [PROTOCOL Status 'T'] | M2 | not yet |

### Conversations, errors and notices

| Item | Section | Milestone | Status |
|---|---|---|---|
| Replies in their fixed order: rows, notices, statuses, then the last reply | [PROTOCOL Conversations] | M3 | not yet |
| Severity ERROR, FATAL, WARNING or NOTICE | [PROTOCOL Errors and notices] | M3 | not yet |
| Conditions told one way: in Complete with `conditions`, else as Notices | [PROTOCOL Errors and notices] | M3 | not yet |
| SQLSTATE 01T01 and TBR01 | [PROTOCOL Errors and notices] | M3 | not yet |
| An Error of class 08 but 08003, or of class 28 at login, closes the connection | [PROTOCOL Error 'E'] | M3 | not yet |
| The worked example, byte for byte | [PROTOCOL A worked example] | M1 | not yet |

---

## The client library

### Connecting

| Item | Section | Milestone | Status |
|---|---|---|---|
| Connecting over TCP | [CLIENT Connecting] | M2 | not yet |
| Connecting over a Unix socket, or on Windows a named pipe, `\\.\pipe\turborel` | [CLIENT Connecting] | M2 | not yet |
| Connecting to a target, TCP or local, over TLS or not | [CLIENT Connecting] | M2 | not yet |
| SCRAM-SHA-256 of a password, refused over plain TCP unless the program says the server allows it | [CLIENT Connecting] | M2 | not yet |
| SCRAM-SHA-256-PLUS over TLS, and binding a TLS stream the program opened | [CLIENT Connecting] | M2 | not yet |
| `Password`, sent whole, only over TLS, a Unix socket or a named pipe | [CLIENT Connecting] | M2 | not yet |
| `Certificate`, a client certificate chain and key presented | [CLIENT Connecting] | M2 | not yet |
| `Trust` | [CLIENT Connecting] | M2 | not yet |
| A secret mechanism refused over plain TCP before connecting (28000) | [CLIENT Connecting] | M2 | not yet |
| Authorities trusted from a file, the platform's, or both; SSL_CERT_FILE and SSL_CERT_DIR | [CLIENT Connecting] | M2 | not yet |
| Terminate, its commit's failure given, waiting at most 60 seconds | [CLIENT Connecting] | M2 | not yet |

### Timeouts

| Item | Section | Milestone | Status |
|---|---|---|---|
| Connect timeout, 30 seconds by default, for connecting, TLS and each login step | [CLIENT Timeouts] | M2 | not yet |
| Read timeout, none by default, and changing it on a connection | [CLIENT Timeouts] | M2 | not yet |
| A wait that runs out loses the connection (08006) | [CLIENT Timeouts] | M2 | not yet |
| The longest reply taken, 16 MiB by default, set by the program | [CLIENT Timeouts] | M1 | not yet |

### Running statements

| Item | Section | Milestone | Status |
|---|---|---|---|
| Running a statement: a query's columns and rows, or a completion | [CLIENT Running statements] | M3 | not yet |
| A query's rows given a batch at a time as they come | [CLIENT Running statements] | M3 | not yet |
| A failure's SQLCODE, SQLSTATE, message, rule and position | [CLIENT Running statements] | M3 | not yet |
| Values of every type given in .NET types | [CLIENT Running statements] | M1 | not yet |
| Whether a transaction is active, as the server last said | [CLIENT Running statements] | M3 | not yet |
| Cancelling the running statement from another thread, within 10 seconds or a limit given | [CLIENT Running statements] | M2 | not yet |
| Autocommit, each statement its own transaction within its request | [CLIENT Running statements] | M3 | not yet |

### Prepared statements

| Item | Section | Milestone | Status |
|---|---|---|---|
| Prepare, execute, the parameters' types and the columns | [CLIENT Prepared statements] | M3 | not yet |
| A cursor opened, fetched and closed; deallocate | [CLIENT Prepared statements] | M3 | not yet |

### Large objects

| Item | Section | Milestone | Status |
|---|---|---|---|
| A large object up to 64 KiB whole in its row, a longer one by its locator | [CLIENT Large objects] | M4 | not yet |
| Reading a range by octets or characters, a chunk of at most 256 KiB at a time | [CLIENT Large objects] | M4 | not yet |
| A stream that reads a large object whole | [CLIENT Large objects] | M4 | not yet |
| Freeing a locator, and holding it past its commit | [CLIENT Large objects] | M4 | not yet |
| Writing a large object a piece at a time and passing it as a parameter; a CLOB's character split between writes | [CLIENT Large objects] | M4 | not yet |
| An import's field over 64 KiB sent as a large object | [CLIENT Large objects] | M4 | not yet |

### XML values

| Item | Section | Milestone | Status |
|---|---|---|---|
| An XML value's text, and its nodes parsed | [CLIENT XML values] | M3 | not yet |
| An XML parameter of its text | [CLIENT XML values] | M3 | not yet |
| A value the server cannot serialize failing the fetch (2200W) | [CLIENT XML values] | M3 | not yet |

### UUIDs

| Item | Section | Milestone | Status |
|---|---|---|---|
| A UUID's 16 octets and its text | [CLIENT UUIDs] | M3 | not yet |
| A UUID parameter of its octets or text | [CLIENT UUIDs] | M3 | not yet |

### Importing

| Item | Section | Milestone | Status |
|---|---|---|---|
| Importing the rows of a source into a table, in bounded memory | [CLIENT Importing] | M4 | not yet |
| CSV and tab-separated text, with a header, a delimiter and a text for null | [CLIENT Importing] | M4 | not yet |
| Rows committed a batch at a time, 1000 by default, or all at once | [CLIENT Importing] | M4 | not yet |
| Skipping a row that fails, each given to the program | [CLIENT Importing] | M4 | not yet |
| An import's failure: where, why, rows committed and rows rolled back | [CLIENT Importing] | M4 | not yet |

### What a connection asks for

| Item | Section | Milestone | Status |
|---|---|---|---|
| Every capability asked for but compression | [CLIENT What a connection asks for] | M2 | not yet |
| An offer of the program's own: none, compressed, connection attributes | [CLIENT What a connection asks for] | M2 | not yet |
| What was granted, and a method needing a capability not granted failing before it sends | [CLIENT What a connection asks for] | M2 | not yet |

### What the server tells

| Item | Section | Milestone | Status |
|---|---|---|---|
| A session setting as last told, with no round trip, and all of them | [CLIENT What the server tells] | M2 | not yet |
| Notices to a handler, or the last 64 kept and taken | [CLIENT What the server tells] | M3 | not yet |
| The last statement's conditions | [CLIENT What the server tells] | M3 | not yet |
| Rows affected | [CLIENT What the server tells] | M3 | not yet |
| Identity values generated | [CLIENT What the server tells] | M3 | not yet |
| The server's time: total, queued and locks | [CLIENT What the server tells] | M3 | not yet |
| The last commit's log position | [CLIENT What the server tells] | M3 | not yet |
| A failure's diagnostics: severity, detail, hint, context, schema, table, column, data type, constraint | [CLIENT What the server tells] | M3 | not yet |
| A cursor under autocommit ending its transaction as it ends, with `fetch-ends` | [CLIENT What the server tells] | M3 | not yet |

### Controlling the session

| Item | Section | Milestone | Status |
|---|---|---|---|
| Ping | [CLIENT Controlling the session] | M3 | not yet |
| Reset | [CLIENT Controlling the session] | M3 | not yet |
| Change user | [CLIENT Controlling the session] | M3 | not yet |
| Notice 01T01 of a transaction rolled back | [CLIENT Controlling the session] | M3 | not yet |
| Query attributes carried by every later statement | [CLIENT Controlling the session] | M3 | not yet |
| Several statements as one request, each outcome and the failure that stopped the rest | [CLIENT Controlling the session] | M3 | not yet |
| Describing a prepared statement again | [CLIENT Controlling the session] | M3 | not yet |
| Numbered cursors of one statement, and an open cursor's columns | [CLIENT Controlling the session] | M3 | not yet |

### Copying and exporting

| Item | Section | Milestone | Status |
|---|---|---|---|
| Copy in: rows sent many to a message, finished with the rows inserted | [CLIENT Copying and exporting] | M4 | not yet |
| A copy all or nothing, its failure naming the row; one dropped before it finishes abandoned | [CLIENT Copying and exporting] | M4 | not yet |
| Copying the rows an enumeration gives | [CLIENT Copying and exporting] | M4 | not yet |
| Importing by copy where the server grants it and no column is a large object | [CLIENT Copying and exporting] | M4 | not yet |
| Export as CSV, a header then a record a row | [CLIENT Copying and exporting] | M4 | not yet |
| Export as the protocol's own frames, and reading it back | [CLIENT Copying and exporting] | M4 | not yet |

### The SQL-agent

| Item | Section | Milestone | Status |
|---|---|---|---|
| Named connections, one current, and CONNECT, SET CONNECTION and DISCONNECT run by the agent | [CLIENT The SQL-agent] | M6 | not yet |
| A connector the program gives making the connections CONNECT asks for | [CLIENT The SQL-agent] | M6 | not yet |

---

## Settings, pipelines and pools

### Connecting by settings

| Item | Section | Milestone | Status |
|---|---|---|---|
| Connecting as the settings say | [CLIENT Connecting by settings] | M5 | not yet |
| A password proved by SCRAM, else sent whole; no password, a certificate, else trust | [CLIENT Connecting by settings] | M5 | not yet |

### Where settings come from

| Item | Section | Milestone | Status |
|---|---|---|---|
| The setting `host`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `port`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `user`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `password`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `tls_mode`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `channel_binding`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `application_name`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `connect_timeout`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `service`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `service_file`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `passfile`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `target`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `load_balance`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `tls_roots`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `tls_system_roots`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `tls_certificate` and `tls_key`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| The setting `server_name`, with its default | [CLIENT Where settings come from] | M5 | not yet |
| Layers: the program's, the service file's section, the environment, the defaults | [CLIENT Where settings come from] | M5 | not yet |
| TURBOREL_HOST, _PORT, _USER, _PASSWORD, _TLS_MODE, _APPLICATION_NAME, _CONNECT_TIMEOUT, _SERVICE, _SERVICE_FILE and _PASSFILE; set but empty gives nothing | [CLIENT Where settings come from] | M5 | not yet |
| The environment read through a function the program gives | [CLIENT Where settings come from] | M5 | not yet |
| Settings that cannot be read, or name no user, refused (08001) | [CLIENT Where settings come from] | M5 | not yet |
| `application_name` sent as the connection attribute `application` | [CLIENT Where settings come from] | M5 | not yet |

### The connection URI

| Item | Section | Milestone | Status |
|---|---|---|---|
| `turborel://` with user, password, several hosts and ports, and settings | [CLIENT The connection URI] | M5 | not yet |
| Percent-encoding, a Unix socket's path, IPv6 in brackets | [CLIENT The connection URI] | M5 | not yet |
| Another scheme, a path, an unknown setting or a pair without `=` refused (08001) | [CLIENT The connection URI] | M5 | not yet |

### The service file

| Item | Section | Milestone | Status |
|---|---|---|---|
| Sections of settings, comments and blank lines; `service` and `service_file` refused in a section | [CLIENT The service file] | M5 | not yet |
| A malformed line refused, its file and line named | [CLIENT The service file] | M5 | not yet |

### The password file

| Item | Section | Milestone | Status |
|---|---|---|---|
| `host:port:user:password` lines, `*` matching anything, `\` escaping, comments; the first match taken | [CLIENT The password file] | M5 | not yet |
| A Unix socket matched by its path and the default port | [CLIENT The password file] | M5 | not yet |
| Read only if a regular file | [CLIENT The password file] | M5 | not yet |

### Several servers

| Item | Section | Milestone | Status |
|---|---|---|---|
| Hosts tried in order, or at random with `load_balance=random` | [CLIENT Several servers] | M5 | not yet |
| Each address of a name tried, each step within the connect timeout | [CLIENT Several servers] | M5 | not yet |
| A failure naming each address and why (08001; 28000 if the last refused the user) | [CLIENT Several servers] | M5 | not yet |
| `target=read-write` and `read-only` by SERVER_READ_ONLY, else INFORMATION_SCHEMA.REPLICAS | [CLIENT Several servers] | M5 | not yet |
| The role a connection's session sees | [CLIENT Several servers] | M5 | not yet |

### TLS by mode

| Item | Section | Milestone | Status |
|---|---|---|---|
| `disable` | [CLIENT TLS by mode] | M5 | not yet |
| `prefer`, falling back to plain TCP when the handshake fails | [CLIENT TLS by mode] | M5 | not yet |
| `require` | [CLIENT TLS by mode] | M5 | not yet |
| `verify-ca` | [CLIENT TLS by mode] | M5 | not yet |
| `verify-full`, against `server_name` or the host's | [CLIENT TLS by mode] | M5 | not yet |
| `channel_binding` `disable`, `prefer` and `require`, refusals before any connection (28000) | [CLIENT TLS by mode] | M5 | not yet |

### Pipelines

| Item | Section | Milestone | Status |
|---|---|---|---|
| Requests queued and run, one outcome each, in order | [CLIENT Pipelines] | M6 | not yet |
| Written a window of 64 KiB at a time, its replies read before the next | [CLIENT Pipelines] | M6 | not yet |
| Each request its own transaction under autocommit | [CLIENT Pipelines] | M6 | not yet |
| Skip on error by savepoints, the rest skipped, the batch one transaction | [CLIENT Pipelines] | M6 | not yet |
| A lost connection or a failed undo failing the run | [CLIENT Pipelines] | M6 | not yet |

### Pools

| Item | Section | Milestone | Status |
|---|---|---|---|
| A bounded set of connections shared by threads; get, and return on dispose | [CLIENT Pools] | M6 | not yet |
| `max` 10, `max_idle` 10, `idle_timeout` 10 minutes, `max_lifetime` 1 hour, `wait` 30 seconds, `check` yes, `reset` none | [CLIENT Pools] | M6 | not yet |
| A connection returned by Reset, autocommit off, then the program's reset statements | [CLIENT Pools] | M6 | not yet |
| The check by Ping; of a server with neither, ROLLBACK and a pipeline | [CLIENT Pools] | M6 | not yet |
| Connections lost, failing reset, past their lifetime or beyond `max_idle` closed | [CLIENT Pools] | M6 | not yet |
| `get` failing with 08001 past `wait`, or with the failure of making one | [CLIENT Pools] | M6 | not yet |
| A pool made of settings or of a source the program gives; no thread of its own | [CLIENT Pools] | M6 | not yet |

### The SCRAM key cache

| Item | Section | Milestone | Status |
|---|---|---|---|
| Keys a proved login derived kept, found by salt, iterations and a keyed tag of the password | [CLIENT The SCRAM key cache] | M2 | not yet |
| At most 64 entries, the oldest dropped; an entry dropped when its login fails | [CLIENT The SCRAM key cache] | M2 | not yet |
| One cache shared by every connection settings make, and one given to a mechanism | [CLIENT The SCRAM key cache] | M5 | not yet |
| Hits and misses counted | [CLIENT The SCRAM key cache] | M2 | not yet |

---

## The standard API

ADO.NET (RULES.md D2), async first. Its rows cite the decision.

| Item | Section | Milestone | Status |
|---|---|---|---|
| `TurborelConnection`, a `DbConnection` | [DESIGN] D2 | M7 | not yet |
| `TurborelCommand`, a `DbCommand` | [DESIGN] D2 | M7 | not yet |
| `TurborelDataReader`, a `DbDataReader` | [DESIGN] D2 | M7 | not yet |
| `TurborelParameter`, a `DbParameter` | [DESIGN] D2 | M7 | not yet |
| `TurborelDataSource`, a `DbDataSource` | [DESIGN] D2 | M7 | not yet |
| `TurborelFactory`, a `DbProviderFactory` | [DESIGN] D2 | M7 | not yet |
| `TurborelTransaction`, a `DbTransaction` | [DESIGN] D2 | M7 | not yet |
| `TurborelBatch`, a `DbBatch`, over pipelines | [DESIGN] D2 | M7 | not yet |
| `TurborelConnectionStringBuilder`, a `DbConnectionStringBuilder` of turborel-client's settings | [DESIGN] D2 | M7 | not yet |
| `TurborelException`, a `DbException`, carrying the whole failure | [DESIGN] D2 | M7 | not yet |
