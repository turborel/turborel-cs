// The standard API's provider factory (RULES.md D2): what ADO.NET's
// tools and frameworks make the driver's objects by, registered under the
// driver's invariant name, Turborel.Data, by the program, as
// DbProviderFactories.RegisterFactory takes it.

using System.Data.Common;

namespace Turborel.Data;

/// <summary>Makes the driver's ADO.NET objects.</summary>
public sealed class TurborelFactory : DbProviderFactory
{
    /// <summary>The name a program registers the factory by.</summary>
    public const string InvariantName = "Turborel.Data";

    private TurborelFactory()
    {
    }

    /// <summary>Gets the one factory.</summary>
    public static TurborelFactory Instance { get; } = new();

    /// <inheritdoc/>
    public override bool CanCreateBatch => true;

    /// <inheritdoc/>
    public override bool CanCreateDataSourceEnumerator => false;

    /// <inheritdoc/>
    public override DbConnection CreateConnection() => new TurborelConnection();

    /// <inheritdoc/>
    public override DbCommand CreateCommand() => new TurborelCommand();

    /// <inheritdoc/>
    public override DbParameter CreateParameter() => new TurborelParameter();

    /// <inheritdoc/>
    public override DbConnectionStringBuilder CreateConnectionStringBuilder() => new TurborelConnectionStringBuilder();

    /// <inheritdoc/>
    public override DbBatch CreateBatch() => new TurborelBatch();

    /// <inheritdoc/>
    public override DbBatchCommand CreateBatchCommand() => new TurborelBatchCommand();

    /// <inheritdoc/>
    public override DbDataSource CreateDataSource(string connectionString) => new TurborelDataSource(connectionString);
}
