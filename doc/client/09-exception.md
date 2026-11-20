# TurborelException

[Contents](README.md)

TurborelException is the one exception the driver reports a failure by,
a DbException carrying the failure's SQLCODE (SqlCode, and ErrorCode
too) and SQLSTATE (SqlState). It exists from M0. This page will describe
the rest of a failure it carries, its rule, position, transaction state
and diagnostics, when they join it in M1 and M3.
