// The one command that runs every check a change must pass (RULES.md D4):
// the locked restore, the format, the build with every analyzer, and every
// test with the coverage floor (D7). Run as `dotnet run tools/verify.cs`,
// by the pre-commit hook and by CI; it stops at the first failure.
//
// The tool is no package: it restores nothing, so it keeps no lock file,
// and it runs as it is built, never compiled ahead of time.
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System;
using System.Collections.Generic;
using System.Diagnostics;

const string Solution = "Turborel.Data.slnx";
const string Configuration = "Release";

// The coverage floor, in percent of lines and of branches (RULES.md D7, D12).
const string CoverageFloor = "90";

// Passed as DOTNET_ROLL_FORWARD to the tests alone, as CI runs them on a
// later runtime than the one they target (RULES.md D3).
const string RollForwardOption = "--roll-forward";

string? rollForward = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == RollForwardOption && i + 1 < args.Length)
    {
        rollForward = args[++i];
    }
    else
    {
        Console.Error.WriteLine($"verify: unknown argument {args[i]}; the one option is {RollForwardOption} <policy>");
        return 2;
    }
}

(string Name, string[] Arguments, string? RollForward)[] steps =
[
    ("restore", ["restore", Solution, "--locked-mode"], null),
    ("format", ["format", Solution, "--verify-no-changes", "--no-restore"], null),
    ("build", ["build", Solution, "--no-restore", "-c", Configuration], null),
    ("test", ["test", "--solution", Solution, "--no-build", "-c", Configuration, "--coverlet",
        "--coverlet-threshold", CoverageFloor, "--coverlet-threshold-type", "line,branch",
        "--results-directory", "TestResults"], rollForward),
];

foreach ((string name, string[] arguments, string? policy) in steps)
{
    Console.WriteLine($"verify: {name}");
    int code = Run(arguments, policy);
    if (code != 0)
    {
        Console.Error.WriteLine($"verify: {name} failed with exit code {code}");
        return code;
    }
}

Console.WriteLine("verify: every check passed");
return 0;

static int Run(IEnumerable<string> arguments, string? rollForward)
{
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    if (rollForward is not null)
    {
        start.Environment["DOTNET_ROLL_FORWARD"] = rollForward;
    }

    using Process process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
    process.WaitForExit();
    return process.ExitCode;
}
