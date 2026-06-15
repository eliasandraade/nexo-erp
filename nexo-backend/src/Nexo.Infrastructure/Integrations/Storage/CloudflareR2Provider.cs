using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Integrations.Options;

namespace Nexo.Infrastructure.Integrations.Storage;

public sealed class CloudflareR2Provider : IStorageProvider
{
    private readonly AmazonS3Client  _s3;
    private readonly StorageOptions  _opts;
    private readonly ILogger<CloudflareR2Provider> _logger;

    public CloudflareR2Provider(IOptions<StorageOptions> opts, ILogger<CloudflareR2Provider> logger)
    {
        _opts   = opts.Value;
        _logger = logger;

        var config = new AmazonS3Config
        {
            ServiceURL           = $"https://{_opts.R2.AccountId}.r2.cloudflarestorage.com",
            ForcePathStyle       = true,
            AuthenticationRegion = "auto",
        };
        var credentials = new BasicAWSCredentials(_opts.R2.AccessKeyId, _opts.R2.SecretAccessKey);
        _s3 = new AmazonS3Client(credentials, config);
    }

    public async Task<StorageUploadResult> UploadAsync(StorageUploadRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("[Storage] Uploading {Key} ({ContentType}, {Length}B)",
            request.ObjectKey, request.ContentType, request.ContentLength);

        var putRequest = new PutObjectRequest
        {
            BucketName            = _opts.R2.BucketName,
            Key                   = request.ObjectKey,
            InputStream           = request.Content,
            ContentType           = request.ContentType,
            DisablePayloadSigning = true,
            UseChunkEncoding      = false,
        };

        await _s3.PutObjectAsync(putRequest, ct);

        var publicUrl = $"{_opts.R2.PublicUrl.TrimEnd('/')}/{request.ObjectKey}";

        _logger.LogInformation("[Storage] Uploaded {Key} → {PublicUrl}", request.ObjectKey, publicUrl);

        return new StorageUploadResult(request.ObjectKey, publicUrl);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        _logger.LogInformation("[Storage] Deleting {Key}", key);
        try
        {
            await _s3.DeleteObjectAsync(_opts.R2.BucketName, key, ct);
            _logger.LogInformation("[Storage] Deleted {Key}", key);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("[Storage] Delete: key not found {Key}", key);
        }
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        await _s3.ListBucketsAsync(ct);
    }
}
