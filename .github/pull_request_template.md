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

- [ ] `dotnet format Turborel.Data.slnx --verify-no-changes` passes
- [ ] `dotnet build` passes, every analyzer an error
- [ ] `dotnet test --solution Turborel.Data.slnx --coverlet --coverlet-threshold 90 --coverlet-threshold-type line,branch` passes

## Compatibility

<!-- What a program using the driver sees change: its API, its settings,
what it sends a server; "none" if nothing changes. -->

## Performance

<!-- If the change claims any: the measurements before and after, with
the numbers. Otherwise "none claimed". -->

## Checklist (RULES.md §7)

- [ ] One logical change per commit, each passing every check
- [ ] Sections cited in the code (R-DOC-4) and in the tests (`[Verifies]`, R-TRACE-1)
- [ ] Normal and robustness tests with the exact SQLCODE and SQLSTATE (R-TEST-2), a failing-first test for a fix (R-TEST-5)
- [ ] Every failure a `TurborelException`, every wire length bounded, no secret shown (R-ERR, R-SAFE-3, R-SAFE-6)
- [ ] No `unsafe`, no new dependency without a decision (R-SAFE-1, R-SAFE-2)
- [ ] CONFORMANCE.md, RULES.md, TODO.md and `doc/` updated where they change
