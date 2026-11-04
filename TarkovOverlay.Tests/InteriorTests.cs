using TarkovOverlay.Models;
using TarkovOverlay.Services;
using Xunit;

namespace TarkovOverlay.Tests;

public class InteriorTests
{
    [Fact]
    public void ShippedCustomsFloorsLoadWithoutABoiler()
    {
        var buildings = Load();
        Assert.Equal(12, buildings.Count);
        Assert.Equal(25, buildings.Sum(b => b.Floors.Length));
        Assert.DoesNotContain(buildings, b => b.Id.Contains("boiler", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(buildings, b => b.Id == "zb1011_building");
        Assert.Contains(buildings, b => b.Id == "zb1011_bunker");

        var ids = buildings.Select(b => b.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        foreach (var building in buildings)
        {
            Assert.Equal("customs", building.MapId);
            Assert.Contains("bigmap", building.Aliases);
            Assert.Equal(180, building.Rotation);
            Assert.NotEmpty(building.NameEn);
            Assert.NotEmpty(building.NameRu);
            Assert.DoesNotContain(building.Floors, f => f.Labels.Any(l => l.Kind == "stairs"));
            foreach (var floor in building.Floors)
            {
                Assert.True(File.Exists(floor.ImagePath));
                Assert.DoesNotContain("labeled", floor.ImagePath, StringComparison.OrdinalIgnoreCase);
                var (w, h) = PngSize(floor.ImagePath);
                Assert.Equal(floor.Width, w);
                Assert.Equal(floor.Height, h);
            }
        }

        Assert.Equal(new[] { 0, 1, 2 }, buildings.Single(b => b.Id == "fortress").Floors.Select(f => f.Floor).ToArray());
        Assert.Equal(new[] { 1 }, buildings.Single(b => b.Id == "new_gas").Floors.Select(f => f.Floor).ToArray());
        Assert.Equal(new[] { 1 }, buildings.Single(b => b.Id == "repair_shop").Floors.Select(f => f.Floor).ToArray());
    }

    [Fact]
    public void DormLabelsAreVerifiedRoomNumbersInImagePixels()
    {
        var buildings = Load();
        var three = buildings.Single(b => b.Id == "dorms_3story");
        var two = buildings.Single(b => b.Id == "dorms_2story");
        Assert.Equal(19, Floor(three, 1).Labels.Length);
        Assert.Equal(23, Floor(three, 2).Labels.Length);
        Assert.Equal(15, Floor(three, 3).Labels.Length);
        Assert.Equal(14, Floor(two, 1).Labels.Length);
        Assert.Equal(18, Floor(two, 2).Labels.Length);

        var room = Floor(three, 1).Labels.Single(l => l.Text == "101");
        Assert.Equal("room", room.Kind);
        Assert.Equal(369, room.U);
        Assert.Equal(63, room.V);
        var image = room.ToImage(Floor(three, 1).Width, Floor(three, 1).Height, english: false);
        Assert.Equal("101", image.Text);
        Assert.Equal(369.0 / 592, image.U, 6);
        Assert.Equal(63.0 / 736, image.V, 6);

        var overseer = Floor(three, 2).Labels.Single(l => l.Kind == "overseer");
        Assert.Equal("Overseer", overseer.Display(english: true));
        Assert.Equal("Смотритель", overseer.Display(english: false));
        var marked = Floor(three, 3).Labels.Single(l => l.Text == "314");
        Assert.Equal("marked", marked.Kind);
        Assert.Equal("314", marked.Display(english: false));
        Assert.All(Floor(three, 1).Labels, l => Assert.Matches("^[0-9]+$", l.Display(false)));
    }

    [Fact]
    public void FootprintUsesTheSameProjectionAsExtracts()
    {
        foreach (var building in Load())
        {
            Assert.True(WorldMath.TryFit(building.MinX, building.MaxX, building.MinZ, building.MaxZ, building.Rotation, 640, 480, out var fit));
            var samples = new[]
            {
                (building.X0, building.Z0),
                (building.X1, building.Z1),
                ((building.X0 + building.X1) / 2, (building.Z0 + building.Z1) / 2)
            };
            foreach (var (x, z) in samples)
            {
                Assert.True(building.TryUv(x, z, out var u, out var v), building.Id);
                Assert.Equal((x - building.X0) / (building.X1 - building.X0), u, 6);
                Assert.Equal((z - building.Z0) / (building.Z1 - building.Z0), v, 6);
                var (sx, sy) = fit.ToScreen(x, z);
                Assert.Equal(fit.OriginX + u * fit.UsedW, sx, 5);
                Assert.Equal(fit.OriginY + v * fit.UsedH, sy, 5);
            }

            Assert.True(building.Contains(building.X0, building.Z0));
            Assert.True(building.Contains(building.X1, building.Z1));
            Assert.False(building.Contains(building.X0 + (building.X0 - building.X1), building.Z0));
        }
    }

    [Fact]
    public void EnteringABuildingSelectsGroundAndLeavingReturnsToOverview()
    {
        var customs = For("bigmap", "customs", "maps/customs_preset.bundle");
        Assert.Equal(12, customs.Count);
        Assert.Empty(InteriorCatalog.ForMap(Load(), Game("woods", "woods", "maps/woods_preset.bundle")));

        var three = customs.Single(b => b.Id == "dorms_3story");
        var two = customs.Single(b => b.Id == "dorms_2story");
        var fortress = customs.Single(b => b.Id == "fortress");
        var gas = customs.Single(b => b.Id == "new_gas");

        var state = InteriorFix.Overview;
        (state, var building, var floor) = InteriorCatalog.Advance(customs, state, 0, 0);
        Assert.Null(building);
        Assert.Equal(1, state.Floor);

        (state, building, floor) = At(customs, state, three);
        Assert.Equal("dorms_3story", building!.Id);
        Assert.Equal(1, floor!.Floor);
        Assert.Null(InteriorCatalog.FloorOptions(gas, 1));
        var options = InteriorCatalog.FloorOptions(building, floor.Floor);
        Assert.Equal(new[] { "1", "2", "3" }, options!.Select(o => o.Caption).ToArray());
        Assert.Equal("Floor 1", options!.Single(o => o.Selected).Tip);

        state = InteriorCatalog.RememberFloor(state, 3);
        (state, building, floor) = At(customs, state, three);
        Assert.Equal(3, floor!.Floor);
        Assert.Equal("3", InteriorCatalog.FloorOptions(building!, 3)!.Single(o => o.Selected).Caption);

        (state, building, floor) = At(customs, state, two);
        Assert.Equal("dorms_2story", building!.Id);
        Assert.Equal(1, floor!.Floor);

        state = InteriorCatalog.RememberFloor(state, 2);
        (state, _, floor) = At(customs, state, two);
        Assert.Equal(2, floor!.Floor);

        (state, building, _) = InteriorCatalog.Advance(customs, state, 210, 160);
        Assert.Null(building);
        Assert.Equal(InteriorFix.Overview, state);

        (state, building, floor) = At(customs, state, three);
        Assert.Equal(1, floor!.Floor);

        (state, building, floor) = At(customs, state, fortress);
        Assert.Equal("fortress", building!.Id);
        Assert.Equal(1, floor!.Floor);
        Assert.Equal(new[] { "U", "1", "2" }, InteriorCatalog.FloorOptions(building, 1)!.Select(o => o.Caption).ToArray());
        Assert.Equal("Underground", InteriorCatalog.FloorOptions(building, 0)!.Single(o => o.Floor == 0).Tip);

        state = InteriorCatalog.RememberFloor(state, 0);
        (state, _, floor) = At(customs, state, fortress);
        Assert.Equal(0, floor!.Floor);
        Assert.Equal("U", InteriorCatalog.FloorCaption(0));

        (state, building, floor) = At(customs, state, gas);
        Assert.Equal("new_gas", building!.Id);
        Assert.Equal(1, floor!.Floor);
        Assert.Equal(1, InteriorCatalog.SelectFloor(gas, 2).Floor);
        Assert.Equal(1, InteriorCatalog.SelectFloor(fortress, 9).Floor);
        Assert.Equal(0, InteriorCatalog.SelectFloor(fortress, 0).Floor);

        (state, building, _) = InteriorCatalog.Advance(customs, state, 100, -50);
        Assert.Null(building);
        Assert.Null(InteriorCatalog.Hit(customs, 100, -50));
    }

    [Fact]
    public void SmallestFootprintWinsAndUnverifiedLabelsAreDropped()
    {
        var outer = Building("outer", 0, 0, 100, 100, Floor(1));
        var inner = Building("inner", 40, 40, 60, 60, Floor(1));
        var hit = InteriorCatalog.Hit([outer, inner], 50, 50);
        Assert.Equal("inner", hit!.Id);
        Assert.Equal("outer", InteriorCatalog.Hit([outer, inner], 10, 10)!.Id);
        Assert.Null(InteriorCatalog.Hit([outer], -1, 50));

        var dir = Path.Combine(MapsDir(), "interiors");
        const string json = """
            {"version":1,"credit":"credit","maps":[{"id":"customs","coordinateRotation":180,"aliases":["customs"],"buildings":[
              {"id":"boiler","name":"Boiler","nameRu":"Котельная","rectXZ":{"x0":1,"z0":1,"x1":20,"z1":20},"floors":[
                {"floor":1,"floorKey":"f1","image":"missing.png","width":10,"height":10}
              ]},
              {"id":"flat","name":"Flat","nameRu":"Плоское","rectXZ":{"x0":1,"z0":1,"x1":1,"z1":8},"floors":[
                {"floor":1,"floorKey":"f1","image":"dorms_3story_f1.png","width":592,"height":736}
              ]},
              {"id":"dorms_3story","name":"3-Story Dormitory","nameRu":"Общежитие 3эт","rectXZ":{"x0":10,"z0":0,"x1":0,"z1":10},"floors":[
                {"floor":2,"floorKey":"f2","image":"dorms_3story_f2.png","width":592,"height":736,"labels":[]},
                {"floor":1,"floorKey":"f1","image":"dorms_3story_f1.png","width":592,"height":736,"labels":[
                  {"text":"101","u":10,"v":20,"kind":"room","confidence":"verified"},
                  {"text":"999","u":1,"v":1,"kind":"room","confidence":"approx"},
                  {"text":"Stairs","textRu":"Лестница","u":2,"v":2,"kind":"stairs","confidence":"verified"},
                  {"text":"Overseer","textRu":"Смотритель","u":3,"v":4,"kind":"overseer","confidence":"verified"}
                ]}
              ]}
            ]}]}
            """;
        var loaded = InteriorCatalog.Load(json, dir);
        var dorms = Assert.Single(loaded);
        Assert.Equal("dorms_3story", dorms.Id);
        Assert.Equal(new[] { 1, 2 }, dorms.Floors.Select(f => f.Floor).ToArray());
        Assert.Equal(new[] { "101", "Overseer" }, dorms.Floors[0].Labels.Select(l => l.Text).ToArray());
        Assert.Equal("credit", dorms.Credit);
        Assert.Empty(InteriorCatalog.Load("{}", dir));
    }

    private static IReadOnlyList<InteriorBuilding> For(string nameId, string normalized, string scene)
        => InteriorCatalog.ForMap(Load(), Game(nameId, normalized, scene));

    private static GameMap Game(string nameId, string normalized, string scene)
        => new("id", nameId, scene, nameId, nameId, 0, 0, 1, 0, 1, MapIndex.Aliases(nameId, normalized, scene), []);

    private static InteriorFloor Floor(InteriorBuilding building, int floor)
        => building.Floors.Single(f => f.Floor == floor);

    private static (double X, double Z) Mid(InteriorBuilding building)
        => ((building.X0 + building.X1) / 2, (building.Z0 + building.Z1) / 2);

    private static (InteriorFix State, InteriorBuilding? Building, InteriorFloor? Floor) At(
        IReadOnlyList<InteriorBuilding> map, InteriorFix state, InteriorBuilding building)
    {
        var (x, z) = Mid(building);
        return InteriorCatalog.Advance(map, state, x, z);
    }

    private static InteriorBuilding Building(string id, double x0, double z0, double x1, double z1, params InteriorFloor[] floors)
        => new()
        {
            Id = id,
            MapId = "customs",
            NameEn = id,
            NameRu = id,
            X0 = x0,
            Z0 = z0,
            X1 = x1,
            Z1 = z1,
            Rotation = 180,
            Aliases = ["customs"],
            Floors = floors
        };

    private static InteriorFloor Floor(int floor)
        => new()
        {
            Floor = floor,
            Key = "f" + floor,
            ImagePath = "x",
            Width = 10,
            Height = 10,
            Labels = []
        };

    private static (int W, int H) PngSize(string path)
    {
        var buf = File.ReadAllBytes(path);
        Assert.True(buf.Length > 24 && buf[0] == 0x89 && buf[1] == 0x50);
        return ((buf[16] << 24) | (buf[17] << 16) | (buf[18] << 8) | buf[19],
            (buf[20] << 24) | (buf[21] << 16) | (buf[22] << 8) | buf[23]);
    }

    private static IReadOnlyList<InteriorBuilding> Load()
    {
        var dir = MapsDir();
        return InteriorCatalog.Load(File.ReadAllText(Path.Combine(dir, "interiors.json")), Path.Combine(dir, "interiors"));
    }

    private static string MapsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "TarkovOverlay", "Maps", "interiors.json");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("interiors.json was not found from the test output directory");
    }
}
