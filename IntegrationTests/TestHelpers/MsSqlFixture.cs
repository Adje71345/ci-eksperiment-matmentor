using IntegrationTests.TestHelpers;
using Testcontainers.MsSql;
using Testcontainers.Xunit;
using Xunit.Sdk;

[assembly: AssemblyFixture(typeof(MsSqlFixture))]

namespace IntegrationTests.TestHelpers;

// One SQL Server container for the whole test run. Each test class gets its own database in it (see MatMentorApiFactory)
public sealed class MsSqlFixture(IMessageSink messageSink)
    : ContainerFixture<MsSqlBuilder, MsSqlContainer>(messageSink)
{
    // Same image tag as compose.yaml, so the tests run against the same SQL Server version as development
    protected override MsSqlBuilder Configure()
        => new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04");
}