using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace IntegrationTests.TestHelpers;

// Starts the real API in memory against its own database in the shared container.
// Used as a class fixture, so each test class gets one API and one database
public sealed class MatMentorApiFactory(MsSqlFixture sqlServer) : WebApplicationFactory<Program>
{
    // HMAC-SHA256 needs a key of at least 32 bytes
    private const string JwtSecretKey = "integration-tests-secret-key-at-least-32-bytes";

    // A new database name for each factory, so test classes running in parallel do not share data
    private readonly string _connectionString = new SqlConnectionStringBuilder(sqlServer.Container.GetConnectionString())
    {
        InitialCatalog = $"MatMentor_{Guid.NewGuid():N}"
    }.ConnectionString;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // "Testing", so user secrets and the development-only endpoints are not loaded
        builder.UseEnvironment("Testing");

        // Host configuration is in place before Program.cs reads JwtSettings:SecretKey
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = _connectionString,
            ["JwtSettings:SecretKey"] = JwtSecretKey
        }));

        return base.CreateHost(builder);
    }
}