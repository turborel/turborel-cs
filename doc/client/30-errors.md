# Errors

[Contents](README.md)

This page will describe the driver's error: its SQLCODE, SQLSTATE and
message, the rule it cites, its position, whether a transaction is still
active, its diagnostics and its cause; and the failures the driver
raises itself, each with the SQLCODE and SQLSTATE turborel-client gives
it. It is filled at milestone M3.

Today the driver's error is `TurborelException`, a `DbException` of
ADO.NET, carrying the failure's SQLCODE, as `SqlCode` and `ErrorCode`,
and its SQLSTATE, as `SqlState`. A malformed message from the server
fails the connection with SQLCODE -919 and SQLSTATE 08006.
