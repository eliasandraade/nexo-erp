namespace Nexo.IntegrationTests.Helpers;

/// <summary>
/// Shares a single TestWebApplicationFactory (and its PostgreSQL container) across all
/// integration test classes in the "Integration" collection.
///
/// xUnit creates ONE instance of TestWebApplicationFactory for the entire collection,
/// calls InitializeAsync once, and injects it into every test class constructor.
/// This prevents Serilog's ReloadableLogger from being frozen more than once per process.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<TestWebApplicationFactory> { }
