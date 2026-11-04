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
    public void CustomsCompassPutsBigRedEastAndDormsSouth()
    {
        var customs = Load().Single(m => m.Id == "customs");
        var bigRed = Uv(customs, Label(customs, "Big Red"));
        var dorms = Uv(customs, Label(customs, "3-story dorms"));
        var trailer = Uv(customs, Label(customs, "Trailer Park"));
        Assert.True(bigRed.U > dorms.U);
        Assert.True(dorms.V > trailer.V);

        var gas = Uv(customs, Label(customs, "Old Gas"));
        var (u, v) = Uv(customs, 310.7, -174.9);
        Assert.InRange(Math.Abs(u - gas.U), 0, 0.04);
        Assert.InRange(Math.Abs(v - gas.V), 0, 0.04);
        Assert.InRange(WorldMath.ScreenAngleDegrees(0, 0, customs.CompassDegrees, customs.Rotation), -0.1, 0.1);
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
    public void HeightSelectsTheFloorInsideThatFootprint()
    {
        var maps = Load();
        var customs = maps.Single(m => m.Id == "customs");
        var dormsSecond = MapUnderlayCatalog.FloorAt(customs.Floors, 200, 4, 150);
        Assert.NotNull(dormsSecond);
        Assert.Equal("2nd Floor", dormsSecond!.Name);
        Assert.Contains("dorm", dormsSecond.Place, StringComparison.OrdinalIgnoreCase);
        Assert.Null(MapUnderlayCatalog.FloorAt(customs.Floors, 200, 1, 150));
        Assert.Equal("3rd Floor", MapUnderlayCatalog.FloorAt(customs.Floors, 200, 8, 150)!.Name);
        Assert.Null(MapUnderlayCatalog.FloorAt(customs.Floors, 0, 4, 0));

        var reserve = maps.Single(m => m.Id == "reserve");
        var bunker = MapUnderlayCatalog.FloorAt(reserve.Floors, 80, -10, -100);
        Assert.NotNull(bunker);
        Assert.Equal("Bunkers", bunker!.Name);
        Assert.Null(MapUnderlayCatalog.FloorAt(reserve.Floors, 80, 2, -100));

        Assert.True(WorldMath.TryFit(dormsSecond.X0!.Value, dormsSecond.X1!.Value, dormsSecond.Z0!.Value, dormsSecond.Z1!.Value, customs.Rotation, 420, 320, out var zoom));
        var (sx, sy) = zoom.ToScreen(200, 150);
        Assert.InRange(sx, zoom.OriginX, zoom.OriginX + zoom.UsedW);
        Assert.InRange(sy, zoom.OriginY, zoom.OriginY + zoom.UsedH);
        var outside = zoom.ToScreen(customs.MinX, customs.MinZ);
        Assert.True(outside.X < zoom.OriginX || outside.X > zoom.OriginX + zoom.UsedW
            || outside.Y < zoom.OriginY || outside.Y > zoom.OriginY + zoom.UsedH);
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

    private static (double U, double V) Uv(MapUnderlay map, MapLabel label) => Uv(map, label.X, label.Z);

    private static MapLabel Label(MapUnderlay map, string text)
        => map.Labels.Single(l => l.Text == text);

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
        return MapUnderlayCatalog.Load(File.ReadAllText(Path.Combine(dir, "underlays.json")), Path.Combine(dir, "images"));
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
