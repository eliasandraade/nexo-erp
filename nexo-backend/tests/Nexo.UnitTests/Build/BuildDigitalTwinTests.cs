using System.Text;
using System.Text.Json;
using FluentAssertions;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Modules.Build;
using static Nexo.Application.Modules.Build.BuildTwinStateEngine;

namespace Nexo.UnitTests.Build;

/// <summary>Wave 4 (Digital Twin): file detection, glTF/DXF/IFC reading, viewer derivative match and the 4D state engine.</summary>
public class BuildDigitalTwinTests
{
    // ── Test files ────────────────────────────────────────────────────────────

    internal static string Gid(string prefix) => prefix.PadRight(22, '0');

    internal static byte[] Ifc()
    {
        using var s = typeof(BuildDigitalTwinTests).Assembly.GetManifestResourceStream("casa-teste.ifc")!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>A minimal valid GLB: one mesh node per name (all under a root group), JSON chunk + a small BIN chunk.</summary>
    internal static byte[] Glb(IEnumerable<string> nodeNames, string? rootName = "Raiz")
    {
        var names = nodeNames.ToList();
        var nodes = new List<object>();
        var children = Enumerable.Range(1, names.Count).ToArray();
        nodes.Add(new { name = rootName, children });
        nodes.AddRange(names.Select(n => (object)new { name = n, mesh = 0 }));
        var gltf = new
        {
            asset = new { version = "2.0" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes,
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0 } } } } },
            accessors = new[] { new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3", max = new[] { 1, 1, 0 }, min = new[] { 0, 0, 0 } } },
            bufferViews = new[] { new { buffer = 0, byteLength = 36 } },
            buffers = new[] { new { byteLength = 36 } },
        };
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(gltf));
        var jsonPadded = Pad(json, 0x20);
        var bin = Pad(new byte[36], 0);
        var total = 12 + 8 + jsonPadded.Length + 8 + bin.Length;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)total);
        w.Write((uint)jsonPadded.Length); w.Write(0x4E4F534Au); w.Write(jsonPadded);
        w.Write((uint)bin.Length); w.Write(0x004E4942u); w.Write(bin);
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] Pad(byte[] data, byte filler)
    {
        var len = (data.Length + 3) / 4 * 4;
        var result = Enumerable.Repeat(filler, len).ToArray();
        data.CopyTo(result, 0);
        return result;
    }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    // ── Format detection ──────────────────────────────────────────────────────

    [Fact]
    public void Each_format_is_recognized_by_its_first_bytes()
    {
        BuildFileSignatures.Sniff(Ifc()).Should().Be(BuildFileKind.Ifc);
        BuildFileSignatures.Sniff(Glb(["a"])).Should().Be(BuildFileKind.Glb);
        BuildFileSignatures.Sniff(Ascii("  {\"asset\":{\"version\":\"2.0\"}}")).Should().Be(BuildFileKind.GltfJson);
        BuildFileSignatures.Sniff(Ascii("%PDF-1.7\n...")).Should().Be(BuildFileKind.Pdf);
        BuildFileSignatures.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0]).Should().Be(BuildFileKind.Png);
        BuildFileSignatures.Sniff([0xFF, 0xD8, 0xFF, 0xE0, 0, 0]).Should().Be(BuildFileKind.Jpeg);
        BuildFileSignatures.Sniff([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0]).Should().Be(BuildFileKind.OleCompound);
        BuildFileSignatures.Sniff(Ascii("AC1032\0\0\0\0")).Should().Be(BuildFileKind.Dwg);
        BuildFileSignatures.Sniff(Ascii("  0\r\nSECTION\r\n  2\r\nHEADER\r\n")).Should().Be(BuildFileKind.Dxf);
        BuildFileSignatures.Sniff(Ascii("999\nfeito no CAD\n0\nSECTION\n2\nENTITIES\n")).Should().Be(BuildFileKind.Dxf);
        BuildFileSignatures.Sniff([0x50, 0x4B, 0x03, 0x04, 0, 0]).Should().Be(BuildFileKind.Zip);
        BuildFileSignatures.Sniff([0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m']).Should().Be(BuildFileKind.Mp4);
        BuildFileSignatures.Sniff([0, 0, 0, 0x14, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'q', (byte)'t', (byte)' ', (byte)' ']).Should().Be(BuildFileKind.QuickTime);
        BuildFileSignatures.Sniff([0x1A, 0x45, 0xDF, 0xA3, 0, 0]).Should().Be(BuildFileKind.Webm);
        BuildFileSignatures.Sniff(Ascii("LASF\0\0\0\0")).Should().Be(BuildFileKind.Las);
        BuildFileSignatures.Sniff(Ascii("ASTM-E57\0\0")).Should().Be(BuildFileKind.E57);
        BuildFileSignatures.Sniff(Ascii("hello world, not a model")).Should().Be(BuildFileKind.Unknown);
        // a file named .ifc that is really a PNG is a PNG
        BuildFileSignatures.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]).Should().NotBe(BuildFileKind.Ifc);
    }

    [Fact]
    public void Rvt_and_dwg_are_rejected_with_the_export_instructions_and_never_accepted()
    {
        foreach (var kind in new[] { BuildFileKind.OleCompound, BuildFileKind.Dwg })
        {
            var d = BuildModelFormats.Decide(kind);
            d.Accepted.Should().BeFalse();
            d.Rejected.Should().BeTrue();
            d.Code.Should().Be("model_format_rejected");
            d.Message.Should().Contain(".rvt/.dwg").And.Contain("IFC").And.Contain("GLB").And.Contain("serviço externo");
        }
        BuildModelFormats.Decide(BuildFileKind.Ifc).Should().Match<BuildModelFormatDecision>(d => d.Accepted && d.Format == BuildModelSourceFormat.Ifc);
        BuildModelFormats.Decide(BuildFileKind.Dxf).Format.Should().Be(BuildModelSourceFormat.Dxf);
        var zip = BuildModelFormats.Decide(BuildFileKind.Zip);
        (zip.Accepted || zip.Rejected).Should().BeFalse("an ifczip is refused, not recorded");
        zip.Code.Should().Be("model_format_compressed");
        BuildModelFormats.Decide(BuildFileKind.Unknown).Code.Should().Be("model_format_unknown");
    }

    [Fact]
    public void Reality_capture_types_accept_only_their_media_family()
    {
        BuildCaptureFormats.Of(BuildFileKind.Mp4)!.Family.Should().Be(BuildCaptureFamily.Video);
        BuildCaptureFormats.Of(BuildFileKind.Las)!.Family.Should().Be(BuildCaptureFamily.PointCloud);
        BuildCaptureFormats.Of(BuildFileKind.Pdf).Should().BeNull();
        BuildCaptureFormats.Accepts(BuildRealityCaptureType.Video, BuildCaptureFamily.Video).Should().BeTrue();
        BuildCaptureFormats.Accepts(BuildRealityCaptureType.Video, BuildCaptureFamily.Image).Should().BeFalse();
        BuildCaptureFormats.Accepts(BuildRealityCaptureType.Drone, BuildCaptureFamily.Image).Should().BeTrue();
        BuildCaptureFormats.Accepts(BuildRealityCaptureType.Photo360, BuildCaptureFamily.Video).Should().BeFalse();
        BuildCaptureFormats.Accepts(BuildRealityCaptureType.PointCloud, BuildCaptureFamily.PointCloud).Should().BeTrue();
    }

    // ── glTF ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Glb_nodes_become_elements_and_the_root_group_a_zone()
    {
        var doc = BuildGltfReader.ReadGlb(Glb(["IfcWall_A", "IfcSlab_B", "Dup", "Dup"]));
        var model = BuildGltfReader.Extract(doc);
        model.Geometry.Should().Be(BuildGeometrySource.Converted3D);
        model.Zones.Should().ContainSingle().Which.Name.Should().Be("Raiz");
        model.Elements.Select(e => e.ExternalId).Should().BeEquivalentTo(["IfcWall_A", "IfcSlab_B", "node:3", "node:4"],
            "duplicated names fall back to the node index");
        model.Elements.Single(e => e.ExternalId == "IfcWall_A").Category.Should().Be("IfcWall");
        model.Elements.Single(e => e.ExternalId == "node:3").Category.Should().Be("Mesh");
        model.Elements.Should().OnlyContain(e => e.ZoneKey == "node:0");
    }

    [Fact]
    public void Gltf_pointing_to_external_files_is_refused_and_embedded_data_is_accepted()
    {
        var external = Ascii("""{"asset":{"version":"2.0"},"buffers":[{"uri":"model.bin","byteLength":10}],"nodes":[{"name":"a","mesh":0}]}""");
        var act = () => BuildGltfReader.ReadJson(external);
        act.Should().Throw<BuildModelFileException>().Which.Code.Should().Be("gltf_external_uri");

        var remote = Ascii("""{"asset":{"version":"2.0"},"images":[{"uri":"https://example.test/t.png"}]}""");
        ((Action)(() => BuildGltfReader.ReadJson(remote))).Should().Throw<BuildModelFileException>().Which.Code.Should().Be("gltf_external_uri");

        var embedded = Ascii("""{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,AAAA","byteLength":3}],"nodes":[{"name":"a","mesh":0}]}""");
        BuildGltfReader.Extract(BuildGltfReader.ReadJson(embedded)).Elements.Should().ContainSingle(e => e.ExternalId == "a");

        ((Action)(() => BuildGltfReader.ReadJson(Ascii("""{"asset":{"version":"1.0"}}""")))).Should().Throw<BuildModelFileException>();
        ((Action)(() => BuildGltfReader.ReadJson(Ascii("{not json")))).Should().Throw<BuildModelFileException>();
    }

    [Fact]
    public void Viewer_derivative_must_mostly_match_the_known_element_ids()
    {
        var known = new HashSet<string>(["A", "B", "C", "D"]);
        var good = BuildViewerMatch.Compute(["A", "B", "C", "Raiz"], known);
        good.Matched.Should().Be(3);
        good.Total.Should().Be(4);
        good.Ratio.Should().Be(0.75);
        good.Passes(0.5).Should().BeTrue();

        BuildViewerMatch.Compute(["X", "Y", "Z", "A"], known).Passes(0.5).Should().BeFalse("only 25% are elements of this version");
        BuildViewerMatch.Compute([], known).Passes(0.5).Should().BeFalse();
        BuildViewerMatch.Compute(["A", "A", "  "], known).Should().Be(new BuildViewerMatch.Result(1, 1, 1));
    }

    // ── DXF ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Dxf_layers_are_read_from_the_layer_table_and_the_entities()
    {
        const string dxf = "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n0\nLAYER\n2\nPAREDES\n70\n0\n0\nLAYER\n2\nCOTAS\n0\nENDTAB\n0\nENDSEC\n"
            + "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nPAREDES\n0\nLINE\n8\nESQUADRIAS\n0\nENDSEC\n0\nEOF\n";
        BuildDxfReader.Layers(new StringReader(dxf)).Should().Equal("PAREDES", "COTAS", "ESQUADRIAS");
    }

    // ── IFC (xBIM, managed) ───────────────────────────────────────────────────

    [Fact]
    public void Ifc4_hierarchy_elements_types_materials_and_quantities_are_extracted()
    {
        using var stream = new MemoryStream(Ifc());
        var model = BuildIfcParser.Parse(stream, 1000);

        model.Schema.Should().Be("IFC4");
        model.Geometry.Should().Be(BuildGeometrySource.NativeBim);
        model.Zones.Select(z => (z.Kind, z.ExternalId)).Should().BeEquivalentTo(new[]
        {
            (BuildZoneKind.Site, Gid("Site")), (BuildZoneKind.Building, Gid("Building")),
            (BuildZoneKind.Storey, Gid("StoreyT")), (BuildZoneKind.Storey, Gid("Storey1")), (BuildZoneKind.Space, Gid("SpaceSala")),
        });
        model.StoreyCount.Should().Be(2);
        var terreo = model.Zones.Single(z => z.ExternalId == Gid("StoreyT"));
        terreo.Name.Should().Be("Térreo");
        terreo.ParentKey.Should().Be(Gid("Building"));
        model.Zones.Single(z => z.ExternalId == Gid("Storey1")).Elevation.Should().Be(3m);
        model.Zones.Single(z => z.Kind == BuildZoneKind.Space).ParentKey.Should().Be(Gid("StoreyT"));

        model.Elements.Should().HaveCount(7);
        var wall = model.Elements.Single(e => e.ExternalId == Gid("WallT1"));
        wall.Category.Should().Be("IfcWall");
        wall.Name.Should().Be("Parede T1");
        wall.TypeName.Should().Be("Alvenaria 14cm");
        wall.Material.Should().Be("Bloco cerâmico");
        wall.ZoneKey.Should().Be(Gid("StoreyT"));
        using (var props = JsonDocument.Parse(wall.PropertiesJson!))
        {
            props.RootElement.GetProperty("netVolume").GetDouble().Should().Be(2.4);
            props.RootElement.GetProperty("netArea").GetDouble().Should().Be(12);
            props.RootElement.GetProperty("length").GetDouble().Should().Be(4);
        }
        model.Elements.Count(e => e.Category == "IfcWall" && e.ZoneKey == Gid("StoreyT")).Should().Be(3);
        model.Elements.Single(e => e.ExternalId == Gid("Slab1")).ZoneKey.Should().Be(Gid("Storey1"));
    }

    [Fact]
    public void Garbage_with_an_ifc_header_is_refused_with_a_readable_message()
    {
        using var stream = new MemoryStream(Ascii("ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC9'));\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n"));
        var act = () => BuildIfcParser.Parse(stream, 1000);
        act.Should().Throw<BuildModelFileException>().Which.Message.Should().NotBeNullOrWhiteSpace();
        BuildIfcParser.Category("IfcWallStandardCase").Should().Be("IfcWall");
        BuildIfcParser.Category("IfcSlab").Should().Be("IfcSlab");
    }

    // ── Domain guards ─────────────────────────────────────────────────────────

    [Fact]
    public void Zone_outline_and_simplified_box_are_validated_and_never_invented()
    {
        var tenant = Guid.NewGuid();
        var project = Guid.NewGuid();
        var sheet = Guid.NewGuid();
        var z = BuildModelZone.Manual(tenant, project, BuildZoneKind.Floor, "Térreo", 0, null,
            [new(0, 0), new(1, 0), new(1, 0.5)], sheet, null, Guid.NewGuid());
        z.Outline().Should().HaveCount(3);
        z.Box().Should().BeNull("no box unless the user typed one");

        ((Action)(() => BuildModelZone.Manual(tenant, project, BuildZoneKind.Floor, "x", 0, null, [new(0, 0), new(1.2, 0), new(1, 1)], sheet, null, Guid.NewGuid())))
            .Should().Throw<DomainException>().Which.Code.Should().Be("invalid_outline");
        ((Action)(() => BuildModelZone.Manual(tenant, project, BuildZoneKind.Floor, "x", 0, null,
                Enumerable.Range(0, 201).Select(i => new BuildZonePoint(i / 300.0, 0.5)).ToList(), sheet, null, Guid.NewGuid())))
            .Should().Throw<DomainException>();
        ((Action)(() => BuildModelZone.Manual(tenant, project, BuildZoneKind.Floor, "x", 0, null, [new(0, 0), new(1, 0), new(1, 1)], null, null, Guid.NewGuid())))
            .Should().Throw<DomainException>().Which.Code.Should().Be("outline_needs_sheet");
        ((Action)(() => BuildModelZone.Manual(tenant, project, BuildZoneKind.Storey, "x", 0, null, null, null, null, Guid.NewGuid())))
            .Should().Throw<DomainException>("Storey is a model zone kind, not a manual one");
        ((Action)(() => BuildModelZone.Manual(tenant, project, BuildZoneKind.Room, "x", 0, null, null, null, new BuildSimplifiedBox(0, 3, 3), Guid.NewGuid())))
            .Should().Throw<DomainException>().Which.Code.Should().Be("invalid_simplified_box");
        BuildModelZone.Manual(tenant, project, BuildZoneKind.Room, "Sala", 0, null, null, null, new BuildSimplifiedBox(4.5m, 3.2m, 2.8m), Guid.NewGuid())
            .Box().Should().Be(new BuildSimplifiedBox(4.5m, 3.2m, 2.8m, 0m));
    }

    [Fact]
    public void Discipline_parsing_ignores_accents_and_case()
    {
        BuildTwinParse.Enum<BuildModelDiscipline>("Hidráulica", "Disciplina").Should().Be(BuildModelDiscipline.Hidraulica);
        BuildTwinParse.Enum<BuildModelDiscipline>("elétrica", "Disciplina").Should().Be(BuildModelDiscipline.Eletrica);
        ((Action)(() => BuildTwinParse.Enum<BuildModelDiscipline>("3", "Disciplina"))).Should().Throw<DomainException>();
        ((Action)(() => BuildTwinParse.Enum<BuildModelDiscipline>("Paisagismo", "Disciplina"))).Should().Throw<DomainException>();
    }

    // ── 4D state engine ───────────────────────────────────────────────────────

    private static readonly DateOnly Today = new(2026, 6, 30);
    private static readonly Guid Version = Guid.NewGuid();

    private static StageInput Stage(Guid id, DateOnly? plannedEnd, params (DateOnly Date, int Percent)[] events)
        => new(id, plannedEnd, Blocked: false,
            events.Select((e, i) => (e.Date, new DateTime(2026, 1, 1).AddMinutes(i), e.Percent)).ToList(),
            events.Length == 0 ? 0 : events[^1].Percent);

    [Fact]
    public void Stage_state_follows_its_progress_history_at_the_date()
    {
        var s = Stage(Guid.NewGuid(), new DateOnly(2026, 5, 31), (new DateOnly(2026, 4, 10), 50), (new DateOnly(2026, 5, 20), 100));
        StageStateAt(s, new DateOnly(2026, 4, 1), Today).Should().Be(BuildElementState.NotStarted);
        StageStateAt(s, new DateOnly(2026, 4, 10), Today).Should().Be(BuildElementState.InProgress);
        StageStateAt(s, new DateOnly(2026, 5, 20), Today).Should().Be(BuildElementState.Completed);
        StagePercentAt(s, new DateOnly(2026, 5, 1), Today).Should().Be(50);

        var late = Stage(Guid.NewGuid(), new DateOnly(2026, 5, 31), (new DateOnly(2026, 4, 10), 50));
        StageStateAt(late, new DateOnly(2026, 5, 31), Today).Should().Be(BuildElementState.InProgress, "the planned end day itself is not late");
        StageStateAt(late, new DateOnly(2026, 6, 1), Today).Should().Be(BuildElementState.Delayed);

        StageStateAt(late with { Blocked = true }, new DateOnly(2026, 6, 1), Today).Should().Be(BuildElementState.Blocked);
        StageStateAt(s with { Blocked = true }, new DateOnly(2026, 6, 1), Today).Should().Be(BuildElementState.Completed, "100% wins over blocked");

        var legacy = new StageInput(Guid.NewGuid(), null, false, [], 40);
        StagePercentAt(legacy, Today, Today).Should().Be(40, "no history: the current percent from today on");
        StagePercentAt(legacy, Today.AddDays(-1), Today).Should().Be(0, "no history: nothing is invented for the past");
    }

    [Fact]
    public void Element_binding_beats_zone_which_beats_category_and_unbound_is_null()
    {
        var alvenaria = Guid.NewGuid();
        var estrutura = Guid.NewGuid();
        var acabamento = Guid.NewGuid();
        var storey = Guid.NewGuid();
        var space = Guid.NewGuid();
        var stages = new[]
        {
            Stage(alvenaria, new DateOnly(2026, 12, 1), (new DateOnly(2026, 6, 1), 30)),
            Stage(estrutura, new DateOnly(2026, 12, 1), (new DateOnly(2026, 6, 1), 100)),
            Stage(acabamento, new DateOnly(2026, 12, 1)),
        };
        var elements = new[]
        {
            new ElementInput("w1", "IfcWall", space, Version),     // zone (through the space's parent storey) and category; element binding wins
            new ElementInput("w2", "IfcWall", space, Version),     // zone (ancestor) beats category
            new ElementInput("w3", "IfcWall", null, Version),      // only category
            new ElementInput("d1", "IfcDoor", null, Version),      // nothing
        };
        var parents = new Dictionary<Guid, Guid?> { [storey] = null, [space] = storey };
        var bindings = new[]
        {
            new BindingInput(acabamento, BuildBindingScope.Element, "w1", null, null, Version),
            new BindingInput(estrutura, BuildBindingScope.Zone, null, storey, null, null),
            new BindingInput(alvenaria, BuildBindingScope.Category, null, null, "ifcwall", null),
        };

        var r = Compute(Today, Today, elements, parents, stages, bindings, []);
        var byId = r.Elements.ToDictionary(e => e.ExternalId);
        byId["w1"].Should().Be(new ElementState("w1", BuildElementState.NotStarted, acabamento, false));
        byId["w2"].Should().Be(new ElementState("w2", BuildElementState.Completed, estrutura, false));
        byId["w3"].Should().Be(new ElementState("w3", BuildElementState.InProgress, alvenaria, false));
        byId["d1"].State.Should().BeNull();
        r.Unbound.Should().Be(1);
        r.Counts["InProgress"].Should().Be(1);
        r.States.Should().NotContainKey("Blocked");
        r.States["Completed"].Should().Equal("w2");
        r.ZoneStates["Completed"].Should().BeEquivalentTo([storey, space], "a zone takes the state of the nearest bound zone");
    }

    [Fact]
    public void Several_stages_on_the_same_element_take_the_latest_planned_end_and_category_bindings_respect_their_version()
    {
        var early = Guid.NewGuid();
        var late = Guid.NewGuid();
        var undated = Guid.NewGuid();
        var stages = new[]
        {
            Stage(early, new DateOnly(2026, 3, 1), (new DateOnly(2026, 2, 1), 100)),
            Stage(late, new DateOnly(2026, 9, 1), (new DateOnly(2026, 6, 1), 20)),
            Stage(undated, null, (new DateOnly(2026, 6, 1), 90)),
        };
        var other = Guid.NewGuid();
        var r = Compute(Today, Today,
            [new ElementInput("x", "IfcSlab", null, Version), new ElementInput("y", "IfcSlab", null, other)],
            new Dictionary<Guid, Guid?>(), stages,
            [
                new BindingInput(early, BuildBindingScope.Element, "x", null, null, Version),
                new BindingInput(late, BuildBindingScope.Element, "x", null, null, Version),
                new BindingInput(undated, BuildBindingScope.Element, "x", null, null, Version),
                new BindingInput(early, BuildBindingScope.Category, null, null, "IfcSlab", Version),
            ], []);
        r.Elements.Single(e => e.ExternalId == "x").StageId.Should().Be(late);
        r.Elements.Single(e => e.ExternalId == "x").State.Should().Be(BuildElementState.InProgress);
        r.Elements.Single(e => e.ExternalId == "y").State.Should().BeNull("the category binding is limited to another version");
    }

    [Fact]
    public void An_explicit_event_overrides_the_derived_state_from_its_date_on()
    {
        var stage = Guid.NewGuid();
        var stages = new[] { Stage(stage, new DateOnly(2026, 12, 1), (new DateOnly(2026, 6, 1), 50)) };
        var bindings = new[] { new BindingInput(stage, BuildBindingScope.Element, "w", null, null, Version) };
        var events = new[]
        {
            new EventInput("w", BuildElementState.NeedsReview, new DateOnly(2026, 6, 10), new DateTime(2026, 6, 10, 12, 0, 0)),
            new EventInput("w", BuildElementState.Completed, new DateOnly(2026, 6, 20), new DateTime(2026, 6, 20, 9, 0, 0)),
            new EventInput("w", BuildElementState.Blocked, new DateOnly(2026, 6, 20), new DateTime(2026, 6, 20, 18, 0, 0)),
            new EventInput("u", BuildElementState.Completed, new DateOnly(2026, 6, 1), new DateTime(2026, 6, 1)),
        };
        var elements = new[] { new ElementInput("w", "IfcWall", null, Version), new ElementInput("u", "IfcBeam", null, Version) };

        State(new DateOnly(2026, 6, 5)).Should().Be(new ElementState("w", BuildElementState.InProgress, stage, false));
        State(new DateOnly(2026, 6, 15)).Should().Be(new ElementState("w", BuildElementState.NeedsReview, stage, true));
        State(new DateOnly(2026, 6, 25)).Should().Be(new ElementState("w", BuildElementState.Blocked, stage, true), "same day: the latest recorded wins");
        Compute(new DateOnly(2026, 6, 5), Today, elements, new Dictionary<Guid, Guid?>(), stages, bindings, events)
            .Elements.Single(e => e.ExternalId == "u").State.Should().Be(BuildElementState.Completed, "an explicit event counts even without a binding");

        ElementState State(DateOnly d)
            => Compute(d, Today, elements, new Dictionary<Guid, Guid?>(), stages, bindings, events).Elements.Single(e => e.ExternalId == "w");
    }
}
