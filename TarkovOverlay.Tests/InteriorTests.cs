using TarkovOverlay.Models;
using TarkovOverlay.Services;
using Xunit;

namespace TarkovOverlay.Tests;

public class InteriorTests
{
    [Fact]
    public void ShippedCustomsFloorsLoadWithoutABoiler()
    {
        var all = Load();
        var buildings = all.Where(b => b.MapId == "customs").ToList();
        Assert.Equal(12, buildings.Count);
        Assert.Equal(25, buildings.Sum(b => b.Floors.Length));
        Assert.Contains(all, b => b.Id == "labs");
        Assert.Contains(all, b => b.MapId == "factory" && b.Id == "factory");
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
                if (building.Rotation is 90 or 270)
                {
                    Assert.Equal((z - building.Z0) / (building.Z1 - building.Z0), u, 6);
                    Assert.Equal((x - building.X0) / (building.X1 - building.X0), v, 6);
                }
                else
                {
                    Assert.Equal((x - building.X0) / (building.X1 - building.X0), u, 6);
                    Assert.Equal((z - building.Z0) / (building.Z1 - building.Z0), v, 6);
                }
                var (sx, sy) = fit.ToScreen(x, z);
                Assert.Equal(fit.OriginX + u * fit.UsedW, sx, 5);
                Assert.Equal(fit.OriginY + v * fit.UsedH, sy, 5);
            }

            Assert.True(building.Contains(building.X0, building.Z0));
            Assert.True(building.Contains(building.X1, building.Z1));
            var outsideX = building.X0 + (building.X0 - building.X1);
            if (building.CoversMap)
                Assert.True(building.Contains(outsideX, building.Z0));
            else
                Assert.False(building.Contains(outsideX, building.Z0));
        }
    }

    [Fact]
    public void LabsFollowsHeightAndKeepsAPinnedFloorUntilTheBandChanges()
    {
        var labs = Load().Single(b => b.Id == "labs");
        Assert.Equal("the-lab", labs.MapId);
        Assert.True(labs.CoversMap);
        Assert.Equal(270, labs.Rotation);
        Assert.Equal(new[] { -1, 1, 2 }, labs.Floors.Select(f => f.Floor).ToArray());
        Assert.Equal(new[] { "T", "1", "2" }, labs.Floors.Select(f => f.Caption).ToArray());
        Assert.Equal(new[] { "Technical", "First", "Second" }, labs.Floors.Select(f => f.Tip).ToArray());
        Assert.Contains("Shebuka", labs.Credit);
        Assert.Equal(-305.23, labs.X0, 3);
        Assert.Equal(-466.72, labs.Z0, 3);
        Assert.Equal(-78.1, labs.X1, 3);
        Assert.Equal(-187.66, labs.Z1, 3);
        foreach (var plan in labs.Floors)
        {
            Assert.Equal(2880, plan.Width);
            Assert.Equal(2344, plan.Height);
            var (w, h) = PngSize(plan.ImagePath);
            Assert.Equal(2880, w);
            Assert.Equal(2344, h);
        }

        var overview = File.ReadAllBytes(Path.Combine(MapsDir(), "images", "the-lab.png"));
        Assert.Equal(overview, File.ReadAllBytes(labs.Floors.Single(f => f.Floor == 1).ImagePath));

        Assert.Equal(-1, InteriorCatalog.FloorFromAltitude(labs, -1));
        Assert.Equal(-1, InteriorCatalog.FloorFromAltitude(labs, -0.900001));
        Assert.Equal(1, InteriorCatalog.FloorFromAltitude(labs, -0.9));
        Assert.Equal(1, InteriorCatalog.FloorFromAltitude(labs, 2.5));
        Assert.Equal(2, InteriorCatalog.FloorFromAltitude(labs, 3));
        Assert.Equal(2, InteriorCatalog.FloorFromAltitude(labs, 8.2));
        Assert.Null(InteriorCatalog.FloorFromAltitude(labs, double.NaN));

        var onMap = InteriorCatalog.ForMap(Load(), Game("laboratory", "the-lab", "maps/laboratory_preset.bundle"));
        Assert.Single(onMap);
        Assert.Single(InteriorCatalog.ForMap(Load(), Game("laboratory_dark", "the-lab-dark", "maps/laboratory_dark_preset.bundle")));
        Assert.Single(InteriorCatalog.ForMap(Load(), Game("laboratory_event", "infected-lab", "maps/laboratory_event_preset.bundle")));

        var state = InteriorFix.Overview;
        (state, var building, var floor) = InteriorCatalog.Advance(onMap, state, -165.25, 1.2, -349.22);
        Assert.Equal("labs", building!.Id);
        Assert.Equal(1, floor!.Floor);
        Assert.Null(state.LockBand);
        Assert.Equal("First", InteriorCatalog.FloorOptions(building, floor.Floor)!.Single(o => o.Selected).Tip);
        Assert.Equal(new[] { "T", "1", "2" }, InteriorCatalog.FloorOptions(building, 1)!.Select(o => o.Caption).ToArray());

        // A point far outside the raster still stays on the complex, on the floor Y selects.
        (state, building, floor) = InteriorCatalog.Advance(onMap, state, 0, -2, 0);
        Assert.Equal(-1, floor!.Floor);

        state = InteriorCatalog.PinFloor(onMap, state, 2, -2);
        Assert.Equal(-1, state.LockBand);
        (state, _, floor) = InteriorCatalog.Advance(onMap, state, 0, -3.5, 0);
        Assert.Equal(2, floor!.Floor);
        Assert.Equal(-1, state.LockBand);

        (state, _, floor) = InteriorCatalog.Advance(onMap, state, -120, 0.4, -300);
        Assert.Equal(1, floor!.Floor);
        Assert.Null(state.LockBand);

        (state, _, floor) = InteriorCatalog.Advance(onMap, state, -231.46, 6.19, -457.77);
        Assert.Equal(2, floor!.Floor);
        (state, _, floor) = InteriorCatalog.Advance(onMap, state, -122.89, -3.66, -258.32);
        Assert.Equal(-1, floor!.Floor);

        Assert.True(InteriorCatalog.ExtractOnFloor(labs, -3.66, -1));
        Assert.False(InteriorCatalog.ExtractOnFloor(labs, -3.66, 1));
        Assert.True(InteriorCatalog.ExtractOnFloor(labs, 6.19, 2));
        Assert.False(InteriorCatalog.ExtractOnFloor(labs, 5.54, -1));
        var dorms = Load().Single(b => b.Id == "dorms_3story");
        Assert.True(InteriorCatalog.ExtractOnFloor(dorms, 0, 1));
        Assert.True(InteriorCatalog.ExtractOnFloor(dorms, 0, 3));

        // Manager lock from the fitted frame lands on the same image point as the SVG.
        Assert.True(labs.TryUv(-165.25, -349.22, out var u, out var v));
        Assert.InRange(u, 303.2 / 720 - 0.004, 303.2 / 720 + 0.004);
        Assert.InRange(v, 361.2 / 586 - 0.004, 361.2 / 586 + 0.004);

        // Red is the small room east of the stair. Violet (R23) is north of it, not the same fill.
        Assert.True(labs.TryUv(-261.34, -320.07, out var redU, out var redV));
        Assert.True(labs.TryUv(-274.47, -316.97, out var violetU, out var violetV));
        Assert.True(violetV < redV - 0.02, $"violet should sit above red, got {violetV} vs {redV}");
        Assert.InRange(Math.Abs(violetU - redU), 0, 0.08);
    }

    [Fact]
    public void FactoryFollowsHeightAndKeepsAPinnedFloorUntilTheBandChanges()
    {
        var factory = Load().Single(b => b.Id == "factory" && b.MapId == "factory");
        Assert.True(factory.CoversMap);
        Assert.Equal(90, factory.Rotation);
        Assert.Equal(new[] { -1, 1, 2, 3 }, factory.Floors.Select(f => f.Floor).ToArray());
        Assert.Equal(new[] { "-1", "1", "2", "3" }, factory.Floors.Select(f => f.Caption).ToArray());
        Assert.Equal(new[] { "Basement", "Ground", "Second", "Third" }, factory.Floors.Select(f => f.Tip).ToArray());
        Assert.Contains("Shebuka", factory.Credit);
        Assert.Equal(77, factory.X0, 3);
        Assert.Equal(67.4, factory.Z0, 3);
        Assert.Equal(-65.5, factory.X1, 3);
        Assert.Equal(-64.5, factory.Z1, 3);
        foreach (var plan in factory.Floors)
        {
            Assert.Equal(1047, plan.Width);
            Assert.Equal(1130, plan.Height);
            var (w, h) = PngSize(plan.ImagePath);
            Assert.Equal(1047, w);
            Assert.Equal(1130, h);
        }

        var overview = File.ReadAllBytes(Path.Combine(MapsDir(), "images", "factory.png"));
        Assert.Equal(overview, File.ReadAllBytes(factory.Floors.Single(f => f.Floor == 1).ImagePath));

        Assert.Equal(-1, InteriorCatalog.FloorFromAltitude(factory, -1.01));
        Assert.Equal(1, InteriorCatalog.FloorFromAltitude(factory, -1));
        Assert.Equal(1, InteriorCatalog.FloorFromAltitude(factory, 2.99));
        Assert.Equal(2, InteriorCatalog.FloorFromAltitude(factory, 3));
        Assert.Equal(2, InteriorCatalog.FloorFromAltitude(factory, 5.99));
        Assert.Equal(3, InteriorCatalog.FloorFromAltitude(factory, 6));
        Assert.Equal(3, InteriorCatalog.FloorFromAltitude(factory, 9.08));
        Assert.Null(InteriorCatalog.FloorFromAltitude(factory, double.NaN));

        var onMap = InteriorCatalog.ForMap(Load(), Game("factory4_day", "factory", "maps/factory_day_preset.bundle"));
        Assert.Single(onMap);
        Assert.Single(InteriorCatalog.ForMap(Load(), Game("factory4_night", "night-factory", "maps/factory_night_preset.bundle")));
        Assert.Single(InteriorCatalog.ForMap(Load(), Game("factory_event", "infected-factory", "maps/factory_event_preset.bundle")));

        var state = InteriorFix.Overview;
        (state, var building, var floor) = InteriorCatalog.Advance(onMap, state, -19.08, 1.45, -48.57);
        Assert.Equal("factory", building!.Id);
        Assert.Equal(1, floor!.Floor);
        Assert.Null(state.LockBand);
        Assert.Equal("Ground", InteriorCatalog.FloorOptions(building, floor.Floor)!.Single(o => o.Selected).Tip);
        Assert.Equal(new[] { "-1", "1", "2", "3" }, InteriorCatalog.FloorOptions(building, 1)!.Select(o => o.Caption).ToArray());

        (state, building, floor) = InteriorCatalog.Advance(onMap, state, 500, -2, 500);
        Assert.Equal(-1, floor!.Floor);

        state = InteriorCatalog.PinFloor(onMap, state, 3, -2);
        Assert.Equal(-1, state.LockBand);
        (state, _, floor) = InteriorCatalog.Advance(onMap, state, 0, -1.5, 0);
        Assert.Equal(3, floor!.Floor);
        Assert.Equal(-1, state.LockBand);

        (state, _, floor) = InteriorCatalog.Advance(onMap, state, -19.08, 1.2, -48.57);
        Assert.Equal(1, floor!.Floor);
        Assert.Null(state.LockBand);

        (state, _, floor) = InteriorCatalog.Advance(onMap, state, 58.43, 3.17, 63.3);
        Assert.Equal(2, floor!.Floor);
        (state, _, floor) = InteriorCatalog.Advance(onMap, state, 17.83, 8.73, 39.89);
        Assert.Equal(3, floor!.Floor);
        (state, _, floor) = InteriorCatalog.Advance(onMap, state, 73.89, -3.29, -29.08);
        Assert.Equal(-1, floor!.Floor);

        Assert.True(InteriorCatalog.ExtractOnFloor(factory, -3.29, -1));
        Assert.False(InteriorCatalog.ExtractOnFloor(factory, -3.29, 1));
        Assert.True(InteriorCatalog.ExtractOnFloor(factory, 1.99, 1));
        Assert.True(InteriorCatalog.ExtractOnFloor(factory, 3.17, 2));
        Assert.False(InteriorCatalog.ExtractOnFloor(factory, 3.17, 1));
        Assert.True(InteriorCatalog.ExtractOnFloor(factory, 8.73, 3));
        Assert.False(InteriorCatalog.ExtractOnFloor(factory, 8.73, 2));

        // Published rect: u=(67.4-z)/131.9, v=(77-x)/142.5. Ground emergency-exit door.
        Assert.True(factory.TryUv(-19.08, -48.57, out var u, out var v));
        Assert.Equal((67.4 - (-48.57)) / 131.9, u, 4);
        Assert.Equal((77 - (-19.08)) / 142.5, v, 4);
        Assert.InRange(u, 0, 1);
        Assert.InRange(v, 0, 1);

        // TerraGroup storage keycard is the basement door, not the ground emergency exit.
        Assert.True(factory.TryUv(-20.43, 44.06, out var cardU, out var cardV));
        Assert.True(cardV > v);
        Assert.InRange(cardU, 0, 1);
    }

    [Fact]
    public void GroundZeroFollowsHeightAndKeepsAPinnedFloorUntilTheBandChanges()
    {
        var map = Load().Single(b => b.Id == "groundzero");
        Assert.Equal("ground-zero", map.MapId);
        Assert.True(map.CoversMap);
        Assert.Equal(180, map.Rotation);
        Assert.Equal(new[] { -1, 1, 2, 3 }, map.Floors.Select(f => f.Floor).ToArray());
        Assert.Equal(new[] { "-1", "1", "2", "3" }, map.Floors.Select(f => f.Caption).ToArray());
        Assert.Equal(new[] { "Underground", "Ground", "Second", "Third" }, map.Floors.Select(f => f.Tip).ToArray());
        Assert.Contains("Shebuka", map.Credit);
        Assert.Equal(249, map.X0);
        Assert.Equal(-124, map.Z0);
        Assert.Equal(-99, map.X1);
        Assert.Equal(364, map.Z1);
        Assert.Empty(map.Floors.SelectMany(f => f.Labels));
        foreach (var plan in map.Floors)
        {
            Assert.Equal(2094, plan.Width);
            Assert.Equal(2931, plan.Height);
            var (w, h) = PngSize(plan.ImagePath);
            Assert.Equal(2094, w);
            Assert.Equal(2931, h);
            Assert.Contains("groundzero_", plan.ImagePath, StringComparison.OrdinalIgnoreCase);
        }

        var overview = File.ReadAllBytes(Path.Combine(MapsDir(), "images", "ground-zero.png"));
        Assert.Equal(overview, File.ReadAllBytes(map.Floors.Single(f => f.Floor == 1).ImagePath));

        Assert.Equal(-1, InteriorCatalog.FloorFromAltitude(map, 13.5));
        Assert.Equal(-1, InteriorCatalog.FloorFromAltitude(map, 20.999));
        Assert.Equal(1, InteriorCatalog.FloorFromAltitude(map, 21));
        Assert.Equal(1, InteriorCatalog.FloorFromAltitude(map, 27.999));
        Assert.Equal(2, InteriorCatalog.FloorFromAltitude(map, 28));
        Assert.Equal(2, InteriorCatalog.FloorFromAltitude(map, 32.3 - 1e-9));
        Assert.Equal(3, InteriorCatalog.FloorFromAltitude(map, 32.3));
        Assert.Equal(3, InteriorCatalog.FloorFromAltitude(map, 35.41));
        Assert.Null(InteriorCatalog.FloorFromAltitude(map, double.NaN));

        var onMap = InteriorCatalog.ForMap(Load(), Game("Sandbox", "ground-zero", "maps/sandbox_preset.bundle"));
        Assert.Single(onMap);
        Assert.Single(InteriorCatalog.ForMap(Load(), Game("Sandbox_high", "ground-zero-21", "maps/sandbox_high_preset.bundle")));
        Assert.Single(InteriorCatalog.ForMap(Load(), Game("Sandbox_start", "ground-zero-tutorial", "maps/sandbox_start_preset.bundle")));
        Assert.DoesNotContain(InteriorCatalog.ForMap(Load(), Game("laboratory", "the-lab", "maps/laboratory_preset.bundle")), b => b.Id == "groundzero");

        // Official frame, no x shift: the parking-utility lock is the image point (249-x)/348, (z+124)/488.
        Assert.True(map.TryUv(107.05, 50.64, out var lockU, out var lockV));
        Assert.Equal((249 - 107.05) / 348, lockU, 6);
        Assert.Equal((50.64 + 124) / 488, lockV, 6);
        Assert.InRange(lockU, 0, 1);
        Assert.InRange(lockV, 0, 1);
        Assert.True(map.TryUv(264.95, 56.14, out var oobU, out _));
        Assert.True(oobU < 0, "the second factory-exit lock is outside the frame");

        var state = InteriorFix.Overview;
        (state, var building, var floor) = InteriorCatalog.Advance(onMap, state, 151.63, 24.66, -97.46);
        Assert.Equal("groundzero", building!.Id);
        Assert.Equal(1, floor!.Floor);
        Assert.Null(state.LockBand);
        Assert.Equal("Ground", InteriorCatalog.FloorOptions(building, floor.Floor)!.Single(o => o.Selected).Tip);
        Assert.Equal(new[] { "-1", "1", "2", "3" }, InteriorCatalog.FloorOptions(building, 1)!.Select(o => o.Caption).ToArray());

        (state, building, floor) = InteriorCatalog.Advance(onMap, state, 900, 24, 900);
        Assert.Equal(1, floor!.Floor);

        state = InteriorCatalog.PinFloor(onMap, state, 3, 24);
        Assert.Equal(1, state.LockBand);
        (state, _, floor) = InteriorCatalog.Advance(onMap, state, 80, 25, 140);
        Assert.Equal(3, floor!.Floor);
        Assert.Equal(1, state.LockBand);

        (state, _, floor) = InteriorCatalog.Advance(onMap, state, -17.98, 30.71, 58.55);
        Assert.Equal(2, floor!.Floor);
        Assert.Null(state.LockBand);

        (state, _, floor) = InteriorCatalog.Advance(onMap, state, 107.05, 13.5, 50.64);
        Assert.Equal(-1, floor!.Floor);
        (state, _, floor) = InteriorCatalog.Advance(onMap, state, 0, 35.41, 0);
        Assert.Equal(3, floor!.Floor);

        Assert.True(InteriorCatalog.ExtractOnFloor(map, 24.66, 1));
        Assert.False(InteriorCatalog.ExtractOnFloor(map, 24.66, -1));
        Assert.True(InteriorCatalog.ExtractOnFloor(map, 14.76, -1));
        Assert.False(InteriorCatalog.ExtractOnFloor(map, 14.76, 1));
        Assert.True(InteriorCatalog.ExtractOnFloor(map, 30.71, 2));
        Assert.False(InteriorCatalog.ExtractOnFloor(map, 30.71, 3));
        Assert.True(InteriorCatalog.ExtractOnFloor(map, 35.41, 3));
        var dorms = Load().Single(b => b.Id == "dorms_3story");
        Assert.Null(dorms.FloorByY);
        Assert.True(InteriorCatalog.ExtractOnFloor(dorms, 14.76, 1));
    }

    [Fact]
    public void ShorelineUsesResortAndUndergroundBoxesAndKeepsAPinUntilThatRuleChanges()
    {
        var shore = Load().Single(b => b.Id == "shoreline" && b.MapId == "shoreline");
        Assert.True(shore.CoversMap);
        Assert.Equal(180, shore.Rotation);
        Assert.Null(shore.FloorByY);
        Assert.NotNull(shore.FloorRules);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, shore.Floors.Select(f => f.Floor).ToArray());
        Assert.Equal(new[] { "U", "G", "1", "2", "3" }, shore.Floors.Select(f => f.Caption).ToArray());
        Assert.Equal(new[] { "Underground", "Ground", "First", "Second", "Third" }, shore.Floors.Select(f => f.Tip).ToArray());
        Assert.Contains("Shebuka", shore.Credit);
        Assert.Equal(504, shore.X0, 3);
        Assert.Equal(-415, shore.Z0, 3);
        Assert.Equal(-1056, shore.X1, 3);
        Assert.Equal(618, shore.Z1, 3);
        foreach (var plan in shore.Floors)
        {
            Assert.Equal(3119, plan.Width);
            Assert.Equal(2065, plan.Height);
            var (w, h) = PngSize(plan.ImagePath);
            Assert.Equal(3119, w);
            Assert.Equal(2065, h);
        }

        var overview = File.ReadAllBytes(Path.Combine(MapsDir(), "images", "shoreline.png"));
        Assert.Equal(overview, File.ReadAllBytes(shore.Floors.Single(f => f.Floor == 1).ImagePath));

        const double resortX = -252.25;
        const double resortZ = -117.35;
        Assert.Equal(4, InteriorCatalog.FloorFromRule(shore, resortX, 3, resortZ));
        Assert.Equal(4, InteriorCatalog.FloorFromRule(shore, resortX, 2, resortZ));
        Assert.Equal(3, InteriorCatalog.FloorFromRule(shore, resortX, 1.999, resortZ));
        Assert.Equal(3, InteriorCatalog.FloorFromRule(shore, resortX, -1, resortZ));
        Assert.Equal(2, InteriorCatalog.FloorFromRule(shore, resortX, -1.001, resortZ));
        Assert.Equal(2, InteriorCatalog.FloorFromRule(shore, resortX, -5, resortZ));
        // Below -5 inside the resort, but outside both underground boxes, stays on the ground floor.
        Assert.Equal(1, InteriorCatalog.FloorFromRule(shore, resortX, -6, resortZ));
        Assert.Equal(2, InteriorCatalog.FloorFromRule(shore, -187, -5, -86));
        Assert.Equal(0, InteriorCatalog.FloorFromRule(shore, -187, -5.001, -86));
        Assert.Equal(0, InteriorCatalog.FloorFromRule(shore, -197.04, -5.96, -79.97));
        Assert.Equal(0, InteriorCatalog.FloorFromRule(shore, -256.87, -7.71, -149.78));
        Assert.Equal(0, InteriorCatalog.FloorFromRule(shore, -195.13, -9.83, -82.93));
        Assert.Equal(1, InteriorCatalog.FloorFromRule(shore, 0, 40, 200));
        Assert.Equal(1, InteriorCatalog.FloorFromRule(shore, -496, 80, 257));
        Assert.Equal(1, InteriorCatalog.FloorFromRule(shore, -296.95, -6.63, -100.89));
        Assert.Equal(1, InteriorCatalog.FloorFromRule(shore, -373.9, 10, -117));
        Assert.Equal(4, InteriorCatalog.FloorFromRule(shore, -373.8, 10, -117));
        Assert.Null(InteriorCatalog.FloorFromRule(shore, resortX, double.NaN, resortZ));

        var onMap = InteriorCatalog.ForMap(Load(), Game("Shoreline", "shoreline", "maps/shoreline_preset.bundle"));
        Assert.Single(onMap);

        var state = InteriorFix.Overview;
        (state, var building, var floor) = InteriorCatalog.Advance(onMap, state, 376.36, -55.94, 319.25);
        Assert.Equal("shoreline", building!.Id);
        Assert.Equal(1, floor!.Floor);
        Assert.Null(state.LockBand);
        Assert.Equal(new[] { "U", "G", "1", "2", "3" }, InteriorCatalog.FloorOptions(building, 1)!.Select(o => o.Caption).ToArray());
        Assert.Equal("Ground", InteriorCatalog.FloorOptions(building, 1)!.Single(o => o.Selected).Tip);

        (state, _, floor) = InteriorCatalog.Advance(onMap, InteriorFix.Overview, 0, double.NaN, 200);
        Assert.Equal(1, floor!.Floor);

        state = InteriorFix.Overview;
        (state, _, floor) = InteriorCatalog.Advance(onMap, state, 0, 40, 200);
        Assert.Equal(1, floor!.Floor);
        state = InteriorCatalog.PinFloor(onMap, state, 4, 0, 40, 200);
        Assert.Equal(1, state.LockBand);
        (state, _, floor) = InteriorCatalog.Advance(onMap, state, -496, 15, 257);
        Assert.Equal(4, floor!.Floor);
        Assert.Equal(1, state.LockBand);

        (state, _, floor) = InteriorCatalog.Advance(onMap, state, resortX, 0, resortZ);
        Assert.Equal(3, floor!.Floor);
        Assert.Null(state.LockBand);

        state = InteriorCatalog.PinFloor(onMap, state, 2, resortX, 0, resortZ);
        Assert.Equal(3, state.LockBand);
        (state, _, floor) = InteriorCatalog.Advance(onMap, state, resortX, 1.5, resortZ);
        Assert.Equal(2, floor!.Floor);
        Assert.Equal(3, state.LockBand);
        (state, _, floor) = InteriorCatalog.Advance(onMap, state, resortX, 2.5, resortZ);
        Assert.Equal(4, floor!.Floor);
        Assert.Null(state.LockBand);

        (state, _, floor) = InteriorCatalog.Advance(onMap, state, -187, -6, -86);
        Assert.Equal(0, floor!.Floor);
        state = InteriorCatalog.PinFloor(onMap, state, 2, -187, -6, -86);
        (state, _, floor) = InteriorCatalog.Advance(onMap, state, -256.87, -7.71, -149.78);
        Assert.Equal(2, floor!.Floor);
        Assert.Equal(0, state.LockBand);

        Assert.True(InteriorCatalog.ExtractOnFloor(shore, -256.87, -7.71, -149.78, 0));
        Assert.False(InteriorCatalog.ExtractOnFloor(shore, -256.87, -7.71, -149.78, 1));
        Assert.True(InteriorCatalog.ExtractOnFloor(shore, -296.95, -6.63, -100.89, 1));
        Assert.False(InteriorCatalog.ExtractOnFloor(shore, -296.95, -6.63, -100.89, 0));
        Assert.True(InteriorCatalog.ExtractOnFloor(shore, 0, 40, 200, 1));
        Assert.False(InteriorCatalog.ExtractOnFloor(shore, 0, 40, 200, 4));
        Assert.True(InteriorCatalog.ExtractOnFloor(shore, resortX, 3, resortZ, 4));
        Assert.False(InteriorCatalog.ExtractOnFloor(shore, resortX, 3, resortZ, 1));

        Assert.True(shore.TryUv(376.36, 319.25, out var u, out var v));
        Assert.Equal((504 - 376.36) / 1560.0, u, 4);
        Assert.Equal((319.25 + 415) / 1033.0, v, 4);
        Assert.True(shore.TryUv(-859.05, 0.49, out var roadU, out _));
        Assert.True(u < roadU);
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
