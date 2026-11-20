// One live server for the whole test run (RULES.md R-TEST-7), shared by
// every live test as an xUnit assembly fixture.

using Turborel.Data.Tests.Server;
using Xunit;

[assembly: AssemblyFixture(typeof(LiveServer))]
