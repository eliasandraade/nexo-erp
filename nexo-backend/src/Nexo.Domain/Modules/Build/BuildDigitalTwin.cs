using System.Globalization;
using System.Text.Json;
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Build;

// ═══════════════════════════════════════════════════════════════════════
// WAVE 4 — DIGITAL TWIN. Three layers kept apart on purpose:
//   1. geometric source of truth: the model file (private) and its viewer derivative (GLB, private);
//   2. operational truth: stages, schedule, daily log, measurements and the element progress events;
//   3. interpretation: stage bindings (manual today, AI suggestions later — always with their source).
// Geometry is never invented: a "simplified representation" only ever shows what the user typed.
// ═══════════════════════════════════════════════════════════════════════

public enum BuildModelDiscipline
{
    Arquitetura = 1,
    Estrutura   = 2,
    Hidraulica  = 3,
    Eletrica    = 4,
    Outro       = 5,
}

public enum BuildModelSourceFormat
{
    Ifc   = 1,
    Glb   = 2,
    Gltf  = 3,
    Rvt   = 4,
    Dwg   = 5,
    Dxf   = 6,
    Pdf   = 7,
    Image = 8,
}

/// <summary>Where the geometry shown comes from — drives the disclaimers shown to users and clients.</summary>
public enum BuildGeometrySource
{
    /// <summary>An IFC with geometry: elements and hierarchy straight from the BIM model.</summary>
    NativeBim                = 1,
    /// <summary>A 3D file (GLB/glTF) exported by the designer: real geometry, semantics only from node names.</summary>
    Converted3D              = 2,
    /// <summary>A PDF, image or DXF sheet: zones are drawn/typed by the user on top of it (no 3D elements).</summary>
    AssistedMapping          = 3,
    /// <summary>No model: boxes typed by the user (dimensions in meters) — "representação simplificada, não é o projeto".</summary>
    SimplifiedRepresentation = 4,
}

public enum BuildViewerSource
{
    None             = 0,
    /// <summary>The uploaded file is already a viewer format (GLB/glTF).</summary>
    Uploaded         = 1,
    /// <summary>Converted on the server by IfcConvert (optional, only when configured).</summary>
    ConvertedServer  = 2,
    /// <summary>Converted in the browser (web-ifc) and uploaded as a derivative whose node names are the IFC GlobalIds.</summary>
    ConvertedBrowser = 3,
}

public enum BuildModelVersionStatus
{
    Uploaded   = 1,
    Queued     = 2,
    Processing = 3,
    Ready      = 4,
    Failed     = 5,
    /// <summary>Format we cannot process (RVT/DWG): nothing was stored; the message says how to export.</summary>
    Rejected   = 6,
}

public enum BuildModelJobKind
{
    Parse   = 1,
    Convert = 2,
}

public enum BuildModelJobStatus
{
    Pending = 1,
    Running = 2,
    Done    = 3,
    Failed  = 4,
}

public enum BuildZoneKind
{
    // from the model (IFC spatial structure, glTF top-level nodes)
    Site     = 1,
    Building = 2,
    Storey   = 3,
    Space    = 4,
    // created by the user (assisted mapping / simplified representation)
    Floor    = 5,
    Area     = 6,
    Room     = 7,
    Facade   = 8,
    Roof     = 9,
    External = 10,
}

public enum BuildBindingScope
{
    Element  = 1,
    Zone     = 2,
    Category = 3,
}

public enum BuildBindingSource
{
    Manual      = 1,
    AiSuggested = 2,
    AiConfirmed = 3,
}

public enum BuildElementState
{
    NotStarted  = 1,
    InProgress  = 2,
    Completed   = 3,
    Delayed     = 4,
    Blocked     = 5,
    NeedsReview = 6,
}

public enum BuildElementEventSource
{
    Manual       = 1,
    Measurement  = 2,
    DailyLog     = 3,
    AiConfirmed  = 4,
    StageDerived = 5,
}

public enum BuildRealityCaptureType
{
    Photo      = 1,
    Photo360   = 2,
    Video      = 3,
    Drone      = 4,
    /// <summary>Accepted and stored (.las / .e57) — no processing yet.</summary>
    PointCloud = 5,
}

/// <summary>
/// Costly Build features per company, switched by the platform (SuperAdmin). Not a tenant-filtered entity: the platform
/// writes it without a tenant context; tenant reads always name the tenant. A missing row means the defaults.
/// </summary>
public class BuildTenantSettings
{
    public const bool DefaultBuild3D      = true;   // no external cost (parsing is local; conversion optional)
    public const bool DefaultBuildAi      = false;
    public const bool DefaultBuildVision  = false;
    public const bool DefaultBuildPredict = true;   // deterministic analytics

    public Guid     Id                  { get; private set; }
    public Guid     TenantId            { get; private set; }
    public bool     Build3DEnabled      { get; private set; } = DefaultBuild3D;
    public bool     BuildAiEnabled      { get; private set; } = DefaultBuildAi;
    public bool     BuildVisionEnabled  { get; private set; } = DefaultBuildVision;
    public bool     BuildPredictEnabled { get; private set; } = DefaultBuildPredict;
    public Guid?    UpdatedBy           { get; private set; }
    public string?  UpdatedByName       { get; private set; }
    public DateTime UpdatedAt           { get; private set; }

    private BuildTenantSettings() { }

    public static BuildTenantSettings Create(Guid tenantId)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Empresa inválida.");
        return new BuildTenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, UpdatedAt = DateTime.UtcNow };
    }

    public void Update(bool build3D, bool ai, bool vision, bool predict, Guid? by, string? byName)
    {
        Build3DEnabled      = build3D;
        BuildAiEnabled      = ai;
        BuildVisionEnabled  = vision;
        BuildPredictEnabled = predict;
        UpdatedBy           = by;
        UpdatedByName       = string.IsNullOrWhiteSpace(byName) ? null : byName.Trim()[..Math.Min(200, byName.Trim().Length)];
        UpdatedAt           = DateTime.UtcNow;
    }
}

/// <summary>A model of a project (one per discipline or sheet) — the files are its versions.</summary>
public class BuildModel : TenantEntity
{
    private BuildModel() { }
    private BuildModel(Guid tenantId) : base(tenantId) { }

    public Guid                 ProjectId        { get; private set; }
    public string               Name             { get; private set; } = string.Empty;
    public BuildModelDiscipline Discipline       { get; private set; }
    /// <summary>The latest version that finished processing (Ready) — what the viewer and the portal use.</summary>
    public Guid?                CurrentVersionId { get; private set; }
    public Guid                 CreatedBy        { get; private set; }

    public static BuildModel Create(Guid tenantId, Guid projectId, string name, BuildModelDiscipline discipline, Guid createdBy)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        if (!Enum.IsDefined(discipline)) throw new DomainException("Disciplina inválida.", "invalid_value");
        return new BuildModel(tenantId)
        {
            ProjectId  = projectId,
            Name       = BuildMoney.Required(name, 200, "O nome do modelo"),
            Discipline = discipline,
            CreatedBy  = createdBy,
        };
    }

    public void Rename(string name, BuildModelDiscipline discipline)
    {
        if (!Enum.IsDefined(discipline)) throw new DomainException("Disciplina inválida.", "invalid_value");
        Name = BuildMoney.Required(name, 200, "O nome do modelo");
        Discipline = discipline;
        SetUpdatedAt();
    }

    public void SetCurrentVersion(Guid versionId)
    {
        CurrentVersionId = versionId;
        SetUpdatedAt();
    }
}

/// <summary>
/// One uploaded file of a model. The source file is private and never leaves through the portal; the portal only ever
/// receives a short-lived signed URL of the viewer derivative (<see cref="ViewerFileKey"/>, a GLB).
/// </summary>
public class BuildModelVersion : TenantEntity
{
    private BuildModelVersion() { }
    private BuildModelVersion(Guid tenantId) : base(tenantId) { }

    public Guid                    ProjectId      { get; private set; }
    public Guid                    ModelId        { get; private set; }
    public int                     Number         { get; private set; }
    public BuildModelSourceFormat  SourceFormat   { get; private set; }
    /// <summary>Private key of the source file; null when the file was refused (never stored).</summary>
    public string?                 SourceFileKey  { get; private set; }
    /// <summary>The client's file name — display only, never used in a storage key.</summary>
    public string                  SourceFileName { get; private set; } = string.Empty;
    public long                    SizeBytes      { get; private set; }
    public string?                 Sha256         { get; private set; }
    public BuildGeometrySource     GeometrySource { get; private set; }
    public string?                 ViewerFileKey  { get; private set; }
    public BuildViewerSource       ViewerSource   { get; private set; }
    public BuildModelVersionStatus Status         { get; private set; }
    /// <summary>User-readable (Portuguese) explanation of the status.</summary>
    public string?                 StatusMessage  { get; private set; }
    public int                     ElementCount   { get; private set; }
    public int                     StoreyCount    { get; private set; }
    /// <summary>IFC schema (IFC2X3, IFC4…) or glTF version.</summary>
    public string?                 Schema         { get; private set; }
    /// <summary>DXF layer names (json list) — a 2D sheet to map zones on.</summary>
    public string?                 LayersJson     { get; private set; }
    public DateTime?               ProcessedAt    { get; private set; }
    public Guid                    CreatedBy      { get; private set; }

    public static BuildModelVersion Stored(Guid tenantId, Guid projectId, Guid modelId, Guid versionId, int number,
        BuildModelSourceFormat format, string sourceKey, string fileName, long size, string sha256, Guid createdBy)
    {
        var v = New(tenantId, projectId, modelId, number, format, fileName, size, createdBy);
        v.Id = versionId;
        v.SourceFileKey = string.IsNullOrWhiteSpace(sourceKey) ? throw new DomainException("Arquivo inválido.") : sourceKey;
        v.Sha256 = sha256;
        v.GeometrySource = InitialGeometry(format);
        if (format is BuildModelSourceFormat.Glb or BuildModelSourceFormat.Gltf)
        {
            v.ViewerFileKey = sourceKey;   // already a viewer format
            v.ViewerSource = BuildViewerSource.Uploaded;
        }
        if (format is BuildModelSourceFormat.Pdf or BuildModelSourceFormat.Image)
        {
            v.Status = BuildModelVersionStatus.Ready;
            v.ProcessedAt = DateTime.UtcNow;
            v.StatusMessage = "Folha 2D pronta para o mapeamento assistido: desenhe as zonas sobre ela. Não há elementos 3D.";
        }
        else
        {
            v.Status = BuildModelVersionStatus.Queued;
            v.StatusMessage = "Na fila de processamento.";
        }
        return v;
    }

    public static BuildModelVersion Rejected(Guid tenantId, Guid projectId, Guid modelId, int number,
        BuildModelSourceFormat format, string fileName, long size, string message, Guid createdBy)
    {
        var v = New(tenantId, projectId, modelId, number, format, fileName, size, createdBy);
        v.Status = BuildModelVersionStatus.Rejected;
        v.StatusMessage = message;
        v.GeometrySource = BuildGeometrySource.AssistedMapping;
        return v;
    }

    private static BuildModelVersion New(Guid tenantId, Guid projectId, Guid modelId, int number, BuildModelSourceFormat format,
        string fileName, long size, Guid createdBy)
    {
        if (projectId == Guid.Empty || modelId == Guid.Empty) throw new DomainException("Modelo inválido.");
        if (!Enum.IsDefined(format)) throw new DomainException("Formato inválido.", "invalid_value");
        var name = string.IsNullOrWhiteSpace(fileName) ? "arquivo" : Path.GetFileName(fileName.Trim());
        return new BuildModelVersion(tenantId)
        {
            ProjectId      = projectId,
            ModelId        = modelId,
            Number         = Math.Max(1, number),
            SourceFormat   = format,
            SourceFileName = name.Length > 255 ? name[..255] : name,
            SizeBytes      = Math.Max(0, size),
            ViewerSource   = BuildViewerSource.None,
            CreatedBy      = createdBy,
        };
    }

    private static BuildGeometrySource InitialGeometry(BuildModelSourceFormat f) => f switch
    {
        BuildModelSourceFormat.Ifc => BuildGeometrySource.NativeBim,
        BuildModelSourceFormat.Glb or BuildModelSourceFormat.Gltf => BuildGeometrySource.Converted3D,
        _ => BuildGeometrySource.AssistedMapping,
    };

    public bool IsProcessed => Status is BuildModelVersionStatus.Ready or BuildModelVersionStatus.Failed
        or BuildModelVersionStatus.Rejected;

    public void MarkProcessing()
    {
        Status = BuildModelVersionStatus.Processing;
        StatusMessage = "Processando o arquivo.";
        SetUpdatedAt();
    }

    public void MarkQueued(string? message = null)
    {
        Status = BuildModelVersionStatus.Queued;
        StatusMessage = message ?? "Na fila de processamento.";
        SetUpdatedAt();
    }

    /// <summary>Parsed: counts, schema and the geometry source actually found (an IFC without any geometry is only hierarchy).</summary>
    public void MarkReady(int elements, int storeys, string? schema, BuildGeometrySource geometry, string message, string? layersJson = null)
    {
        Status = BuildModelVersionStatus.Ready;
        ElementCount = Math.Max(0, elements);
        StoreyCount = Math.Max(0, storeys);
        Schema = schema is { Length: > 40 } ? schema[..40] : schema;
        GeometrySource = geometry;
        StatusMessage = Clip(message, 1000);
        LayersJson = layersJson;
        ProcessedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void MarkFailed(string message)
    {
        Status = BuildModelVersionStatus.Failed;
        StatusMessage = Clip(message, 1000);
        ProcessedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void AppendMessage(string message)
    {
        StatusMessage = Clip(string.IsNullOrWhiteSpace(StatusMessage) ? message : $"{StatusMessage} {message}", 1000);
        SetUpdatedAt();
    }

    public void AttachViewer(string key, BuildViewerSource source)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new DomainException("Arquivo do visualizador inválido.");
        if (source == BuildViewerSource.None) throw new DomainException("Origem do visualizador inválida.");
        ViewerFileKey = key;
        ViewerSource = source;
        SetUpdatedAt();
    }

    private static string Clip(string text, int max) => text.Length > max ? text[..max] : text;
}

/// <summary>
/// Database queue of model processing (parse / optional conversion). Claimed atomically (FOR UPDATE SKIP LOCKED), at most
/// <see cref="MaxAttempts"/> attempts, a lock that expires so a crashed worker's job is picked up again.
/// </summary>
public class BuildModelProcessingJob : TenantEntity
{
    public const int MaxAttempts = 3;

    private BuildModelProcessingJob() { }
    private BuildModelProcessingJob(Guid tenantId) : base(tenantId) { }

    public Guid                VersionId   { get; private set; }
    public BuildModelJobKind   Kind        { get; private set; }
    public BuildModelJobStatus Status      { get; private set; }
    public int                 Attempts    { get; private set; }
    public DateTime?           LockedUntil { get; private set; }
    public string?             Error       { get; private set; }
    public DateTime?           StartedAt   { get; private set; }
    public DateTime?           FinishedAt  { get; private set; }

    public static BuildModelProcessingJob Enqueue(Guid tenantId, Guid versionId, BuildModelJobKind kind)
    {
        if (versionId == Guid.Empty) throw new DomainException("Versão inválida.");
        return new BuildModelProcessingJob(tenantId) { VersionId = versionId, Kind = kind, Status = BuildModelJobStatus.Pending };
    }

    public void Complete()
    {
        Status = BuildModelJobStatus.Done;
        Error = null;
        LockedUntil = null;
        FinishedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    /// <summary>A failure: back to the queue while attempts remain (unless permanent), else failed for good.</summary>
    public bool Fail(string error, bool permanent)
    {
        Error = error.Length > 2000 ? error[..2000] : error;
        LockedUntil = null;
        if (permanent || Attempts >= MaxAttempts)
        {
            Status = BuildModelJobStatus.Failed;
            FinishedAt = DateTime.UtcNow;
        }
        else
        {
            Status = BuildModelJobStatus.Pending;
        }
        SetUpdatedAt();
        return Status == BuildModelJobStatus.Failed;
    }
}

/// <summary>
/// A zone: from the model (IFC Site/Building/Storey/Space or glTF top-level node — <see cref="ModelVersionId"/> set) or
/// created by the user for assisted mapping / simplified representation (no version).
/// </summary>
public class BuildModelZone : TenantEntity
{
    public const int MaxOutlinePoints = 200;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private BuildModelZone() { }
    private BuildModelZone(Guid tenantId) : base(tenantId) { }

    public Guid          ProjectId         { get; private set; }
    public Guid?         ModelVersionId    { get; private set; }
    public BuildZoneKind Kind              { get; private set; }
    public string        Name              { get; private set; } = string.Empty;
    /// <summary>IFC GlobalId (or glTF node name) for zones read from a model.</summary>
    public string?       ExternalId        { get; private set; }
    public Guid?         ParentZoneId      { get; private set; }
    public decimal?      Elevation         { get; private set; }
    public int           Order             { get; private set; }
    /// <summary>Polygon on a sheet: [{x,y}] normalized 0..1, ≤ 200 points.</summary>
    public string?       Outline2DJson     { get; private set; }
    /// <summary>The PDF/image version the outline is drawn on.</summary>
    public Guid?         SheetVersionId    { get; private set; }
    /// <summary>{width, depth, height, elevation} in meters, typed by the user — a simplified representation, not the design.</summary>
    public string?       SimplifiedBoxJson { get; private set; }
    public Guid?         CreatedBy         { get; private set; }

    public bool IsManual => ModelVersionId is null;

    public static BuildModelZone FromModel(Guid tenantId, Guid projectId, Guid versionId, Guid id, BuildZoneKind kind, string? name,
        string? externalId, Guid? parentZoneId, decimal? elevation, int order)
    {
        var z = new BuildModelZone(tenantId)
        {
            ProjectId      = projectId,
            ModelVersionId = versionId,
            Kind           = kind,
            Name           = Short(string.IsNullOrWhiteSpace(name) ? kind.ToString() : name.Trim(), 200),
            ExternalId     = externalId is null ? null : Short(externalId, 100),
            ParentZoneId   = parentZoneId,
            Elevation      = elevation,
            Order          = order,
        };
        z.Id = id;
        return z;
    }

    public static BuildModelZone Manual(Guid tenantId, Guid projectId, BuildZoneKind kind, string name, int order, Guid? parentZoneId,
        IReadOnlyList<BuildZonePoint>? outline, Guid? sheetVersionId, BuildSimplifiedBox? box, Guid createdBy)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        var z = new BuildModelZone(tenantId) { ProjectId = projectId, CreatedBy = createdBy };
        z.Apply(kind, name, order, parentZoneId, outline, sheetVersionId, box);
        return z;
    }

    public void Update(BuildZoneKind kind, string name, int order, Guid? parentZoneId, IReadOnlyList<BuildZonePoint>? outline,
        Guid? sheetVersionId, BuildSimplifiedBox? box)
    {
        if (!IsManual) throw new DomainException("Zonas lidas do modelo não podem ser editadas.", "zone_from_model");
        if (parentZoneId == Id) throw new DomainException("Uma zona não pode ser pai dela mesma.", "invalid_parent_zone");
        Apply(kind, name, order, parentZoneId, outline, sheetVersionId, box);
        SetUpdatedAt();
    }

    private void Apply(BuildZoneKind kind, string name, int order, Guid? parentZoneId, IReadOnlyList<BuildZonePoint>? outline,
        Guid? sheetVersionId, BuildSimplifiedBox? box)
    {
        if (kind is < BuildZoneKind.Floor or > BuildZoneKind.External)
            throw new DomainException("Tipo de zona inválido: use Floor, Area, Room, Facade, Roof ou External.", "invalid_value");
        if (outline is { Count: > 0 } && sheetVersionId is null)
            throw new DomainException("O contorno precisa da folha (PDF/imagem) em que foi desenhado.", "outline_needs_sheet");
        Kind = kind;
        Name = BuildMoney.Required(name, 200, "O nome da zona");
        Order = Math.Max(0, order);
        ParentZoneId = parentZoneId;
        Outline2DJson = outline is { Count: > 0 } ? JsonSerializer.Serialize(ValidOutline(outline), Json) : null;
        SheetVersionId = sheetVersionId;
        SimplifiedBoxJson = box is null ? null : JsonSerializer.Serialize(box.Validated(), Json);
    }

    public static IReadOnlyList<BuildZonePoint> ValidOutline(IReadOnlyList<BuildZonePoint> points)
    {
        if (points.Count is < 3 or > MaxOutlinePoints)
            throw new DomainException($"O contorno precisa de 3 a {MaxOutlinePoints} pontos.", "invalid_outline");
        if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X is < 0 or > 1 || p.Y is < 0 or > 1))
            throw new DomainException("Os pontos do contorno são proporcionais à folha: X e Y entre 0 e 1.", "invalid_outline");
        return points.Select(p => new BuildZonePoint(Math.Round(p.X, 6), Math.Round(p.Y, 6))).ToList();
    }

    public IReadOnlyList<BuildZonePoint>? Outline()
        => Outline2DJson is null ? null : JsonSerializer.Deserialize<List<BuildZonePoint>>(Outline2DJson, Json);

    public BuildSimplifiedBox? Box()
        => SimplifiedBoxJson is null ? null : JsonSerializer.Deserialize<BuildSimplifiedBox>(SimplifiedBoxJson, Json);

    private static string Short(string s, int max) => s.Length > max ? s[..max] : s;
}

public sealed record BuildZonePoint(double X, double Y);

/// <summary>Dimensions in meters typed by the user. Nothing is ever generated: no box unless the user typed one.</summary>
public sealed record BuildSimplifiedBox(decimal Width, decimal Depth, decimal Height, decimal Elevation = 0m)
{
    public BuildSimplifiedBox Validated()
    {
        if (Width is <= 0 or > 1000 || Depth is <= 0 or > 1000 || Height is <= 0 or > 500)
            throw new DomainException("Medidas da representação simplificada inválidas: largura/profundidade até 1000 m e altura até 500 m, maiores que zero.",
                "invalid_simplified_box");
        if (Elevation is < -200 or > 1000)
            throw new DomainException("Cota inválida na representação simplificada.", "invalid_simplified_box");
        return this with
        {
            Width = Math.Round(Width, 3), Depth = Math.Round(Depth, 3), Height = Math.Round(Height, 3), Elevation = Math.Round(Elevation, 3),
        };
    }
}

/// <summary>One element read from a model version (IFC product, glTF mesh node). ExternalId is unique per version.</summary>
public class BuildModelElement : TenantEntity
{
    public const int MaxPropertiesJson = 2048;

    private BuildModelElement() { }
    private BuildModelElement(Guid tenantId) : base(tenantId) { }

    public Guid    ProjectId      { get; private set; }
    public Guid    ModelVersionId { get; private set; }
    public string  ExternalId     { get; private set; } = string.Empty;
    /// <summary>IFC class (IfcWall, IfcSlab…) or the glTF category.</summary>
    public string  Category       { get; private set; } = string.Empty;
    public string? Name           { get; private set; }
    public string? TypeName       { get; private set; }
    public string? Material       { get; private set; }
    /// <summary>The containing storey/space (or glTF top-level zone).</summary>
    public Guid?   ZoneId         { get; private set; }
    /// <summary>Selected quantities (netVolume, netArea, length, count) — compact, ≤ 2 KB.</summary>
    public string? PropertiesJson { get; private set; }

    public static BuildModelElement Create(Guid tenantId, Guid projectId, Guid versionId, string externalId, string category,
        string? name, string? typeName, string? material, Guid? zoneId, string? propertiesJson)
        => new(tenantId)
        {
            ProjectId      = projectId,
            ModelVersionId = versionId,
            ExternalId     = Short(externalId, 100)!,
            Category       = Short(string.IsNullOrWhiteSpace(category) ? "Elemento" : category, 100)!,
            Name           = Short(name, 300),
            TypeName       = Short(typeName, 300),
            Material       = Short(material, 200),
            ZoneId         = zoneId,
            PropertiesJson = propertiesJson is { Length: > MaxPropertiesJson } ? null : propertiesJson,
        };

    private static string? Short(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }
}

/// <summary>
/// Interpretation layer: links elements (by external id), a zone or a category to a stage. The element's state is derived
/// from the stage's progress. Precedence when computing an element: Element &gt; Zone &gt; Category.
/// </summary>
public class BuildModelStageBinding : TenantEntity
{
    private BuildModelStageBinding() { }
    private BuildModelStageBinding(Guid tenantId) : base(tenantId) { }

    public Guid               ProjectId         { get; private set; }
    public Guid               StageId           { get; private set; }
    public BuildBindingScope  Scope             { get; private set; }
    /// <summary>Element scope: the version the binding was made against (ids are matched across versions — IFC GlobalIds are stable).
    /// Category scope: restricts to that version (null = every version of the project).</summary>
    public Guid?              ModelVersionId    { get; private set; }
    public string?            ElementExternalId { get; private set; }
    public Guid?              ZoneId            { get; private set; }
    public string?            Category          { get; private set; }
    public BuildBindingSource Source            { get; private set; }
    public Guid               CreatedBy         { get; private set; }

    public static BuildModelStageBinding Create(Guid tenantId, Guid projectId, Guid stageId, BuildBindingScope scope,
        Guid? versionId, string? externalId, Guid? zoneId, string? category, BuildBindingSource source, Guid createdBy)
    {
        if (projectId == Guid.Empty || stageId == Guid.Empty) throw new DomainException("Etapa inválida.");
        if (!Enum.IsDefined(scope) || !Enum.IsDefined(source)) throw new DomainException("Vínculo inválido.", "invalid_value");
        var b = new BuildModelStageBinding(tenantId)
        {
            ProjectId = projectId, StageId = stageId, Scope = scope, Source = source, CreatedBy = createdBy, ModelVersionId = versionId,
        };
        switch (scope)
        {
            case BuildBindingScope.Element:
                if (string.IsNullOrWhiteSpace(externalId) || versionId is null)
                    throw new DomainException("Informe a versão e o id do elemento.", "invalid_binding");
                b.ElementExternalId = externalId.Trim();
                break;
            case BuildBindingScope.Zone:
                b.ZoneId = zoneId ?? throw new DomainException("Informe a zona.", "invalid_binding");
                break;
            default:
                if (string.IsNullOrWhiteSpace(category)) throw new DomainException("Informe a categoria.", "invalid_binding");
                b.Category = category.Trim().Length > 100 ? category.Trim()[..100] : category.Trim();
                break;
        }
        return b;
    }
}

/// <summary>
/// Explicit state of an element on a date (append-only): overrides what the stage binding would derive. State of an element
/// at date d = its latest event with EffectiveDate ≤ d, else the derived state.
/// </summary>
public class BuildElementProgressEvent : TenantEntity
{
    private BuildElementProgressEvent() { }
    private BuildElementProgressEvent(Guid tenantId) : base(tenantId) { }

    public Guid                    ProjectId         { get; private set; }
    public Guid                    ModelVersionId    { get; private set; }
    public string                  ElementExternalId { get; private set; } = string.Empty;
    public BuildElementState       State             { get; private set; }
    public BuildElementEventSource Source            { get; private set; }
    public Guid?                   SourceRef         { get; private set; }
    public string?                 Note              { get; private set; }
    public DateOnly                EffectiveDate     { get; private set; }
    public Guid                    RecordedBy        { get; private set; }
    public string                  RecordedByName    { get; private set; } = string.Empty;

    public static BuildElementProgressEvent Record(Guid tenantId, Guid projectId, Guid versionId, string externalId,
        BuildElementState state, BuildElementEventSource source, DateOnly effectiveDate, Guid recordedBy, string? recordedByName,
        string? note = null, Guid? sourceRef = null)
    {
        if (projectId == Guid.Empty || versionId == Guid.Empty || string.IsNullOrWhiteSpace(externalId))
            throw new DomainException("Elemento inválido.");
        if (!Enum.IsDefined(state) || !Enum.IsDefined(source)) throw new DomainException("Estado inválido.", "invalid_value");
        return new BuildElementProgressEvent(tenantId)
        {
            ProjectId         = projectId,
            ModelVersionId    = versionId,
            ElementExternalId = externalId.Trim().Length > 100 ? externalId.Trim()[..100] : externalId.Trim(),
            State             = state,
            Source            = source,
            SourceRef         = sourceRef,
            Note              = BuildMoney.Clean(note, 1000, "A observação"),
            EffectiveDate     = effectiveDate,
            RecordedBy        = recordedBy,
            RecordedByName    = string.IsNullOrWhiteSpace(recordedByName) ? "usuário" : recordedByName.Trim(),
        };
    }
}

/// <summary>A photo, 360° photo, video, drone flight or point cloud of the site — private file, client-visible only when marked.</summary>
public class BuildRealityCapture : TenantEntity
{
    private BuildRealityCapture() { }
    private BuildRealityCapture(Guid tenantId) : base(tenantId) { }

    public Guid                    ProjectId         { get; private set; }
    public BuildRealityCaptureType Type              { get; private set; }
    public DateTime                CapturedAt        { get; private set; }
    public Guid?                   StageId           { get; private set; }
    public Guid?                   ZoneId            { get; private set; }
    public string?                 ElementExternalId { get; private set; }
    public string                  Title             { get; private set; } = string.Empty;
    /// <summary>Internal notes — never shown in the client portal.</summary>
    public string?                 Notes             { get; private set; }
    public string                  MediaKey          { get; private set; } = string.Empty;
    public string?                 ThumbnailKey      { get; private set; }
    public string                  ContentType       { get; private set; } = string.Empty;
    public long                    SizeBytes         { get; private set; }
    public bool                    ClientVisible     { get; private set; }
    public Guid                    CreatedBy         { get; private set; }

    public static BuildRealityCapture Create(Guid tenantId, Guid projectId, Guid id, BuildRealityCaptureType type, DateTime capturedAt,
        string title, string mediaKey, string contentType, long size, Guid createdBy)
    {
        if (projectId == Guid.Empty) throw new DomainException("Obra inválida.");
        if (!Enum.IsDefined(type)) throw new DomainException("Tipo de captura inválido.", "invalid_value");
        var c = new BuildRealityCapture(tenantId)
        {
            ProjectId   = projectId,
            Type        = type,
            CapturedAt  = DateTime.SpecifyKind(capturedAt, DateTimeKind.Utc),
            Title       = BuildMoney.Required(title, 200, "O título"),
            MediaKey    = string.IsNullOrWhiteSpace(mediaKey) ? throw new DomainException("Arquivo inválido.") : mediaKey,
            ContentType = contentType,
            SizeBytes   = Math.Max(0, size),
            CreatedBy   = createdBy,
        };
        c.Id = id;
        return c;
    }

    public void Describe(string title, string? notes, DateTime capturedAt, Guid? stageId, Guid? zoneId, string? elementExternalId)
    {
        Title = BuildMoney.Required(title, 200, "O título");
        Notes = BuildMoney.Clean(notes, 2000, "As observações");
        CapturedAt = DateTime.SpecifyKind(capturedAt, DateTimeKind.Utc);
        StageId = stageId;
        ZoneId = zoneId;
        ElementExternalId = BuildMoney.Clean(elementExternalId, 100, "O id do elemento");
        SetUpdatedAt();
    }

    public void SetClientVisible(bool visible)
    {
        ClientVisible = visible;
        SetUpdatedAt();
    }
}

/// <summary>Parses user input for the Wave 4 enums (accent- and case-insensitive: "Hidráulica" = Hidraulica).</summary>
public static class BuildTwinParse
{
    public static TEnum Enum<TEnum>(string? value, string what) where TEnum : struct, System.Enum
    {
        var clean = Fold(value);
        if (clean.Length > 0 && !int.TryParse(clean, out _)
            && System.Enum.TryParse<TEnum>(clean, true, out var v) && System.Enum.IsDefined(v))
            return v;
        throw new DomainException($"{what} inválido(a): '{value}'.", "invalid_value");
    }

    public static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Trim().Normalize(System.Text.NormalizationForm.FormD);
        return new string(normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .Replace(" ", string.Empty);
    }
}
