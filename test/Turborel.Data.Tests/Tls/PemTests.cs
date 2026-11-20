// The platform's authorities read in place of its store from the files
// SSL_CERT_FILE and SSL_CERT_DIR name, as turborel-client reads them
// [CLIENT Connecting].

using System;
using System.IO;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Turborel.Data.Tls;
using Xunit;

namespace Turborel.Data.Tests.Tls;

/// <summary>Tests of <see cref="Pem"/>.</summary>
public sealed class PemTests
{
    /// <summary>A file's authorities, and every file of a directory that holds one, are read; neither named, none.</summary>
    [Fact]
    [Verifies("[CLIENT Connecting]")]
    public void VariablesNameTheAuthorities()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("turborel-cs-pem-");
        try
        {
            var certificates = TestCertificates.Make(directory.FullName);
            string fileVariable = "TURBOREL_CS_TEST_CERT_FILE_" + Guid.NewGuid().ToString("N");
            string directoryVariable = "TURBOREL_CS_TEST_CERT_DIR_" + Guid.NewGuid().ToString("N");
            Assert.Empty(Pem.FromVariables(fileVariable, directoryVariable));
            Environment.SetEnvironmentVariable(fileVariable, certificates.AuthorityFile);
            Environment.SetEnvironmentVariable(directoryVariable, directory.FullName);

            Assert.Equal(1 + 3, Pem.FromVariables(fileVariable, directoryVariable).Count);
            Environment.SetEnvironmentVariable(fileVariable, null);
            Environment.SetEnvironmentVariable(directoryVariable, null);
            Wire.Fails(() => Pem.Certificates(certificates.ServerKeyFile));
            Wire.Fails(() => Pem.Certificates(Path.Combine(directory.FullName, "missing.pem")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
