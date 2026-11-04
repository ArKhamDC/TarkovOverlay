using System.Text.Json;
using TarkovOverlay.Models;
using TarkovOverlay.Services;
using Xunit;

namespace TarkovOverlay.Tests;

public class MapUnderlayTests
{
    [Fact]
    public void ShippedFramesCoverTheKnownRaids()
    {
        var maps = Load();
        Assert.Equal(12, maps.Count);
        Assert.Equal(
            new[] { "customs", "factory", "ground-zero", "interchange", "lighthouse", "reserve", "shoreline", "streets-of-tarkov", "terminal", "the-lab", "the-labyrinth", "woods" },
            maps.Select(m => m.Id).OrderBy(id => id).ToArray());

        var aliases = maps.SelectMany(m => m.Aliases).ToArray();
        Assert.Equal(aliases.Length, aliases.Distinct().Count());

        Assert.Equal("reserve", Match(maps, "RezervBase", "reserve", "maps/rezerv_base_preset.bundle").Id);
        Assert.Equal("factory", Match(maps, "factory4_night", "night-factory", "maps/factory_night_preset.bundle").Id);
        Assert.Equal("factory", Match(maps, "factory_event", "infected-factory", "maps/factory_event_preset.bundle").Id);
        Assert.Equal("ground-zero", Match(maps, "Sandbox_high", "ground-zero-21", "maps/sandbox_high_preset.bundle").Id);
        Assert.Equal("the-lab", Match(maps, "laboratory_dark", "the-lab-dark", "maps/laboratory_dark_preset.bundle").Id);
        Assert.Equal("streets-of-tarkov", Match(maps, "TarkovStreets", "streets-of-tarkov", "maps/city_preset.bundle").Id);
        Assert.Equal("customs", Match(maps, "bigmap", "customs", "maps/customs_preset.bundle").Id);
        Assert.Null(MapUnderlayCatalog.Match(maps, Game("Icebreaker", "icebreaker", "maps/icebreaker.bundle")));
        Assert.Null(MapUnderlayCatalog.Match(maps, Game("unknown", "nowhere", "maps/nowhere.bundle")));
    }

    [Fact]
    public void MissingImageFallsBackInsteadOfThrowing()
    {
        const string json = """
            {"version":1,"maps":[
              {"id":"x","image":"missing.png","coordinateRotation":180,"bounds":{"x0":0,"z0":0,"x1":10,"z1":10},"aliases":["x"],"labels":[]},
              {"id":"bad","image":"x.png","coordinateRotation":0,"bounds":{"x0":1,"z0":1,"x1":1,"z1":2}}
            ]}
            """;
        Assert.Empty(MapUnderlayCatalog.Load(json, Path.GetTempPath()));
        Assert.Empty(MapUnderlayCatalog.Load("{}", Path.GetTempPath()));
    }

    [Fact]
    public void FrameCornersAndCenterMatchTheImage()
    {
        foreach (var map in Load())
        {
            var corners = new[]
            {
                Uv(map, map.MinX, map.MinZ),
                Uv(map, map.MaxX, map.MinZ),
                Uv(map, map.MinX, map.MaxZ),
                Uv(map, map.MaxX, map.MaxZ)
            };
            Assert.Equal(4, corners.Select(p => $"{Math.Round(p.U)}:{Math.Round(p.V)}").Distinct().Count());
            foreach (var (u, v) in corners)
            {
                Assert.True(NearEdge(u) && NearEdge(v), $"{map.Id} corner {u},{v}");
            }

            var (cu, cv) = Uv(map, (map.MinX + map.MaxX) / 2, (map.MinZ + map.MaxZ) / 2);
            Assert.InRange(cu, 0.49, 0.51);
            Assert.InRange(cv, 0.49, 0.51);

            var (ou, ov) = Uv(map, map.MaxX + 400, map.MaxZ + 400);
            Assert.True(ou < 0 || ou > 1 || ov < 0 || ov > 1, $"{map.Id} {ou},{ov}");

            Assert.True(WorldMath.TryFit(map.MinX, map.MaxX, map.MinZ, map.MaxZ, map.Rotation, 1000, 1000, out var fit));
            var (pw, ph) = PngSize(map.ImagePath);
            var frame = fit.UsedW / fit.UsedH;
            var image = pw / (double)ph;
            Assert.InRange(Math.Abs(frame - image) / frame, 0, 0.02);
        }
    }

    [Fact]
    public void CustomsRasterKeepsApiExtractOrientation()
    {
        var customs = Load().Single(m => m.Id == "customs");
        Assert.Equal(180, customs.Rotation);
        Assert.Equal(-372, customs.MinX);
        Assert.Equal(698, customs.MaxX);
        Assert.Equal(-307, customs.MinZ);
        Assert.Equal(237, customs.MaxZ);
        var (width, height) = PngSize(customs.ImagePath);
        Assert.Equal(1400, width);
        Assert.Equal(705, height);
        Assert.Contains("Shebuka", customs.Credit);

        // Published landmark positions still sit on this frame: Big Red east of dorms, dorms south of trailer park.
        var bigRed = Uv(customs, -215, -119);
        var dorms = Uv(customs, 200, 150);
        var trailerPark = Uv(customs, -211, -219);
        Assert.True(bigRed.U > dorms.U);
        Assert.True(dorms.V > trailerPark.V);
        Assert.InRange(WorldMath.ScreenAngleDegrees(0, 0, customs.CompassDegrees, customs.Rotation), -0.1, 0.1);

        Assert.Equal(28, customs.Extracts.Length);
        Assert.Contains(customs.Extracts, e => e.Faction == "scav");
        Assert.Equal(11, customs.Labels.Length);
        Assert.DoesNotContain(customs.Labels, l => l.Text is "Administration Gate" or "Railroad to Port" or "Big Red");

        var zb1011 = Extract(customs, "59c2c12e375a1b45a8e5ac1a32c5581b593aa4df");
        var zb1012 = Extract(customs, "cdae9cc1c431a141431e9022a901eaaa70ef67ea");
        var crossroads = Extract(customs, "ab12c1f902a669fb0c940820e8cc40d7131e3208");
        var trailer = Extract(customs, "f4cdf3dbe54ef647a2a798266a39f1761f871b96");
        var ruaf = Extract(customs, "aa56e9228b8452533873eeffa11b587007f49429");
        Assert.Equal("ZB-1011", zb1011.NameEn);
        Assert.Equal("ЗБ-1011", zb1011.NameRu);
        Assert.Equal("Smugglers' Bunker (ZB-1012)", zb1012.NameEn);
        Assert.Equal("Бункер Контрабандистов (ЗБ-1012)", zb1012.NameRu);
        Assert.Equal(621.5, zb1011.X);
        Assert.Equal(-336.33, crossroads.X);

        var zbU = Uv(customs, zb1011).U;
        var bunkerU = Uv(customs, zb1012).U;
        var crossU = Uv(customs, crossroads).U;
        var trailerU = Uv(customs, trailer).U;
        var ruafU = Uv(customs, ruaf).U;
        Assert.True(zbU < 0.15 && bunkerU < 0.3, $"ZB left, got {zbU} {bunkerU}");
        Assert.True(crossU > 0.9 && trailerU > 0.9 && ruafU > 0.6, $"east exits right, got {crossU} {trailerU} {ruafU}");
        Assert.True(zbU < crossU && bunkerU < ruafU);
        // API coordinates stay on the left. They are not pulled over to the Big Red landmark.
        Assert.True(zbU < bigRed.U - 0.5);

        var label = customs.Labels.Single(l => l.Text == "ZB-1011");
        Assert.Equal("ЗБ-1011", label.TextRu);
        Assert.Equal("ZB-1011", label.Display(english: true));
        Assert.Equal("ЗБ-1011", label.Display(english: false));
        Assert.Equal(zb1011.X, label.X);
        Assert.Equal(zb1011.Z, label.Z);
    }

    [Fact]
    public void ReserveTrainExtractLandsOnTheTrainDepot()
    {
        var reserve = Load().Single(m => m.Id == "reserve");
        var depot = Area("reserve", "train depot");
        var point = Uv(reserve, 140.8, -146.2);
        var box = new[]
        {
            Uv(reserve, depot.x0, depot.z0),
            Uv(reserve, depot.x1, depot.z0),
            Uv(reserve, depot.x0, depot.z1),
            Uv(reserve, depot.x1, depot.z1)
        };
        Assert.InRange(point.U, box.Min(p => p.U) - 1e-6, box.Max(p => p.U) + 1e-6);
        Assert.InRange(point.V, box.Min(p => p.V) - 1e-6, box.Max(p => p.V) + 1e-6);
        Assert.InRange(WorldMath.ScreenAngleDegrees(0, 0, reserve.CompassDegrees, reserve.Rotation), 14, 17);
    }

    [Fact]
    public void FactoryFrameUsesThePublishedQuarterTurn()
    {
        var factory = Load().Single(m => m.Id == "factory");
        var (u, v) = Uv(factory, factory.MaxX, (factory.MinZ + factory.MaxZ) / 2);
        Assert.InRange(u, 0.4, 0.6);
        Assert.InRange(v, -0.02, 0.02);
        var (gateU, gateV) = Uv(factory, -17.5, -61.3);
        Assert.InRange(gateU, 0, 1);
        Assert.InRange(gateV, 0, 1);
    }

    [Fact]
    public void ScreenPositionMatchesImageUvAndLetterboxes()
    {
        var customs = Load().Single(m => m.Id == "customs");
        Assert.True(WorldMath.TryFit(customs.MinX, customs.MaxX, customs.MinZ, customs.MaxZ, customs.Rotation, 200, 200, out var fit));
        Assert.True(fit.UsedW > fit.UsedH);
        Assert.InRange(fit.OriginX, -0.01, 0.01);
        Assert.True(fit.OriginY > 1);
        Assert.InRange(fit.UsedW, 199.9, 200.01);

        var (u, v) = Uv(customs, 200, 150);
        var (x, y) = fit.ToScreen(200, 150);
        Assert.Equal(fit.OriginX + u * fit.UsedW, x, 6);
        Assert.Equal(fit.OriginY + v * fit.UsedH, y, 6);
        Assert.False(WorldMath.TryFit(0, 1, 0, 1, 0, 1, 200, out _));
    }

    private static MapUnderlay Match(IReadOnlyList<MapUnderlay> maps, string nameId, string normalized, string scene)
        => MapUnderlayCatalog.Match(maps, Game(nameId, normalized, scene))!;

    private static GameMap Game(string nameId, string normalized, string scene)
        => new("id", nameId, scene, nameId, nameId, 0, 0, 1, 0, 1, MapIndex.Aliases(nameId, normalized, scene), []);

    private static (double U, double V) Uv(MapUnderlay map, double x, double z)
    {
        Assert.True(WorldMath.TryImageUv(x, z, map.MinX, map.MaxX, map.MinZ, map.MaxZ, map.Rotation, out var u, out var v));
        return (u, v);
    }

    private static (double U, double V) Uv(MapUnderlay map, MapExtract extract) => Uv(map, extract.X, extract.Z);

    private static MapExtract Extract(MapUnderlay map, string id)
        => map.Extracts.Single(e => e.Id == id);

    private static (double x0, double z0, double x1, double z1) Area(string id, string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(MapsDir(), "underlays.json")));
        var map = doc.RootElement.GetProperty("maps").EnumerateArray().Single(m => m.GetProperty("id").GetString() == id);
        var area = map.GetProperty("areas").EnumerateArray().Single(a => a.GetProperty("name").GetString() == name);
        return (area.GetProperty("x0").GetDouble(), area.GetProperty("z0").GetDouble(), area.GetProperty("x1").GetDouble(), area.GetProperty("z1").GetDouble());
    }

    private static bool NearEdge(double value) => Math.Abs(value) < 1e-4 || Math.Abs(value - 1) < 1e-4;

    private static (int W, int H) PngSize(string path)
    {
        var buf = File.ReadAllBytes(path);
        Assert.True(buf.Length > 24 && buf[0] == 0x89 && buf[1] == 0x50);
        return (ReadBe(buf, 16), ReadBe(buf, 20));
    }

    private static int ReadBe(byte[] buf, int offset)
        => (buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3];

    private static IReadOnlyList<MapUnderlay> Load()
    {
        var dir = MapsDir();
        return MapUnderlayCatalog.LoadDirectory(dir);
    }

    private static string MapsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "TarkovOverlay", "Maps", "underlays.json");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("underlays.json was not found from the test output directory");
    }
}
