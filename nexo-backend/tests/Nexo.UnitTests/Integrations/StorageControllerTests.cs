using FluentAssertions;
using System.Security.Claims;
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
        IIntegrationFeatureFlags Flags,
        IPrivateFileUrls  PrivateUrls);

    private static Sut Build(bool storageEnabled = true, int maxFileSizeMb = 5, string role = "Gerente")
    {
        var provider = Substitute.For<IStorageProvider>();
        var privateUrls = Substitute.For<IPrivateFileUrls>();
        privateUrls.For(Arg.Any<string?>()).Returns(c => "https://signed.example/" + c.Arg<string?>());
        var flags    = Substitute.For<IIntegrationFeatureFlags>();
        var tenant   = Substitute.For<ICurrentTenant>();

        flags.StorageEnabled.Returns(storageEnabled);
        tenant.Id.Returns(TenantId);
        tenant.Slug.Returns("test-tenant");

        var opts = Options.Create(new StorageOptions
        {
            MaxFileSizeMb     = maxFileSizeMb,
            AllowedContentTypes = ["image/jpeg", "image/png", "image/webp", "application/pdf"],
        });

        var controller = new StorageController(
            provider,
            privateUrls,
            flags,
            opts,
            tenant,
            NullLogger<StorageController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test")),
                },
            },
        };

        return new Sut(controller, provider, flags, privateUrls);
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
        var bytes = new byte[size > 0 ? (int)size : 0];
        var signature = Signature(contentType);
        if (bytes.Length >= signature.Length) signature.CopyTo(bytes, 0);
        file.OpenReadStream().Returns(_ => new MemoryStream(bytes));
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
    /// <summary>Real first bytes of each type, so the signature check passes for honest files.</summary>
    private static byte[] Signature(string contentType) => contentType switch
    {
        "image/jpeg"      => [0xFF, 0xD8, 0xFF, 0xE0],
        "image/png"       => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
        "image/webp"      => "RIFF    WEBP"u8.ToArray(),
        "application/pdf" => "%PDF-"u8.ToArray(),
        _                 => [],
    };

    // ── Private files (PR 70) ─────────────────────────────────────────────────

    [Fact]
    public async Task A_record_is_stored_private_and_answered_with_a_signed_url_only()
    {
        var sut = Build();
        StorageUploadRequest? captured = null;
        sut.Provider
           .UploadAsync(Arg.Do<StorageUploadRequest>(r => captured = r), Arg.Any<CancellationToken>())
           .Returns(c => new StorageUploadResult(c.Arg<StorageUploadRequest>().ObjectKey, string.Empty));

        var result = await sut.Controller.Upload(MakeFile(contentType: "application/pdf", fileName: "exame.pdf"), "service-record", CancellationToken.None);

        captured!.ObjectKey.Should().StartWith($"private/tenants/{TenantId}/service/records/");
        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        body.GetType().GetProperty("publicUrl").Should().BeNull("a private file never gets a public URL");
        body.GetType().GetProperty("url")!.GetValue(body).Should().Be("https://signed.example/" + captured.ObjectKey);
    }

    [Fact]
    public async Task A_stock_clerk_uploads_product_images_but_not_records_or_site_photos()
    {
        var sut = Build(role: "Estoquista");
        sut.Provider.UploadAsync(Arg.Any<StorageUploadRequest>(), Arg.Any<CancellationToken>())
           .Returns(new StorageUploadResult("k", "https://cdn/k"));

        (await sut.Controller.Upload(MakeFile(), "product-image", CancellationToken.None)).Should().BeOfType<OkObjectResult>();
        (await sut.Controller.Upload(MakeFile(), "service-record", CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await sut.Controller.Upload(MakeFile(), "build-daily-log", CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await sut.Controller.Upload(MakeFile(), "restaurant-logo", CancellationToken.None)).Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task A_file_whose_bytes_do_not_match_its_type_is_refused()
    {
        var sut = Build();
        var zip = Substitute.For<IFormFile>();
        zip.ContentType.Returns("image/jpeg");
        zip.Length.Returns(100);
        zip.FileName.Returns("foto.jpg");
        zip.OpenReadStream().Returns(_ => new MemoryStream([0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0, 0, 0, 0, 0]));

        (await sut.Controller.Upload(zip, "product-image", CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
        await sut.Provider.DidNotReceive().UploadAsync(Arg.Any<StorageUploadRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Signed_urls_are_only_for_this_tenants_private_files()
    {
        var sut = Build();
        sut.Controller.SignedUrl($"private/tenants/{TenantId}/service/records/a.pdf").Should().BeOfType<OkObjectResult>();
        sut.Controller.SignedUrl($"private/tenants/{Guid.NewGuid()}/service/records/a.pdf").Should().BeOfType<ForbidResult>();
        sut.Controller.SignedUrl($"tenants/{TenantId}/products/a.jpg").Should().BeOfType<ForbidResult>("public files have their public URL");
        sut.Controller.SignedUrl($"private/tenants/{TenantId}/../{Guid.NewGuid()}/x.pdf").Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Only_management_deletes_private_files()
    {
        var clerk = Build(role: "Estoquista");
        (await clerk.Controller.Delete($"private/tenants/{TenantId}/build/daily-logs/a.jpg", CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await clerk.Controller.Delete($"tenants/{TenantId}/products/a.jpg", CancellationToken.None)).Should().BeOfType<NoContentResult>();
    }
}
