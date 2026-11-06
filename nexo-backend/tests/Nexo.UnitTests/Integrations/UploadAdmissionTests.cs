using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Nexo.Api.Uploads;
using Nexo.Infrastructure.Modules.Build;
using Xunit;

namespace Nexo.UnitTests.Integrations;

/// <summary>The temp-disk ceiling is arithmetic over configuration: stated here so a change of default shows up as a test to edit.</summary>
public class UploadAdmissionTests
{
    private const long MiB = 1024 * 1024;

    private static UploadAdmission Admission(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        return new UploadAdmission(config, new BuildTwinOptions(config));
    }

    [Fact]
    public void The_default_ceiling_is_24_small_bodies_plus_3_of_the_largest()
    {
        using var admission = Admission();

        admission.LargestBodyBytes.Should().Be(501 * MiB, "the default point cloud limit (500 MB) with its framing");
        admission.TempCeilingBytes.Should().Be((24 * 26 + 3 * 501) * MiB);
        admission.TempCeilingBytes.Should().Be(2127 * MiB);
    }

    [Fact]
    public void The_ceiling_follows_the_configured_limits()
    {
        using var admission = Admission(
            ("Build:Models:MaxUploadMb", "800"),
            ("Build:RealityCapture:MaxPointCloudMb", "100"),
            ("Uploads:InFlight:Small:Global", "10"),
            ("Uploads:InFlight:Large:Global", "2"));

        admission.TempCeilingBytes.Should().Be((10 * 26 + 2 * 801) * MiB);
    }

    [Fact]
    public void Limits_below_one_slot_are_read_as_one()
    {
        using var admission = Admission(("Uploads:InFlight:Large:Global", "0"), ("Uploads:InFlight:Small:PerUser", "-3"));

        admission.Large.Global.Should().Be(1, "zero would refuse every upload of the pool for good");
        admission.Small.PerUser.Should().Be(1);
    }
}
