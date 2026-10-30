using System.Globalization;
using System.Text.Json;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Modules.Build;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Reads an IFC (STEP, IFC2X3 / IFC4) with xBIM Essentials' in-memory model — managed code only, no geometry engine, no
/// native dependency, works on Linux. Extracts:
///   - the spatial structure (Site / Building / Storey / Space) as zones, parent from IfcRelAggregates, storey elevation;
///   - the elements (every IfcElement except openings/voids) with GlobalId, class, name, type name (IfcRelDefinesByType),
///     first material (IfcRelAssociatesMaterial), containing zone (IfcRelContainedInSpatialStructure, through the
///     aggregation parent for parts such as stair flights) and a few base quantities (NetVolume, NetArea, Length, Count).
/// Nothing is computed from geometry: an IFC whose elements have no representation is reported as hierarchy only.
/// </summary>
public static class BuildIfcParser
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static BuildParsedModel Parse(Stream stream, int maxElements)
    {
        MemoryModel model;
        try
        {
            model = MemoryModel.OpenReadStep21(stream, (Xbim.Common.ReportProgressDelegate?)null, (IEnumerable<string>?)null, false, true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new BuildModelFileException("Não foi possível ler o arquivo IFC: ele parece corrompido ou fora do padrão STEP.",
                "ifc_unreadable");
        }

        using (model)
        {
            var schema = model.Header?.FileSchema?.Schemas?.FirstOrDefault()?.Trim().ToUpperInvariant();
            if (schema is null || !(schema.StartsWith("IFC2X3", StringComparison.Ordinal) || schema.StartsWith("IFC4", StringComparison.Ordinal)))
                throw new BuildModelFileException($"Esquema IFC não suportado ({schema ?? "desconhecido"}). Use IFC2X3 ou IFC4.", "ifc_schema_unsupported");

            // ── relations, read once ──
            var aggregateParent = new Dictionary<int, IIfcObjectDefinition>();
            foreach (var rel in model.Instances.OfType<IIfcRelAggregates>())
                foreach (var child in rel.RelatedObjects)
                    aggregateParent.TryAdd(child.EntityLabel, rel.RelatingObject);

            var containedIn = new Dictionary<int, IIfcSpatialElement>();
            foreach (var rel in model.Instances.OfType<IIfcRelContainedInSpatialStructure>())
                foreach (var p in rel.RelatedElements)
                    containedIn.TryAdd(p.EntityLabel, rel.RelatingStructure);

            var typeName = new Dictionary<int, string>();
            foreach (var rel in model.Instances.OfType<IIfcRelDefinesByType>())
            {
                var name = Text(rel.RelatingType?.Name);
                if (name is null) continue;
                foreach (var o in rel.RelatedObjects) typeName.TryAdd(o.EntityLabel, name);
            }

            var material = new Dictionary<int, string>();
            foreach (var rel in model.Instances.OfType<IIfcRelAssociatesMaterial>())
            {
                var name = MaterialName(rel.RelatingMaterial);
                if (name is null) continue;
                foreach (var o in rel.RelatedObjects) material.TryAdd(o.EntityLabel, name);
            }

            var quantities = new Dictionary<int, Dictionary<string, double>>();
            foreach (var rel in model.Instances.OfType<IIfcRelDefinesByProperties>())
            {
                if (rel.RelatingPropertyDefinition is not IIfcElementQuantity qto) continue;
                var values = QuantityValues(qto);
                if (values.Count == 0) continue;
                foreach (var o in rel.RelatedObjects)
                {
                    if (!quantities.TryGetValue(o.EntityLabel, out var bag)) quantities[o.EntityLabel] = bag = new();
                    foreach (var (k, v) in values) bag.TryAdd(k, v);
                }
            }

            // ── zones (spatial structure) ──
            var spatial = model.Instances.OfType<IIfcSpatialStructureElement>()
                .Where(s => s is IIfcSite or IIfcBuilding or IIfcBuildingStorey or IIfcSpace)
                .ToList();
            var spatialLabels = spatial.ToDictionary(s => s.EntityLabel, s => Gid(s));
            var zones = new List<BuildParsedZone>();
            var order = 0;
            foreach (var s in spatial.OrderBy(KindOf).ThenBy(s => s is IIfcBuildingStorey st ? Number(st.Elevation) ?? 0 : 0).ThenBy(s => Text(s.Name)))
            {
                string? parentKey = null;
                var parent = aggregateParent.GetValueOrDefault(s.EntityLabel);
                if (parent is not null && spatialLabels.TryGetValue(parent.EntityLabel, out var pk)) parentKey = pk;
                else if (containedIn.TryGetValue(s.EntityLabel, out var c) && spatialLabels.TryGetValue(c.EntityLabel, out var ck)) parentKey = ck;
                var elevation = s is IIfcBuildingStorey storey && Number(storey.Elevation) is { } e ? (decimal?)Math.Round((decimal)e, 3) : null;
                var gid = Gid(s);
                zones.Add(new BuildParsedZone(gid, KindOf(s), Text(s.Name) ?? Text(s.LongName), gid, parentKey, elevation, order++));
            }

            // ── elements ──
            var elements = new List<BuildParsedElement>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var withGeometry = 0;
            foreach (var e in model.Instances.OfType<IIfcElement>())
            {
                if (e is IIfcFeatureElementSubtraction) continue;   // openings are voids, not things to build
                var gid = Gid(e);
                if (string.IsNullOrWhiteSpace(gid) || !seen.Add(gid)) continue;
                if (elements.Count >= maxElements)
                    throw new BuildModelFileException(
                        $"O modelo tem mais de {maxElements} elementos — divida-o por disciplina ou pavimento.", "ifc_too_many_elements");
                if (e.Representation is not null) withGeometry++;
                elements.Add(new BuildParsedElement(
                    gid,
                    Category(e.ExpressType.Name),
                    Text(e.Name),
                    typeName.GetValueOrDefault(e.EntityLabel),
                    material.GetValueOrDefault(e.EntityLabel),
                    ZoneOf(e, containedIn, aggregateParent, spatialLabels),
                    Properties(quantities.GetValueOrDefault(e.EntityLabel))));
            }

            var geometry = withGeometry > 0 ? BuildGeometrySource.NativeBim : BuildGeometrySource.AssistedMapping;
            var storeys = zones.Count(z => z.Kind == BuildZoneKind.Storey);
            var message = geometry == BuildGeometrySource.NativeBim
                ? $"IFC lido ({schema}): {elements.Count} elemento(s), {storeys} pavimento(s)."
                : $"IFC lido ({schema}): {elements.Count} elemento(s), {storeys} pavimento(s), mas sem geometria — só a hierarquia.";
            return new BuildParsedModel(schema, geometry, zones, elements, message);
        }
    }

    private static BuildZoneKind KindOf(IIfcSpatialStructureElement s) => s switch
    {
        IIfcSite => BuildZoneKind.Site,
        IIfcBuilding => BuildZoneKind.Building,
        IIfcBuildingStorey => BuildZoneKind.Storey,
        _ => BuildZoneKind.Space,
    };

    private static string? ZoneOf(IIfcElement e, IReadOnlyDictionary<int, IIfcSpatialElement> containedIn,
        IReadOnlyDictionary<int, IIfcObjectDefinition> aggregateParent, IReadOnlyDictionary<int, string> spatialLabels)
    {
        var label = e.EntityLabel;
        for (var guard = 0; guard < 16; guard++)
        {
            if (containedIn.TryGetValue(label, out var s) && spatialLabels.TryGetValue(s.EntityLabel, out var key)) return key;
            if (!aggregateParent.TryGetValue(label, out var parent)) return null;
            if (spatialLabels.TryGetValue(parent.EntityLabel, out var direct)) return direct;
            label = parent.EntityLabel;
        }
        return null;
    }

    /// <summary>"IfcWallStandardCase" → "IfcWall" (IFC2X3 subtypes that mean the same element).</summary>
    public static string Category(string expressName)
    {
        foreach (var suffix in new[] { "StandardCase", "ElementedCase" })
            if (expressName.EndsWith(suffix, StringComparison.Ordinal) && expressName.Length > suffix.Length + 3)
                return expressName[..^suffix.Length];
        return expressName;
    }

    private static Dictionary<string, double> QuantityValues(IIfcElementQuantity qto)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var q in qto.Quantities)
        {
            var name = Text(q.Name) ?? string.Empty;
            switch (q)
            {
                case IIfcQuantityVolume v when name is "NetVolume":
                    values.TryAdd("netVolume", (double)v.VolumeValue); break;
                case IIfcQuantityVolume v when name is "GrossVolume":
                    values.TryAdd("grossVolume", (double)v.VolumeValue); break;
                case IIfcQuantityArea a when name is "NetArea" or "NetSideArea" or "NetFloorArea" or "NetFootprintArea":
                    values.TryAdd("netArea", (double)a.AreaValue); break;
                case IIfcQuantityArea a when name is "GrossArea" or "GrossSideArea" or "GrossFloorArea" or "GrossFootprintArea":
                    values.TryAdd("grossArea", (double)a.AreaValue); break;
                case IIfcQuantityLength l when name is "Length":
                    values.TryAdd("length", (double)l.LengthValue); break;
                case IIfcQuantityCount c when name is "Count":
                    values.TryAdd("count", (double)c.CountValue); break;
            }
        }
        return values;
    }

    /// <summary>Output key → quantity names in order of preference (net first, gross as fallback).</summary>
    private static readonly (string Key, string[] Sources)[] QuantityPicks =
    [
        ("netVolume", ["netVolume", "grossVolume"]),
        ("netArea", ["netArea", "grossArea"]),
        ("length", ["length"]),
        ("count", ["count"]),
    ];

    /// <summary>Selected quantities as compact JSON (net preferred, gross as fallback), or null.</summary>
    private static string? Properties(Dictionary<string, double>? q)
    {
        if (q is null || q.Count == 0) return null;
        var bag = new Dictionary<string, double>();
        foreach (var (key, sources) in QuantityPicks)
        {
            var value = sources.Select(f => q.TryGetValue(f, out var v) && double.IsFinite(v) ? v : (double?)null)
                .FirstOrDefault(v => v.HasValue);
            if (value is { } found) bag[key] = Math.Round(found, 4);
        }
        if (bag.Count == 0) return null;
        var json = JsonSerializer.Serialize(bag, Json);
        return json.Length <= BuildModelElement.MaxPropertiesJson ? json : null;
    }

    private static string? MaterialName(IIfcMaterialSelect? select) => select switch
    {
        IIfcMaterial m => Text(m.Name),
        IIfcMaterialLayerSetUsage u => u.ForLayerSet?.MaterialLayers.Select(l => Text(l.Material?.Name)).FirstOrDefault(n => n is not null),
        IIfcMaterialLayerSet s => s.MaterialLayers.Select(l => Text(l.Material?.Name)).FirstOrDefault(n => n is not null),
        IIfcMaterialLayer l => Text(l.Material?.Name),
        IIfcMaterialList list => list.Materials.Select(x => Text(x.Name)).FirstOrDefault(n => n is not null),
        IIfcMaterialConstituentSet cs => cs.MaterialConstituents.Select(c => Text(c.Material?.Name)).FirstOrDefault(n => n is not null),
        IIfcMaterialProfileSetUsage pu => pu.ForProfileSet?.MaterialProfiles.Select(p => Text(p.Material?.Name)).FirstOrDefault(n => n is not null),
        IIfcMaterialProfileSet ps => ps.MaterialProfiles.Select(p => Text(p.Material?.Name)).FirstOrDefault(n => n is not null),
        _ => null,
    };

    private static string Gid(IIfcRoot r) => r.GlobalId.ToString().Trim();

    private static string? Text(object? label)
    {
        var s = label?.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static double? Number(object? measure)
        => measure is null ? null
            : double.TryParse(Convert.ToString(measure, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
              && double.IsFinite(v) ? v : null;
}
