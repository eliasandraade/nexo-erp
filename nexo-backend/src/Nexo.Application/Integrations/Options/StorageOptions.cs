namespace Nexo.Application.Integrations.Options;

public sealed class StorageOptions
{
    public const string SectionKey = "Integrations:Storage";

    public string Provider { get; init; } = "R2";

    /// <summary>Maximum allowed upload size in MB.</summary>
    public int MaxFileSizeMb { get; init; } = 10;

    /// <summary>Allowed MIME types for upload.</summary>
    public string[] AllowedContentTypes { get; init; } =
        ["image/jpeg", "image/png", "image/webp"];

    public R2Options R2 { get; init; } = new();
}

public sealed class R2Options
{
    public string AccountId       { get; init; } = string.Empty;
    public string AccessKeyId     { get; init; } = string.Empty;
    public string SecretAccessKey { get; init; } = string.Empty;
    public string BucketName      { get; init; } = "orken-assets";
    /// <summary>Public base URL, e.g. https://assets.orken.com.br</summary>
    public string PublicUrl       { get; init; } = string.Empty;
    /// <summary>
    /// Bucket WITHOUT public access for private files (records, site photos). Empty = they go to
    /// <see cref="BucketName"/> under "private/" and are only handed out signed — but a public
    /// bucket still serves any key it is asked for, so production should set this.
    /// </summary>
    public string PrivateBucketName { get; init; } = string.Empty;
}
