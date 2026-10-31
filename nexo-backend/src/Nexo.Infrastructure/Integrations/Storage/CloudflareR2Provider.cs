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
    private readonly Lazy<IAmazonS3> _s3;
    private readonly StorageOptions  _opts;
    private readonly ILogger<CloudflareR2Provider> _logger;

    public CloudflareR2Provider(IOptions<StorageOptions> opts, ILogger<CloudflareR2Provider> logger)
        : this(opts, logger, null) { }

    /// <summary>Tests hand in their own S3 client to see which bucket each call goes to.</summary>
    internal CloudflareR2Provider(IOptions<StorageOptions> opts, ILogger<CloudflareR2Provider> logger, Func<IAmazonS3>? client)
    {
        _opts   = opts.Value;
        _logger = logger;

        // Lazy: the S3 client is created only on first use, never in the constructor.
        // With empty/invalid R2 config (e.g. StorageEnabled=false in an environment that
        // never set credentials) the AWS SDK can throw while building the client. Doing it
        // in the constructor made the DI-injected singleton fail, which 500'd the
        // StorageController BEFORE its StorageEnabled feature-flag gate could return 404.
        // Deferring construction keeps the controller constructable, so a disabled/misconfigured
        // store yields a controlled response (404 when off, 503 when on-but-unreachable).
        _s3 = new Lazy<IAmazonS3>(client ?? CreateClient);
    }

    private IAmazonS3 CreateClient()
    {
        var config = new AmazonS3Config
        {
            ServiceURL           = $"https://{_opts.R2.AccountId}.r2.cloudflarestorage.com",
            ForcePathStyle       = true,
            AuthenticationRegion = "auto",
        };
        var credentials = new BasicAWSCredentials(_opts.R2.AccessKeyId, _opts.R2.SecretAccessKey);
        return new AmazonS3Client(credentials, config);
    }

    public async Task<StorageUploadResult> UploadAsync(StorageUploadRequest request, CancellationToken ct = default)
    {
        // resolved before anything is sent: a private key without a private bucket stops here, nothing is written
        var bucket = BucketFor(request.ObjectKey);
        _logger.LogInformation("[Storage] Uploading {Key} ({ContentType}, {Length}B)",
            request.ObjectKey, request.ContentType, request.ContentLength);

        var putRequest = new PutObjectRequest
        {
            BucketName            = bucket,
            Key                   = request.ObjectKey,
            InputStream           = request.Content,
            ContentType           = request.ContentType,
            DisablePayloadSigning = true,
            UseChunkEncoding      = false,
        };

        await _s3.Value.PutObjectAsync(putRequest, ct);

        // private files never get a public URL — only signed ones, on read
        var publicUrl = StorageKeys.IsPrivate(request.ObjectKey)
            ? string.Empty
            : $"{_opts.R2.PublicUrl.TrimEnd('/')}/{request.ObjectKey}";

        _logger.LogInformation("[Storage] Uploaded {Key} → {PublicUrl}", request.ObjectKey, publicUrl);

        return new StorageUploadResult(request.ObjectKey, publicUrl);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var bucket = BucketFor(key);
        _logger.LogInformation("[Storage] Deleting {Key}", key);
        try
        {
            await _s3.Value.DeleteObjectAsync(bucket, key, ct);
            _logger.LogInformation("[Storage] Deleted {Key}", key);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("[Storage] Delete: key not found {Key}", key);
        }
    }

    public string GetSignedUrl(string key, TimeSpan ttl)
    {
        var bucket = BucketFor(key);
        return _s3.Value.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key        = key,
            Verb       = HttpVerb.GET,
            Expires    = DateTime.UtcNow.Add(ttl),
            Protocol   = Protocol.HTTPS,
        });
    }

    /// <summary>Streams the object (the S3 response stream; disposing it releases the connection).</summary>
    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var bucket = BucketFor(key);
        var response = await _s3.Value.GetObjectAsync(bucket, key, ct);
        return response.ResponseStream;
    }

    public bool CanStorePrivateFiles => HasPrivateBucket(_opts.R2);

    /// <summary>A private bucket that is set and is not the public one (a public bucket under another name is still public).</summary>
    internal static bool HasPrivateBucket(R2Options r2)
        => !string.IsNullOrWhiteSpace(r2.PrivateBucketName)
           && !string.Equals(r2.PrivateBucketName.Trim(), r2.BucketName?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The only bucket a key may touch. Private keys go to the private bucket or nowhere: without one, every operation
    /// on them throws <see cref="PrivateStorageUnavailableException"/> before any request — never the public bucket.
    /// </summary>
    private string BucketFor(string key)
    {
        if (!StorageKeys.IsPrivate(key)) return _opts.R2.BucketName;
        if (!CanStorePrivateFiles) throw new PrivateStorageUnavailableException();
        return _opts.R2.PrivateBucketName.Trim();
    }

    /// <summary>
    /// Lists at most one object of the app's bucket — what its token is allowed to do. (Listing all
    /// buckets needs an account-wide token the app does not have, so it always failed.)
    /// </summary>
    public async Task PingAsync(CancellationToken ct = default)
    {
        await _s3.Value.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _opts.R2.BucketName, MaxKeys = 1 }, ct);
    }

    /// <summary>The same read-only listing against the private bucket; without one it throws before any request.</summary>
    public async Task PingPrivateAsync(CancellationToken ct = default)
    {
        if (!CanStorePrivateFiles) throw new PrivateStorageUnavailableException();
        await _s3.Value.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _opts.R2.PrivateBucketName.Trim(), MaxKeys = 1 }, ct);
    }
}
