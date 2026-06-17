using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Integrations.Options;
using Nexo.Api.Controllers.Integrations;
using Xunit;

namespace Nexo.UnitTests.Integrations;

public class StorageControllerTests
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed record Sut(
        StorageController Controller,
        IStorageProvider  Provider,
        IIntegrationFeatureFlags Flags);

    private static Sut Build(bool storageEnabled = true, int maxFileSizeMb = 5)
    {
        var provider = Substitute.For<IStorageProvider>();
        var flags    = Substitute.For<IIntegrationFeatureFlags>();
        var tenant   = Substitute.For<ICurrentTenant>();

        flags.StorageEnabled.Returns(storageEnabled);
        tenant.Id.Returns(TenantId);
        tenant.Slug.Returns("test-tenant");

        var opts = Options.Create(new StorageOptions
        {
            MaxFileSizeMb     = maxFileSizeMb,
            AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"],
        });

        var controller = new StorageController(
            provider,
            flags,
            opts,
            tenant,
            NullLogger<StorageController>.Instance);

        return new Sut(controller, provider, flags);
    }

    private static IFormFile MakeFile(
        string contentType = "image/jpeg",
        long   size        = 1024,
        string fileName    = "photo.jpg")
    {
        var file = Substitute.For<IFormFile>();
        file.ContentType.Returns(contentType);
        file.Length.Returns(size);
        file.FileName.Returns(fileName);
        file.OpenReadStream().Returns(new MemoryStream(new byte[size > 0 ? (int)size : 0]));
        return file;
    }

    // ── Upload tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_StorageDisabled_ReturnsNotFound()
    {
        var sut  = Build(storageEnabled: false);
        var file = MakeFile();

        var result = await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
        await sut.Provider.DidNotReceive().UploadAsync(Arg.Any<StorageUploadRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_StorageDisabled_ReturnsNotFound()
    {
        var sut = Build(storageEnabled: false);
        var key = $"tenants/{TenantId}/products/somefile.jpg";

        var result = await sut.Controller.Delete(key, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
        await sut.Provider.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_InvalidContext_ReturnsBadRequest()
    {
        var sut  = Build();
        var file = MakeFile();

        var result = await sut.Controller.Upload(file, "invalid-context", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Upload_DisallowedContentType_ReturnsBadRequest()
    {
        var sut  = Build();
        var file = MakeFile(contentType: "application/zip", fileName: "archive.zip");

        var result = await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Upload_FileTooLarge_ReturnsBadRequest()
    {
        var sut  = Build(maxFileSizeMb: 1);
        // 2 MB — exceeds the 1 MB limit
        var file = MakeFile(size: 2 * 1024 * 1024);

        var result = await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Upload_EmptyFile_ReturnsBadRequest()
    {
        var sut  = Build();
        var file = MakeFile(size: 0);

        var result = await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Upload_ValidFile_ReturnsKeyAndPublicUrl()
    {
        var sut  = Build();
        var file = MakeFile();

        sut.Provider
           .UploadAsync(Arg.Any<StorageUploadRequest>(), Arg.Any<CancellationToken>())
           .Returns(new StorageUploadResult("tenants/test/products/abc.jpg", "https://cdn.example.com/abc.jpg"));

        var result = await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new
        {
            key       = "tenants/test/products/abc.jpg",
            publicUrl = "https://cdn.example.com/abc.jpg",
        });
    }

    [Fact]
    public async Task Upload_ProviderThrows_Returns503()
    {
        var sut  = Build();
        var file = MakeFile();

        sut.Provider
           .UploadAsync(Arg.Any<StorageUploadRequest>(), Arg.Any<CancellationToken>())
           .ThrowsAsync(new Exception("Storage unavailable"));

        var result = await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task Upload_ObjectKey_ContainsTenantIdAndContextPath()
    {
        var sut      = Build();
        var fileName = "my-original-photo.jpg";
        var file     = MakeFile(fileName: fileName);

        StorageUploadRequest? captured = null;
        sut.Provider
           .UploadAsync(Arg.Do<StorageUploadRequest>(r => captured = r), Arg.Any<CancellationToken>())
           .Returns(new StorageUploadResult("key", "https://cdn.example.com/key"));

        await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ObjectKey.Should().Contain(TenantId.ToString());
        captured.ObjectKey.Should().Contain("products");
        // Original filename must NOT appear in the generated key
        captured.ObjectKey.Should().NotContain(fileName);
    }

    [Fact]
    public async Task Upload_BuildDailyLogContext_KeyContainsBuildPath()
    {
        var sut  = Build();
        var file = MakeFile();

        StorageUploadRequest? captured = null;
        sut.Provider
           .UploadAsync(Arg.Do<StorageUploadRequest>(r => captured = r), Arg.Any<CancellationToken>())
           .Returns(new StorageUploadResult("key", "https://cdn.example.com/key"));

        var result = await sut.Controller.Upload(file, "build-daily-log", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        captured.Should().NotBeNull();
        captured!.ObjectKey.Should().Contain(TenantId.ToString());
        captured.ObjectKey.Should().Contain("build/daily-logs");
    }

    [Fact]
    public async Task Upload_ServiceRecordContext_KeyContainsServiceRecordsPath()
    {
        var sut  = Build();
        var file = MakeFile();

        StorageUploadRequest? captured = null;
        sut.Provider
           .UploadAsync(Arg.Do<StorageUploadRequest>(r => captured = r), Arg.Any<CancellationToken>())
           .Returns(new StorageUploadResult("key", "https://cdn.example.com/key"));

        var result = await sut.Controller.Upload(file, "service-record", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        captured.Should().NotBeNull();
        captured!.ObjectKey.Should().Contain(TenantId.ToString());
        captured.ObjectKey.Should().Contain("service/records");
    }

    [Fact]
    public async Task Upload_ProviderReceivesCorrectContentType()
    {
        var sut  = Build();
        var file = MakeFile(contentType: "image/webp", fileName: "photo.webp");

        StorageUploadRequest? captured = null;
        sut.Provider
           .UploadAsync(Arg.Do<StorageUploadRequest>(r => captured = r), Arg.Any<CancellationToken>())
           .Returns(new StorageUploadResult("key", "https://cdn.example.com/key"));

        await sut.Controller.Upload(file, "product-image", CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ContentType.Should().Be("image/webp");
        captured.ObjectKey.Should().EndWith(".webp");
    }

    // ── Delete tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_KeyFromOtherTenant_ReturnsForbid()
    {
        var sut = Build();
        var key = "tenants/other-tenant-id/products/file.jpg";

        var result = await sut.Controller.Delete(key, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        await sut.Provider.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_ValidKey_ReturnsNoContent()
    {
        var sut = Build();
        var key = $"tenants/{TenantId}/products/file.jpg";

        var result = await sut.Controller.Delete(key, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        await sut.Provider.Received(1).DeleteAsync(key, Arg.Any<CancellationToken>());
    }
}
