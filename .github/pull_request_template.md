<!--
This description is the series' cover letter (CONTRIBUTING.md, Pull
requests). Each commit message still stands alone, in Linux's form.
Base the series on main, keep it rebased, and on revising say what
changed in a comment, not here.
-->

## What and why

<!-- The problem and its impact, then the approach and the choices made.
Link the issue: "Closes #N". -->

## The series

<!-- One line per commit, in order: what it does. Each passes every check
on its own. -->

1.

## Testing

<!-- The tests added and what each verifies. For a change to the codec,
the bytes checked against the protocol's page; for authentication or TLS,
the attacks refused. -->

- [ ] `dotnet run tools/verify.cs` passes: restore, format, build with every analyzer, every test, the coverage floor

## Compatibility

<!-- What a program using the driver sees change: its API, its settings,
what it sends a server; "none" if nothing changes. -->

## Performance

<!-- If the change claims any: the measurements before and after, with
the numbers. Otherwise "none claimed". -->

## Checklist (RULES.md §6)

- [ ] One logical change per commit, each passing `dotnet run tools/verify.cs`
- [ ] Sections cited in the code (R-DOC-4) and in the tests (`[Verifies]`, R-TRACE-1)
- [ ] Normal and robustness tests with the exact SQLCODE and SQLSTATE (R-TEST-2), a failing-first test for a fix (R-TEST-6)
- [ ] Every failure a `TurborelException`, every wire length bounded, no secret shown (R-ERR, R-SAFE-1, R-SAFE-4)
- [ ] No `unsafe`, no new dependency without a decision (R-UNSAFE-1, R-DEP-1)
- [ ] CONFORMANCE.md, RULES.md, TODO.md and `doc/` updated where they change
