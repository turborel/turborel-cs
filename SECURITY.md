# Security

Report a vulnerability privately, never in a public issue: on the
repository's **Security** tab, choose **Report a vulnerability**. Only the
maintainers see the report.

What counts, for a driver:

- TLS not used, or not verified, where the program's settings asked for
  it, or a connection that falls back to less than they asked without
  saying so;
- a password, a SCRAM key or proof, or a cancel key disclosed: sent over
  plain TCP where it should not be, written to a message, a log or an
  exception, or kept where another program can read it;
- a server's reply, or bytes on the wire, that crash the client, run code
  in it, or make it take memory or time without bound.

A wrong value, a wrong error, or a crash only a program's own misuse can
cause is a bug: report it with the **Bug** form, its impact in the title
(CONTRIBUTING.md, Reporting issues). A vulnerability of the server is
reported to [turborel](https://github.com/turborel/turborel/security).

Say in the report:

- what an attacker needs, such as a position on the network, a server of
  their own, or a user;
- what they gain;
- the commit you tested (`git rev-parse --short=12 HEAD`), and the
  server's version;
- the smallest steps that show it.

You will hear back within a week. The fix is made in private and
published with an advisory that credits you, unless you ask not to be
named.
