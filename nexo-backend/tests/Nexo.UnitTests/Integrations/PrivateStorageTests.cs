using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Integrations.Options;
using Nexo.Infrastructure.Integrations.Storage;
using Nexo.Infrastructure.Modules.Build;
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
    [InlineData("PRIVATE/tenants/11111111-1111-1111-1111-111111111111/service/records/a.pdf", false)]   // case is another key
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

    private const string PublicBucket = "orken-assets";
    private const string PrivateBucket = "orken-private";

    private static IOptions<StorageOptions> R2(string privateBucket) => Options.Create(new StorageOptions
    {
        R2 = new R2Options
        {
            AccountId = "acct", AccessKeyId = "test-access", SecretAccessKey = Guid.NewGuid().ToString("N"),
            BucketName = PublicBucket, PublicUrl = "https://assets.example", PrivateBucketName = privateBucket,
        },
    });

    private static CloudflareR2Provider Provider(string privateBucket)
        => new(R2(privateBucket), NullLogger<CloudflareR2Provider>.Instance);

    /// <summary>The provider over a fake S3 client, to see which bucket every call goes to.</summary>
    private static (CloudflareR2Provider Provider, IAmazonS3 S3) Recording(string privateBucket)
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>()).Returns(new PutObjectResponse());
        return (new CloudflareR2Provider(R2(privateBucket), NullLogger<CloudflareR2Provider>.Instance, () => s3), s3);
    }

    private static StorageUploadRequest Upload(string key)
        => new(new MemoryStream([1, 2, 3]), "a.bin", "application/octet-stream", key, 3);

    private static string PrivateKey(string folder = "build/models")
        => StorageKeys.Build(Tenant, folder, "source.ifc", isPrivate: true);

    [Fact]
    public void Private_files_are_signed_against_the_private_bucket_and_expire()
    {
        var url = Provider("orken-private").GetSignedUrl($"private/tenants/{Tenant}/service/records/a.pdf", TimeSpan.FromMinutes(15));

        url.Should().StartWith("https://").And.Contain("orken-private").And.NotContain("orken-assets");
        url.Should().Contain("X-Amz-Signature=").And.Contain("X-Amz-Expires=");
        url.Should().NotContain("assets.example", "never the public base URL");
    }

    // ── Private content never falls back to the public bucket ─────────────────────────────────────────────────

    [Fact]
    public async Task With_a_private_bucket_private_files_are_written_only_there()
    {
        var (provider, s3) = Recording(PrivateBucket);

        var result = await provider.UploadAsync(Upload(PrivateKey()));

        provider.CanStorePrivateFiles.Should().BeTrue();
        await s3.Received(1).PutObjectAsync(Arg.Is<PutObjectRequest>(r => r.BucketName == PrivateBucket), Arg.Any<CancellationToken>());
        await s3.DidNotReceive().PutObjectAsync(Arg.Is<PutObjectRequest>(r => r.BucketName == PublicBucket), Arg.Any<CancellationToken>());
        result.PublicUrl.Should().BeEmpty("a private file never gets a public URL");
    }

    /// <summary>Missing, blank or the public bucket under the private name: all mean "no private place".</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(PublicBucket)]
    [InlineData(" ORKEN-ASSETS ")]
    public async Task Without_a_valid_private_bucket_every_private_operation_fails_closed_before_any_request(string privateBucket)
    {
        var (provider, s3) = Recording(privateBucket);
        var key = PrivateKey();

        provider.CanStorePrivateFiles.Should().BeFalse();
        await provider.Invoking(p => p.UploadAsync(Upload(key))).Should().ThrowAsync<PrivateStorageUnavailableException>();
        await provider.Invoking(p => p.OpenReadAsync(key)).Should().ThrowAsync<PrivateStorageUnavailableException>();
        await provider.Invoking(p => p.DeleteAsync(key)).Should().ThrowAsync<PrivateStorageUnavailableException>();
        provider.Invoking(p => p.GetSignedUrl(key, TimeSpan.FromMinutes(15))).Should().Throw<PrivateStorageUnavailableException>();
        s3.ReceivedCalls().Should().BeEmpty("nothing is sent anywhere, above all not to the public bucket");
    }

    /// <summary>Every private place the Build writes to (and records) is refused the same way.</summary>
    [Theory]
    [InlineData(BuildModelService.Folder)]
    [InlineData(BuildDocumentService.Folder)]
    [InlineData(BuildRealityCaptureService.Folder)]
    [InlineData(BuildPostConstructionService.AssistanceFolder)]
    [InlineData(BuildPostConstructionService.HandoverFolder)]
    [InlineData(BuildQualityService.PhotoFolder)]
    [InlineData(BuildSelectionService.ImageFolder)]
    [InlineData("build/daily-logs")]
    [InlineData("service/records")]
    public async Task Every_private_context_is_refused_without_a_private_bucket(string folder)
    {
        var (provider, s3) = Recording("");

        await provider.Invoking(p => p.UploadAsync(Upload(PrivateKey(folder)))).Should().ThrowAsync<PrivateStorageUnavailableException>();
        s3.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task An_unavailable_private_bucket_fails_closed_without_trying_the_public_one()
    {
        var (provider, s3) = Recording(PrivateBucket);
        s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
          .ThrowsAsync(new AmazonS3Exception("The specified bucket does not exist"));

        await provider.Invoking(p => p.UploadAsync(Upload(PrivateKey()))).Should().ThrowAsync<AmazonS3Exception>();

        await s3.Received(1).PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>());
        await s3.DidNotReceive().PutObjectAsync(Arg.Is<PutObjectRequest>(r => r.BucketName == PublicBucket), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Public_files_keep_using_the_public_bucket_with_or_without_a_private_one()
    {
        var (provider, s3) = Recording("");
        var key = StorageKeys.Build(Tenant, "build/portal/logo", "logo.png", isPrivate: false);

        var result = await provider.UploadAsync(Upload(key));

        await s3.Received(1).PutObjectAsync(Arg.Is<PutObjectRequest>(r => r.BucketName == PublicBucket), Arg.Any<CancellationToken>());
        result.PublicUrl.Should().Be($"https://assets.example/{key}");
    }

    [Fact]
    public void Without_a_private_bucket_a_private_file_gets_no_signed_url_at_all()
    {
        var flags = Substitute.For<IIntegrationFeatureFlags>();
        flags.StorageEnabled.Returns(true);
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns(Tenant);

        new PrivateFileUrls(Provider(privateBucket: ""), flags, tenant, NullLogger<PrivateFileUrls>.Instance)
            .For(PrivateKey("build/daily-logs")).Should().BeNull("the file is unavailable, never a URL of the public bucket");
        new PrivateFileUrls(Provider(PrivateBucket), flags, tenant, NullLogger<PrivateFileUrls>.Instance)
            .For(PrivateKey("build/daily-logs")).Should().Contain(PrivateBucket).And.NotContain(PublicBucket);
    }

    [Theory]
    [InlineData("private/tenants/11111111-1111-1111-1111-111111111111/build/models/m/v/source.ifc")]
    [InlineData("/private/tenants/11111111-1111-1111-1111-111111111111/build/assistance/a.jpg")]
    public void A_private_key_is_never_served_through_the_public_url(string key)
        => new StoragePublicUrlResolver(R2(PrivateBucket)).ResolvePublicUrl(key).Should().BeNull();

    [Fact]
    public void A_public_key_still_resolves_to_the_public_url()
        => new StoragePublicUrlResolver(R2(""))
            .ResolvePublicUrl($"tenants/{Tenant}/build/portal/logo/a.png")
            .Should().Be($"https://assets.example/tenants/{Tenant}/build/portal/logo/a.png");
}
