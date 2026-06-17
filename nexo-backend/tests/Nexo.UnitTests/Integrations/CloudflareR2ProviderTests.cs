using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexo.Application.Integrations.Options;
using Nexo.Infrastructure.Integrations.Storage;
using Xunit;

namespace Nexo.UnitTests.Integrations;

public class CloudflareR2ProviderTests
{
    /// <summary>
    /// Regression: the provider must construct even with empty/invalid R2 config.
    /// Previously the AWS S3 client was built in the constructor, so the DI-injected
    /// singleton threw when R2 was unconfigured (StorageEnabled=false). That made the
    /// StorageController un-instantiable and 500'd uploads BEFORE the feature-flag gate
    /// could return a controlled 404. Construction must be lazy.
    /// </summary>
    [Fact]
    public void Constructor_WithEmptyR2Config_DoesNotThrow()
    {
        var opts = Options.Create(new StorageOptions()); // empty R2 — no AccountId/keys

        var act = () => new CloudflareR2Provider(opts, NullLogger<CloudflareR2Provider>.Instance);

        act.Should().NotThrow();
    }
}
