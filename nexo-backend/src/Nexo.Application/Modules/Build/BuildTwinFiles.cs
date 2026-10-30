using System.Text;
using System.Text.Json;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build;

/// <summary>What a file really is, from its first bytes — the client's name and declared type are never trusted.</summary>
public enum BuildFileKind
{
    Unknown,
    Ifc,
    IfcXml,
    Zip,
    Glb,
    GltfJson,
    Pdf,
    Png,
    Jpeg,
    Webp,
    OleCompound,   // .rvt (and old Office files)
    Dwg,
    Dxf,
    DxfBinary,
    Mp4,
    QuickTime,
    Webm,
    Las,
    E57,
}

/// <summary>A file refused (or a model rejected) with a user-readable Portuguese message and a stable code.</summary>
public sealed class BuildModelFileException : DomainException
{
    public BuildModelFileException(string message, string code) : base(message, code) { }
}

public static class BuildFileSignatures
{
    /// <summary>How many bytes <see cref="Sniff"/> needs to decide.</summary>
    public const int HeadLength = 2048;

    public static BuildFileKind Sniff(ReadOnlySpan<byte> head)
    {
        if (head.Length < 4) return BuildFileKind.Unknown;
        if (Starts(head, "glTF")) return head.Length >= 8 && BitConverter.ToUInt32(head[4..8]) == 2 ? BuildFileKind.Glb : BuildFileKind.Unknown;
        if (Starts(head, "%PDF-")) return BuildFileKind.Pdf;
        if (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 && head[4] == 0x0D
            && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A) return BuildFileKind.Png;
        if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return BuildFileKind.Jpeg;
        if (head.Length >= 12 && Starts(head, "RIFF") && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P')
            return BuildFileKind.Webp;
        if (head.Length >= 8 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0 && head[4] == 0xA1
            && head[5] == 0xB1 && head[6] == 0x1A && head[7] == 0xE1) return BuildFileKind.OleCompound;
        if (Starts(head, "AC10")) return BuildFileKind.Dwg;
        if (head[0] == 'P' && head[1] == 'K' && head[2] == 0x03 && head[3] == 0x04) return BuildFileKind.Zip;
        if (head.Length >= 8 && head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p')
            return head.Length >= 12 && head[8] == 'q' && head[9] == 't' ? BuildFileKind.QuickTime : BuildFileKind.Mp4;
        if (head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3) return BuildFileKind.Webm;
        if (Starts(head, "LASF")) return BuildFileKind.Las;
        if (Starts(head, "ASTM-E57")) return BuildFileKind.E57;
        if (Starts(head, "AutoCAD Binary DXF")) return BuildFileKind.DxfBinary;

        var text = Text(head);
        if (text.StartsWith("ISO-10303-21;", StringComparison.Ordinal)) return BuildFileKind.Ifc;
        if (text.StartsWith("<?xml", StringComparison.Ordinal) || text.StartsWith("<ifc", StringComparison.OrdinalIgnoreCase))
            return text.Contains("ifc", StringComparison.OrdinalIgnoreCase) ? BuildFileKind.IfcXml : BuildFileKind.Unknown;
        if (text.StartsWith('{')) return BuildFileKind.GltfJson;   // confirmed (asset.version) by BuildGltfReader
        if (LooksLikeAsciiDxf(text)) return BuildFileKind.Dxf;
        return BuildFileKind.Unknown;
    }

    private static bool Starts(ReadOnlySpan<byte> head, string ascii)
    {
        if (head.Length < ascii.Length) return false;
        for (var i = 0; i < ascii.Length; i++)
            if (head[i] != ascii[i]) return false;
        return true;
    }

    /// <summary>The head as text without a UTF-8 BOM and leading whitespace.</summary>
    private static string Text(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) head = head[3..];
        return Encoding.UTF8.GetString(head).TrimStart();
    }

    /// <summary>ASCII DXF: group code / value lines — the first pair is "0" / "SECTION" (comments "999" may come first).</summary>
    private static bool LooksLikeAsciiDxf(string text)
    {
        var lines = text.Split('\n', 12).Select(l => l.Trim()).ToList();
        for (var i = 0; i + 1 < lines.Count; i += 2)
        {
            if (lines[i] == "999") continue;
            return lines[i] == "0" && lines[i + 1] == "SECTION";
        }
        return false;
    }
}

/// <summary>How a model upload is handled, decided from the sniffed kind.</summary>
public sealed record BuildModelFormatDecision(
    BuildModelSourceFormat? Format,
    /// <summary>Recorded as a Rejected version (nothing stored) — known formats we cannot read (RVT/DWG).</summary>
    bool Rejected,
    string? Message,
    string Code,
    string Extension,
    string ContentType)
{
    public bool Accepted => Format is not null && !Rejected;
}

public static class BuildModelFormats
{
    public const string RvtDwgMessage =
        "Arquivos .rvt/.dwg precisam ser exportados para IFC (Revit: Arquivo → Exportar → IFC; AutoCAD/Civil: exportar IFC) ou GLB. "
        + "Conversão automática exige serviço externo, não incluído.";

    public static BuildModelFormatDecision Decide(BuildFileKind kind) => kind switch
    {
        BuildFileKind.Ifc         => Ok(BuildModelSourceFormat.Ifc, ".ifc", "application/x-step"),
        BuildFileKind.Glb         => Ok(BuildModelSourceFormat.Glb, ".glb", "model/gltf-binary"),
        BuildFileKind.GltfJson    => Ok(BuildModelSourceFormat.Gltf, ".gltf", "model/gltf+json"),
        BuildFileKind.Dxf         => Ok(BuildModelSourceFormat.Dxf, ".dxf", "image/vnd.dxf"),
        BuildFileKind.Pdf         => Ok(BuildModelSourceFormat.Pdf, ".pdf", "application/pdf"),
        BuildFileKind.Png         => Ok(BuildModelSourceFormat.Image, ".png", "image/png"),
        BuildFileKind.Jpeg        => Ok(BuildModelSourceFormat.Image, ".jpg", "image/jpeg"),
        BuildFileKind.Webp        => Ok(BuildModelSourceFormat.Image, ".webp", "image/webp"),
        BuildFileKind.OleCompound => new(BuildModelSourceFormat.Rvt, true, RvtDwgMessage, "model_format_rejected", "", ""),
        BuildFileKind.Dwg         => new(BuildModelSourceFormat.Dwg, true, RvtDwgMessage, "model_format_rejected", "", ""),
        BuildFileKind.Zip         => Refuse("Arquivos compactados (.ifczip/.zip) não são aceitos: descompacte e envie o .ifc.", "model_format_compressed"),
        BuildFileKind.IfcXml      => Refuse("ifcXML não é aceito: exporte o IFC no formato padrão (STEP, .ifc).", "model_format_unsupported"),
        BuildFileKind.DxfBinary   => Refuse("DXF binário não é aceito: salve o DXF em texto (ASCII).", "model_format_unsupported"),
        _ => Refuse("Formato não reconhecido. Envie IFC, GLB/glTF, DXF, PDF ou imagem (PNG/JPEG/WebP).", "model_format_unknown"),
    };

    private static BuildModelFormatDecision Ok(BuildModelSourceFormat f, string ext, string type) => new(f, false, null, "ok", ext, type);

    private static BuildModelFormatDecision Refuse(string message, string code) => new(null, false, message, code, "", "");
}

/// <summary>What a reality capture file is and the limit that applies (configurable per family).</summary>
public enum BuildCaptureFamily { Image, Video, PointCloud }

public sealed record BuildCaptureFile(BuildCaptureFamily Family, string Extension, string ContentType);

public static class BuildCaptureFormats
{
    public static BuildCaptureFile? Of(BuildFileKind kind) => kind switch
    {
        BuildFileKind.Jpeg      => new(BuildCaptureFamily.Image, ".jpg", "image/jpeg"),
        BuildFileKind.Png       => new(BuildCaptureFamily.Image, ".png", "image/png"),
        BuildFileKind.Webp      => new(BuildCaptureFamily.Image, ".webp", "image/webp"),
        BuildFileKind.Mp4       => new(BuildCaptureFamily.Video, ".mp4", "video/mp4"),
        BuildFileKind.QuickTime => new(BuildCaptureFamily.Video, ".mov", "video/quicktime"),
        BuildFileKind.Webm      => new(BuildCaptureFamily.Video, ".webm", "video/webm"),
        BuildFileKind.Las       => new(BuildCaptureFamily.PointCloud, ".las", "application/vnd.las"),
        BuildFileKind.E57       => new(BuildCaptureFamily.PointCloud, ".e57", "application/e57"),
        _ => null,
    };

    /// <summary>Which file families each capture type accepts (a drone flight may be photos or a video).</summary>
    public static bool Accepts(BuildRealityCaptureType type, BuildCaptureFamily family) => type switch
    {
        BuildRealityCaptureType.Photo or BuildRealityCaptureType.Photo360 => family == BuildCaptureFamily.Image,
        BuildRealityCaptureType.Video => family == BuildCaptureFamily.Video,
        BuildRealityCaptureType.Drone => family is BuildCaptureFamily.Image or BuildCaptureFamily.Video,
        BuildRealityCaptureType.PointCloud => family == BuildCaptureFamily.PointCloud,
        _ => false,
    };

    public static string Expected(BuildRealityCaptureType type) => type switch
    {
        BuildRealityCaptureType.Photo or BuildRealityCaptureType.Photo360 => "uma imagem JPEG, PNG ou WebP",
        BuildRealityCaptureType.Video => "um vídeo MP4, MOV ou WebM",
        BuildRealityCaptureType.Drone => "uma imagem (JPEG/PNG/WebP) ou um vídeo (MP4/MOV/WebM)",
        _ => "uma nuvem de pontos .las ou .e57",
    };
}

// ── Parsed model (format-independent result of the processors) ─────────────────

public sealed record BuildParsedZone(string Key, BuildZoneKind Kind, string? Name, string? ExternalId, string? ParentKey,
    decimal? Elevation, int Order);

public sealed record BuildParsedElement(string ExternalId, string Category, string? Name, string? TypeName, string? Material,
    string? ZoneKey, string? PropertiesJson);

public sealed record BuildParsedModel(
    string? Schema,
    BuildGeometrySource Geometry,
    IReadOnlyList<BuildParsedZone> Zones,
    IReadOnlyList<BuildParsedElement> Elements,
    string Message,
    string? LayersJson = null)
{
    public int StoreyCount => Zones.Count(z => z.Kind == BuildZoneKind.Storey);
}

/// <summary>
/// glTF 2.0 reading without fetching anything: GLB (JSON chunk only — the binary chunk is never loaded) or .gltf JSON whose
/// buffers/images are embedded data URIs. External URIs are refused (the server never downloads what a file points to).
/// Elements = nodes with a mesh (external id = the node name when unique, else "node:{index}"); zones = top-level scene
/// nodes that group others.
/// </summary>
public static class BuildGltfReader
{
    private const uint GlbMagic = 0x46546C67;   // "glTF"
    private const uint JsonChunk = 0x4E4F534A;  // "JSON"

    public sealed record Node(int Index, string? Name, bool HasMesh, string? Category, IReadOnlyList<int> Children);

    public sealed record Document(string Version, IReadOnlyList<Node> Nodes, IReadOnlyList<int> RootNodes);

    /// <summary>Reads the JSON chunk of a GLB stream (positioned at the start).</summary>
    public static Document ReadGlb(Stream stream)
    {
        var header = new byte[20];
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            throw Invalid("GLB incompleto.");
        if (BitConverter.ToUInt32(header, 0) != GlbMagic || BitConverter.ToUInt32(header, 4) != 2)
            throw Invalid("GLB inválido: só glTF 2.0 é aceito.");
        var jsonLength = BitConverter.ToUInt32(header, 12);
        if (BitConverter.ToUInt32(header, 16) != JsonChunk || jsonLength == 0 || jsonLength > 64 * 1024 * 1024)
            throw Invalid("GLB inválido: o primeiro bloco precisa ser o JSON do glTF.");
        var json = new byte[jsonLength];
        if (stream.ReadAtLeast(json, json.Length, throwOnEndOfStream: false) < json.Length) throw Invalid("GLB incompleto.");
        return ReadJson(json, isGlb: true);
    }

    public static Document ReadGlb(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        return ReadGlb(ms);
    }

    public static Document ReadJson(ReadOnlySpan<byte> json, bool isGlb = false)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            throw Invalid("O JSON do glTF é inválido.");
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("asset", out var asset) || asset.ValueKind != JsonValueKind.Object
                || !asset.TryGetProperty("version", out var ver) || ver.ValueKind != JsonValueKind.String
                || !(ver.GetString() ?? "").StartsWith('2'))
                throw Invalid("Arquivo glTF inválido: só glTF 2.0 é aceito.");

            EnsureEmbedded(root, "buffers", isGlb);
            EnsureEmbedded(root, "images", isGlb);

            var nodes = new List<Node>();
            if (root.TryGetProperty("nodes", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var n in arr.EnumerateArray())
                {
                    var name = n.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() : null;
                    var children = n.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array
                        ? ch.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out _)).Select(c => c.GetInt32()).ToList()
                        : [];
                    nodes.Add(new Node(i++, string.IsNullOrWhiteSpace(name) ? null : name.Trim(), n.TryGetProperty("mesh", out _),
                        CategoryOf(n, name), children));
                }
            }

            var roots = new List<int>();
            if (root.TryGetProperty("scenes", out var scenes) && scenes.ValueKind == JsonValueKind.Array && scenes.GetArrayLength() > 0)
            {
                var sceneIndex = root.TryGetProperty("scene", out var s) && s.TryGetInt32(out var si) && si >= 0 && si < scenes.GetArrayLength() ? si : 0;
                var scene = scenes[sceneIndex];
                if (scene.TryGetProperty("nodes", out var sn) && sn.ValueKind == JsonValueKind.Array)
                    roots.AddRange(sn.EnumerateArray().Where(x => x.TryGetInt32(out var v) && v >= 0 && v < nodes.Count).Select(x => x.GetInt32()));
            }
            if (roots.Count == 0)
            {
                var childSet = nodes.SelectMany(n => n.Children).ToHashSet();
                roots.AddRange(nodes.Where(n => !childSet.Contains(n.Index)).Select(n => n.Index));
            }
            return new Document(ver.GetString()!, nodes, roots.Distinct().ToList());
        }
    }

    /// <summary>Elements and zones of a glTF document (see the class summary).</summary>
    public static BuildParsedModel Extract(Document doc)
    {
        var parent = new Dictionary<int, int>();
        foreach (var n in doc.Nodes)
            foreach (var c in n.Children.Where(c => c >= 0 && c < doc.Nodes.Count && c != n.Index))
                parent.TryAdd(c, n.Index);

        var zoneRoots = doc.RootNodes.Where(i => doc.Nodes[i].Children.Count > 0).ToList();
        var zones = zoneRoots.Select((i, order) =>
        {
            var n = doc.Nodes[i];
            return new BuildParsedZone($"node:{i}", BuildZoneKind.Area, n.Name ?? $"Grupo {order + 1}", n.Name ?? $"node:{i}", null, null, order);
        }).ToList();
        var zoneSet = zoneRoots.ToHashSet();

        var meshNodes = doc.Nodes.Where(n => n.HasMesh).ToList();
        var nameCount = meshNodes.Where(n => n.Name is not null).GroupBy(n => n.Name!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var elements = meshNodes.Select(n =>
        {
            var id = n.Name is { } nm && nameCount[nm] == 1 && !nm.StartsWith("node:", StringComparison.Ordinal) && nm.Length <= 100
                ? nm
                : $"node:{n.Index}";
            var top = TopAncestor(n.Index, parent);
            return new BuildParsedElement(id, n.Category ?? "Mesh", n.Name, null, null,
                top != n.Index && zoneSet.Contains(top) ? $"node:{top}" : null, null);
        }).ToList();

        return new BuildParsedModel($"glTF {doc.Version}", BuildGeometrySource.Converted3D, zones, elements,
            elements.Count == 0
                ? "Arquivo 3D lido, mas sem malhas: nada para colorir no visualizador."
                : $"Arquivo 3D lido: {elements.Count} elemento(s). Os ids vêm dos nomes dos nós do arquivo.");
    }

    /// <summary>Every non-empty node name (what a viewer derivative is matched with).</summary>
    public static IReadOnlyList<string> NodeNames(Document doc)
        => doc.Nodes.Where(n => n.Name is not null).Select(n => n.Name!).ToList();

    private static int TopAncestor(int index, IReadOnlyDictionary<int, int> parent)
    {
        var current = index;
        for (var guard = 0; guard < 10_000 && parent.TryGetValue(current, out var p); guard++) current = p;
        return current;
    }

    private static string? CategoryOf(JsonElement node, string? name)
    {
        if (node.TryGetProperty("extras", out var extras) && extras.ValueKind == JsonValueKind.Object)
            foreach (var key in new[] { "category", "ifcClass", "ifcType", "type" })
                if (extras.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                    return v.GetString()!.Trim();
        return IfcClassPrefix(name);
    }

    /// <summary>"IfcWall_3hX…" → "IfcWall" (letters and digits after the "Ifc" prefix), else null.</summary>
    private static string? IfcClassPrefix(string? name)
    {
        if (name is null || name.Length < 4 || !name.StartsWith("Ifc", StringComparison.Ordinal) || !char.IsAsciiLetterUpper(name[3]))
            return null;
        var end = 3;
        while (end < name.Length && end < 64 && char.IsAsciiLetterOrDigit(name[end])) end++;
        return name[..end];
    }

    private static void EnsureEmbedded(JsonElement root, string property, bool isGlb)
    {
        if (!root.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array) return;
        foreach (var item in list.EnumerateArray())
        {
            if (!item.TryGetProperty("uri", out var uri))
            {
                if (property == "buffers" && !isGlb)
                    throw Invalid("glTF sem os dados embutidos: exporte como GLB ou com os buffers embutidos.");
                continue;
            }
            var value = uri.ValueKind == JsonValueKind.String ? uri.GetString() ?? "" : "";
            if (!value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                throw new BuildModelFileException(
                    "O glTF aponta para arquivos externos (.bin/texturas): exporte como GLB ou com os dados embutidos. O servidor não busca arquivos externos.",
                    "gltf_external_uri");
        }
    }

    private static BuildModelFileException Invalid(string message) => new(message, "model_file_invalid");
}

/// <summary>Layer names of an ASCII DXF (LAYER table entries and the layers used by entities) — the sheet's structure.</summary>
public static class BuildDxfReader
{
    public const int MaxLayers = 500;

    public static IReadOnlyList<string> Layers(TextReader reader)
    {
        var layers = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? previousValue = null;
        var inLayerEntry = false;
        for (var lineNo = 0; lineNo < 5_000_000; lineNo++)
        {
            var code = reader.ReadLine();
            var value = reader.ReadLine();
            if (code is null || value is null) break;
            code = code.Trim();
            value = value.Trim();
            if (code == "0")
            {
                inLayerEntry = value == "LAYER" && previousValue != "TABLE";
                if (value == "EOF") break;
            }
            if ((code == "8" || (code == "2" && inLayerEntry)) && value.Length is > 0 and <= 255 && seen.Add(value))
            {
                layers.Add(value);
                if (layers.Count >= MaxLayers) break;
            }
            previousValue = value;
        }
        return layers;
    }
}

/// <summary>How much of a viewer derivative's node names are elements of the version (prevents attaching a wrong file).</summary>
public static class BuildViewerMatch
{
    public const double DefaultMinRatio = 0.5;

    public sealed record Result(int Matched, int Total, double Ratio)
    {
        public bool Passes(double minRatio) => Matched > 0 && Ratio >= minRatio;
    }

    public static Result Compute(IEnumerable<string> nodeNames, IReadOnlySet<string> knownExternalIds)
    {
        var names = nodeNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0) return new Result(0, 0, 0);
        var matched = names.Count(knownExternalIds.Contains);
        return new Result(matched, names.Count, Math.Round((double)matched / names.Count, 4));
    }
}
