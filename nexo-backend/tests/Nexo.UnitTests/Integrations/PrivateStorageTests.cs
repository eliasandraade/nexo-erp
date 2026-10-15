using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.Options;
using Nexo.Infrastructure.Integrations.Storage;
using Xunit;

namespace Nexo.UnitTests.Integrations;

/// <summary>PR 70 — private keys, tenant ownership and signed URLs.</summary>
public class PrivateStorageTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other  = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Private_keys_live_under_private_and_the_tenant()
    {
        StorageKeys.Build(Tenant, "service/records", "a.pdf", isPrivate: true)
            .Should().Be($"private/tenants/{Tenant}/service/records/a.pdf");
        StorageKeys.Build(Tenant, "products", "a.jpg", isPrivate: false)
            .Should().Be($"tenants/{Tenant}/products/a.jpg");
    }

    [Theory]
    [InlineData("private/tenants/11111111-1111-1111-1111-111111111111/service/records/a.pdf", true)]
    [InlineData("private/tenants/22222222-2222-2222-2222-222222222222/service/records/a.pdf", false)]   // other tenant
    [InlineData("tenants/11111111-1111-1111-1111-111111111111/service/records/a.pdf", false)]           // public folder
    [InlineData("private/tenants/11111111-1111-1111-1111-111111111111/build/daily-logs/a.jpg", false)] // other place
    [InlineData("private/tenants/11111111-1111-1111-1111-111111111111/service/records/", false)]        // no file
    [InlineData("private/tenants/11111111-1111-1111-1111-111111111111/service/records/../../x", false)] // traversal
    [InlineData("https://evil.example/a.pdf", false)]
    [InlineData(null, false)]
    public void Only_this_tenants_record_folder_is_accepted_for_records(string? key, bool ok)
        => StorageKeys.IsPrivateOf(key, Tenant, "service/records").Should().Be(ok);

    [Fact]
    public void A_tenant_owns_its_public_and_private_folders_only()
    {
        StorageKeys.IsOfTenant($"tenants/{Tenant}/products/a.jpg", Tenant).Should().BeTrue();
        StorageKeys.IsOfTenant($"private/tenants/{Tenant}/service/records/a.pdf", Tenant).Should().BeTrue();
        StorageKeys.IsOfTenant($"tenants/{Other}/products/a.jpg", Tenant).Should().BeFalse();
        StorageKeys.IsOfTenant($"private/tenants/{Tenant}/../{Other}/a.jpg", Tenant).Should().BeFalse();
    }

    private static CloudflareR2Provider Provider(string privateBucket) => new(
        Options.Create(new StorageOptions
        {
            R2 = new R2Options
            {
                AccountId = "acct", AccessKeyId = "test-access", SecretAccessKey = Guid.NewGuid().ToString("N"),
                BucketName = "orken-assets", PublicUrl = "https://assets.example", PrivateBucketName = privateBucket,
            },
        }),
        NullLogger<CloudflareR2Provider>.Instance);

    [Fact]
    public void Private_files_are_signed_against_the_private_bucket_and_expire()
    {
        var url = Provider("orken-private").GetSignedUrl($"private/tenants/{Tenant}/service/records/a.pdf", TimeSpan.FromMinutes(15));

        url.Should().StartWith("https://").And.Contain("orken-private").And.NotContain("orken-assets");
        url.Should().Contain("X-Amz-Signature=").And.Contain("X-Amz-Expires=");
        url.Should().NotContain("assets.example", "never the public base URL");
    }

    [Fact]
    public void Without_a_private_bucket_private_files_stay_in_the_main_bucket_but_still_signed()
    {
        var url = Provider(privateBucket: "").GetSignedUrl($"private/tenants/{Tenant}/build/daily-logs/a.jpg", TimeSpan.FromMinutes(15));
        url.Should().Contain("orken-assets").And.Contain("X-Amz-Signature=");
    }
}
