# TODO

Open work, by milestone. Each item says what is missing. How the driver is
built is in [RULES.md](RULES.md), and what is implemented, row by row, is
in [CONFORMANCE.md](CONFORMANCE.md). Finished work leaves this file, and
git keeps its history. The driver is done when this file is empty.

Code never cites this file (R-DOC-3).

[M1 Codec](#m1-codec) · [M2 Connect](#m2-connect) ·
[M3 Statements](#m3-statements) ·
[M4 Large objects and bulk](#m4-large-objects-and-bulk) ·
[M5 Settings](#m5-settings) · [M6 Throughput](#m6-throughput) ·
[M7 Standard API](#m7-standard-api) · [M8 Parity audit](#m8-parity-audit)

---

## M1 Codec

The protocol's encoding, without I/O, each part with its codec tests.

- Frames read from and written to a stream, the 16 MiB body bound and a
  bound the program sets; every request and reply by its kind.
- The fields: integers, `i128`, flags, strings, bytes, lists, keys, with
  every count bounded before memory is taken (R-SAFE-3).
- The tail, its tags in increasing order, and each message's tags.
- All 27 data types and every value tag, at their edges: `i128` limits,
  scales, NaN and −0.0, DECFLOAT, each interval qualifier, displacements
  −12:59 to +14:00, nested arrays, rows, multisets and structured values,
  locators, UUID (tag 27), XML and its modifiers.
- JSON values (tag 25) as turborel's binary tree (its RULES.md D89), read
  to `System.Text.Json` and written from it.
- An exact number type of the driver's own over `BigInteger`, since
  `decimal` holds 96 bits and the protocol carries an `i128` of units.
- Compressed frames (`z`) with `ZLibStream`, the length bound kept.
- Malformed frames failing the connection, the page's worked example the
  first test.
- `TurborelException` carrying the rest of a failure: rule, position,
  transaction state, diagnostics.

## M2 Connect

- TCP on 7422, Unix sockets, and Windows named pipes, chosen at run time.
- TLS begun on the same port with `SslStream`.
- The mechanisms `trust`, `password`, `certificate`, `scram-sha-256` and
  `scram-sha-256-plus`, by `tls-server-end-point` (the BCL gives no
  exporter), and the SCRAM key cache; secrets refused over plain TCP.
- Hello asking for every capability but compression, Ready, Status.
- The connect and read timeouts, terminate, and cancel on a connection of
  its own.
- Live tests against `ghcr.io/turborel/turborel:nightly` in CI, with test
  certificates, SCRAM users and SERVER.TRUST_LOCAL, set up in
  `test/server/`.

## M3 Statements

- Query, and a query's rows streamed as they come.
- Prepare, execute, fetch, close and deallocate; numbered cursors;
  describe; fetch-ends.
- Errors, diagnostics, notices (the last 64 kept, or a handler) and
  conditions.
- Autocommit through `ends`; completions' rows, generated values, server
  time and commit position.
- Multi-statement queries, query attributes, ping, reset, change-user.

## M4 Large objects and bulk

- Locators, reads in 256 KiB chunks by octets or characters, create,
  append, free and hold, and streams over them.
- CopyIn, CopyData and CopyEnd, abandoned when not finished.
- CSV and TSV import; CSV and native export, and reading native exports
  back.

## M5 Settings

- The `turborel://` URI, the settings and their defaults, the
  `TURBOREL_*` variables, the service file and the password file, read as
  turborel-client reads them, its settings tests the driver's.
- Several hosts, `load_balance`, and `target` by SERVER_READ_ONLY.
- The TLS modes and channel binding.

## M6 Throughput

- Pipelines, written a 64 KiB window at a time, and skip-on-error by
  savepoints.
- Pools with PoolConfig's defaults, a Ping check and a Reset return.
- The SQL-agent: CONNECT, SET CONNECTION and DISCONNECT.

## M7 Standard API

- ADO.NET: `TurborelConnection`, `TurborelCommand`, `TurborelDataReader`,
  `TurborelParameter`, `TurborelDataSource` and `TurborelFactory`,
  async first (RULES.md D2), and the decision of which exception a misuse
  throws.
- The manual's chapters, every example run.

## M8 Parity audit

- Every row of CONFORMANCE.md checked against turborel-client and the
  protocol's page, the coverage floor held, and this file empty.
