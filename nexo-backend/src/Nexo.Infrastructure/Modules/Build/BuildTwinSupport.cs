using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>Wave 4 settings (appsettings / environment, section <c>Build</c>). Every value has a safe default.</summary>
public sealed class BuildTwinOptions
{
    private readonly IConfiguration _config;

    public BuildTwinOptions(IConfiguration config) => _config = config;

    public const int DefaultMaxUploadMb = 150;

    /// <summary>Largest model file accepted (Build:Models:MaxUploadMb, default 150).</summary>
    public long MaxModelBytes => Mb(_config.GetValue("Build:Models:MaxUploadMb", DefaultMaxUploadMb), 1, 2048);
    public int MaxElements => Math.Clamp(_config.GetValue("Build:Models:MaxElements", 200_000), 100, 2_000_000);
    public double ViewerMatchMinRatio => Math.Clamp(_config.GetValue("Build:Models:ViewerMatchMinRatio", BuildViewerMatch.DefaultMinRatio), 0.05, 1.0);
    /// <summary>Path of the IfcConvert binary (IfcOpenShell). Empty = no server conversion.</summary>
    public string? IfcConvertPath => _config["Build:Models:IfcConvertPath"] is { Length: > 0 } p ? p : null;
    public TimeSpan IfcConvertTimeout => TimeSpan.FromSeconds(Math.Clamp(_config.GetValue("Build:Models:IfcConvertTimeoutSeconds", 300), 10, 3600));
    public TimeSpan JobTimeout => TimeSpan.FromMinutes(Math.Clamp(_config.GetValue("Build:Models:JobTimeoutMinutes", 15), 1, 180));
    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Clamp(_config.GetValue("Build:Models:PollSeconds", 10), 1, 600));

    public long MaxImageBytes => Mb(_config.GetValue("Build:RealityCapture:MaxImageMb", 25), 1, 200);
    public long MaxVideoBytes => Mb(_config.GetValue("Build:RealityCapture:MaxVideoMb", 200), 1, 4096);
    public long MaxPointCloudBytes => Mb(_config.GetValue("Build:RealityCapture:MaxPointCloudMb", 500), 1, 8192);
    public long MaxCaptureBytes => Math.Max(MaxImageBytes, Math.Max(MaxVideoBytes, MaxPointCloudBytes));

    public long MaxCaptureBytesFor(BuildCaptureFamily family) => family switch
    {
        BuildCaptureFamily.Image => MaxImageBytes,
        BuildCaptureFamily.Video => MaxVideoBytes,
        _ => MaxPointCloudBytes,
    };

    public bool IfcConverterAvailable => IfcConvertPath is { } p && File.Exists(p);

    private static long Mb(int value, int min, int max) => Math.Clamp(value, min, max) * 1024L * 1024L;
}

/// <summary>Storage is off or failed: answered as 503 storage_unavailable by the controllers.</summary>
public sealed class BuildStorageUnavailableException : Exception
{
    public BuildStorageUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>A file too large for its kind: answered as 413 file_too_large by the controllers.</summary>
public sealed class BuildFileTooLargeException : Exception
{
    public BuildFileTooLargeException(string message) : base(message) { }
}

/// <summary>
/// An upload copied to a temporary file (deleted on dispose) while its SHA-256 is computed: the first bytes decide the format,
/// then the same file is validated and stored. The client's file name is kept for display only.
/// </summary>
public sealed class BuildUploadBuffer : IAsyncDisposable, IDisposable
{
    private readonly FileStream _file;

    private BuildUploadBuffer(FileStream file, long length, string sha256, byte[] head, string fileName)
    {
        _file = file;
        Length = length;
        Sha256 = sha256;
        Head = head;
        FileName = fileName;
    }

    public long Length { get; }
    public string Sha256 { get; }
    public byte[] Head { get; }
    public string FileName { get; }

    public static async Task<BuildUploadBuffer> FromAsync(Stream source, string? fileName, long maxBytes, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), "orken-build-" + Guid.NewGuid().ToString("N"));
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > maxBytes) throw new BuildFileTooLargeException($"Arquivo grande demais: o limite é {maxBytes / (1024 * 1024)} MB.");
                hash.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            await file.FlushAsync(ct);
            file.Position = 0;
            var head = new byte[(int)Math.Min(BuildFileSignatures.HeadLength, total)];
            await file.ReadExactlyAsync(head, ct);
            file.Position = 0;
            return new BuildUploadBuffer(file, total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), head,
                string.IsNullOrWhiteSpace(fileName) ? "arquivo" : Path.GetFileName(fileName));
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    /// <summary>The whole file from the start (the same stream — read it once at a time).</summary>
    public Stream Open()
    {
        _file.Position = 0;
        return new NonClosingStream(_file);
    }

    public async Task<byte[]> ReadAllAsync(CancellationToken ct)
    {
        _file.Position = 0;
        var bytes = new byte[Length];
        await _file.ReadExactlyAsync(bytes, ct);
        _file.Position = 0;
        return bytes;
    }

    public void Dispose() => _file.Dispose();

    public ValueTask DisposeAsync() => _file.DisposeAsync();

    /// <summary>Lets a consumer (the storage SDK) dispose its stream without deleting the buffer.</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => inner.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { /* the buffer owns the file */ }
    }
}

/// <summary>
/// Per-company Build feature flags (Build3D, BuildAI, BuildVision, BuildPredict). A missing row means the defaults — 3D and
/// predictions on (no external cost), AI and vision off. Only the platform (SuperAdmin) changes them; every change is audited.
/// </summary>
public sealed class BuildTenantSettingsService
{
    public const string Code3DDisabled = "build_3d_disabled";

    private readonly NexoDbContext _db;
    private readonly IAuditWriter _audit;

    public BuildTenantSettingsService(NexoDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<BuildTenantSettingsDto> GetAsync(Guid tenantId, CancellationToken ct)
        => Map(tenantId, await _db.BldTenantSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct));

    public async Task<bool> Build3DEnabledAsync(Guid tenantId, CancellationToken ct)
        => await _db.BldTenantSettings.AsNoTracking().Where(s => s.TenantId == tenantId)
               .Select(s => (bool?)s.Build3DEnabled).FirstOrDefaultAsync(ct)
           ?? BuildTenantSettings.DefaultBuild3D;

    /// <summary>403 build_3d_disabled when the platform turned 3D off for this company.</summary>
    public async Task EnsureBuild3DAsync(Guid tenantId, CancellationToken ct)
    {
        if (!await Build3DEnabledAsync(tenantId, ct))
            throw new ForbiddenException("O módulo 3D do Orken Build está desligado para esta empresa. Fale com o suporte Orken.", Code3DDisabled);
    }

    /// <summary>Platform side: changes the flags of a company (only the ones sent). Audited with the platform actor.</summary>
    public async Task<BuildTenantSettingsDto> UpdateAsync(Guid tenantId, UpdateBuildTenantSettingsRequest r, PlatformActor actor,
        CancellationToken ct)
    {
        var tenantName = await _db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId).Select(t => t.CompanyName)
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Empresa", tenantId);
        var row = await _db.BldTenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (row is null)
        {
            row = BuildTenantSettings.Create(tenantId);
            _db.BldTenantSettings.Add(row);
        }
        var before = new { row.Build3DEnabled, row.BuildAiEnabled, row.BuildVisionEnabled, row.BuildPredictEnabled };
        row.Update(r.Build3DEnabled ?? row.Build3DEnabled, r.BuildAiEnabled ?? row.BuildAiEnabled,
            r.BuildVisionEnabled ?? row.BuildVisionEnabled, r.BuildPredictEnabled ?? row.BuildPredictEnabled, actor.Id, actor.Name);
        var after = new { row.Build3DEnabled, row.BuildAiEnabled, row.BuildVisionEnabled, row.BuildPredictEnabled };
        _audit.Stage(AuditActions.BuildTenantSettingsChanged, AuditSeverity.Warning, "BuildTenantSettings", tenantId.ToString(),
            $"Recursos do Orken Build de '{tenantName}': 3D {OnOff(after.Build3DEnabled)}, IA {OnOff(after.BuildAiEnabled)}, "
            + $"visão {OnOff(after.BuildVisionEnabled)}, previsões {OnOff(after.BuildPredictEnabled)}.",
            tenantId: tenantId, actorId: actor.Id, actorName: actor.Name, actorType: "platform",
            metadata: new { before, after, userAgent = actor.UserAgent, correlationId = actor.RequestId });
        await _db.SaveChangesAsync(ct);
        return Map(tenantId, row);
    }

    private static string OnOff(bool v) => v ? "ligado" : "desligado";

    private static BuildTenantSettingsDto Map(Guid tenantId, BuildTenantSettings? s)
        => s is null
            ? new BuildTenantSettingsDto(tenantId, BuildTenantSettings.DefaultBuild3D, BuildTenantSettings.DefaultBuildAi,
                BuildTenantSettings.DefaultBuildVision, BuildTenantSettings.DefaultBuildPredict, false, null, null)
            : new BuildTenantSettingsDto(tenantId, s.Build3DEnabled, s.BuildAiEnabled, s.BuildVisionEnabled, s.BuildPredictEnabled,
                true, s.UpdatedByName, BuildScope.Utc(s.UpdatedAt));
}

/// <summary>Entity → DTO mapping of Wave 4 (no storage keys ever leave).</summary>
internal static class BuildTwinMap
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static BuildModelVersionDto Version(BuildModelVersion v)
        => new(v.Id, v.ModelId, v.Number, v.SourceFormat.ToString(), v.SourceFileName, v.SizeBytes, v.Sha256,
            v.GeometrySource.ToString(), v.ViewerSource.ToString(), v.ViewerFileKey is not null, v.Status.ToString(), v.StatusMessage,
            v.ElementCount, v.StoreyCount, v.Schema,
            v.LayersJson is null ? null : JsonSerializer.Deserialize<List<string>>(v.LayersJson, Json),
            BuildScope.Utc(v.ProcessedAt), BuildScope.Utc(v.CreatedAt));

    public static BuildModelDto Model(BuildModel m, IEnumerable<BuildModelVersion> versions)
        => new(m.Id, m.ProjectId, m.Name, m.Discipline.ToString(), m.CurrentVersionId,
            versions.Where(v => v.ModelId == m.Id).OrderByDescending(v => v.Number).Select(Version).ToList(), BuildScope.Utc(m.CreatedAt));

    public static BuildModelZoneDto Zone(BuildModelZone z, int elementCount)
        => new(z.Id, z.ModelVersionId, z.Kind.ToString(), z.Name, z.ExternalId, z.ParentZoneId, z.Elevation, z.Order, z.Outline(),
            z.SheetVersionId, z.Box(), elementCount);

    public static BuildElementEventDto Event(BuildElementProgressEvent e)
        => new(e.Id, e.ModelVersionId, e.ElementExternalId, e.State.ToString(), e.Source.ToString(), e.SourceRef, e.Note, e.EffectiveDate,
            e.RecordedByName, BuildScope.Utc(e.CreatedAt));

    public static BuildTwinStateDto State(BuildTwinStateEngine.Result r)
        => new(r.Date, r.Counts, r.States, r.ZoneStates, r.Unbound, r.Elements.Count);
}
