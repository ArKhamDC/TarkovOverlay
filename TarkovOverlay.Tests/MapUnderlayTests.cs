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
    public void LabsRasterUsesTheFittedFrameAndKeycardColors()
    {
        var lab = Load().Single(m => m.Id == "the-lab");
        Assert.Equal(270, lab.Rotation);
        Assert.Equal(270, lab.CompassDegrees);
        Assert.Equal(-305.23, lab.MinX, 3);
        Assert.Equal(-78.1, lab.MaxX, 3);
        Assert.Equal(-466.72, lab.MinZ, 3);
        Assert.Equal(-187.66, lab.MaxZ, 3);
        Assert.Contains("Shebuka", lab.Credit);
        var (width, height) = PngSize(lab.ImagePath);
        Assert.Equal(2880, width);
        Assert.Equal(2344, height);

        Assert.True(WorldMath.TryImageUv(-165.25, -349.22, lab.MinX, lab.MaxX, lab.MinZ, lab.MaxZ, lab.Rotation, out var u, out var v));
        Assert.InRange(u, 303.2 / 720 - 0.004, 303.2 / 720 + 0.004);
        Assert.InRange(v, 361.2 / 586 - 0.004, 361.2 / 586 + 0.004);

        Assert.Equal(7, lab.Extracts.Length);
        var hangar = lab.Extracts.Single(e => e.NameEn == "Hangar Gate");
        Assert.Equal("Ангар", hangar.NameRu);
        Assert.Equal("pmc", hangar.Faction);
        Assert.Equal(5.54, hangar.Y, 2);
        var medical = lab.Extracts.Single(e => e.NameEn == "Medical Block Elevator");
        Assert.Equal("Лифт медблока", medical.NameRu);
        Assert.Equal("shared", medical.Faction);
        var sewage = lab.Extracts.Single(e => e.NameEn == "Sewage Conduit");
        Assert.Equal("Накопительный коллектор", sewage.NameRu);
        foreach (var extract in lab.Extracts)
        {
            Assert.True(WorldMath.TryImageUv(extract.X, extract.Z, lab.MinX, lab.MaxX, lab.MinZ, lab.MaxZ, lab.Rotation, out var eu, out var ev));
            Assert.InRange(eu, 0, 1);
            Assert.InRange(ev, 0, 1);
            Assert.False(string.IsNullOrWhiteSpace(extract.NameRu));
        }

        Assert.Equal(12, lab.Keycards.Length);
        var red = lab.Keycards.Single(k => k.Name == "Red");
        var violet = lab.Keycards.Single(k => k.Name == "Violet");
        Assert.Equal("#c53030", red.Fill);
        Assert.Equal("#6b46c1", violet.Fill);
        Assert.NotEqual(red.Fill, violet.Fill);
        Assert.Equal("5c1d0efb86f7744baf2e7b7b", red.ItemId);
        Assert.Equal("5c1e495a86f7743109743dfb", violet.ItemId);
        Assert.Equal("#1a1a1a", lab.Keycards.Single(k => k.Name == "Black").Fill);
        Assert.Equal(4, lab.Keycards.Single(k => k.Name == "Black").Count);
        var unknown = lab.Keycards.Single(k => k.Name == "Unknown");
        Assert.False(unknown.RoomFill);
        Assert.Equal("#a0aec0", unknown.Fill);
        Assert.Equal("6a33c17933cff6b88c08902e", unknown.ItemId);
        Assert.Contains("low confidence", lab.Keycards.Single(k => k.Name == "Arsenal").Tip, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(lab.Keycards, k => k.Name.Contains("admin", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            new[] { "#2b6cb0", "#2f855a", "#1a1a1a", "#c53030", "#6b46c1", "#d69e2e", "#d53f8c", "#63b3ed", "#dd6b20", "#8b5a2b", "#319795", "#a0aec0" },
            lab.Keycards.Select(k => k.Fill).ToArray());
    }

    [Fact]
    public void SvgOverviewsReplaceThePlaceholderRasters()
    {
        var maps = Load();
        var expected = new Dictionary<string, (int W, int H, int Extracts)>
        {
            ["woods"] = (2946, 2870, 24),
            ["lighthouse"] = (1575, 2560, 12),
            ["shoreline"] = (2560, 1695, 19),
            ["reserve"] = (2560, 2322, 11),
            ["interchange"] = (2560, 2106, 9),
            ["streets-of-tarkov"] = (1867, 2560, 19),
            ["terminal"] = (2174, 2560, 0),
        };

        foreach (var (id, size) in expected)
        {
            var map = maps.Single(m => m.Id == id);
            Assert.Contains("Shebuka", map.Credit);
            var (width, height) = PngSize(map.ImagePath);
            Assert.Equal(size.W, width);
            Assert.Equal(size.H, height);
            Assert.True(new FileInfo(map.ImagePath).Length > 40_000, id);
            Assert.Equal(size.Extracts, map.Extracts.Length);
            foreach (var extract in map.Extracts)
            {
                Assert.False(string.IsNullOrWhiteSpace(extract.NameRu), extract.NameEn);
                Assert.True(WorldMath.TryImageUv(extract.X, extract.Z, map.MinX, map.MaxX, map.MinZ, map.MaxZ, map.Rotation, out var u, out var v));
                Assert.InRange(u, 0, 1);
                Assert.InRange(v, 0, 1);
            }
        }

        var factory = maps.Single(m => m.Id == "factory");
        var courtyard = factory.Extracts.Single(e => e.NameEn == "Courtyard Gate");
        Assert.Equal("Ворота во дворе", courtyard.NameRu);
        Assert.Equal("pmc", courtyard.Faction);
        Assert.Contains(factory.Extracts, e => e.NameEn == "Gate 2");
        var night = Match(maps, "factory4_night", "night-factory", "maps/factory_night_preset.bundle");
        Assert.Equal("factory", night.Id);
        Assert.Equal(10, night.Extracts.Length);

        var woods = maps.Single(m => m.Id == "woods");
        var zb = woods.Extracts.Single(e => e.NameEn == "ZB-016");
        Assert.Equal("ЗБ-016", zb.NameRu);
        var ruaf = woods.Extracts.Single(e => e.NameEn == "RUAF Gate" && e.Faction == "pmc");
        var (ruafU, ruafV) = Uv(woods, ruaf);
        Assert.InRange(ruafV, 0.9, 1);

        var flare = maps.Single(m => m.Id == "interchange").Extracts.Single(e => e.NameEn == "Path to River (Flare)");
        Assert.Equal("Тропа к реке (Сигнал)", flare.NameRu);
        Assert.Equal(613.9, flare.X);

        var level21 = Match(maps, "Sandbox_high", "ground-zero-21", "maps/sandbox_high_preset.bundle");
        Assert.Equal("ground-zero", level21.Id);

        var terminal = maps.Single(m => m.Id == "terminal");
        Assert.Empty(terminal.Extracts);
        Assert.Contains("Shebuka", terminal.Credit);

        var labyrinth = maps.Single(m => m.Id == "the-labyrinth");
        Assert.Null(labyrinth.Credit);
        Assert.Empty(labyrinth.Extracts);
        Assert.True(new FileInfo(labyrinth.ImagePath).Length < 30_000);

        var customs = maps.Single(m => m.Id == "customs");
        var lab = maps.Single(m => m.Id == "the-lab");
        Assert.Equal((1400, 705), PngSize(customs.ImagePath));
        Assert.Equal((2880, 2344), PngSize(lab.ImagePath));
        Assert.Equal(28, customs.Extracts.Length);
        Assert.Equal(7, lab.Extracts.Length);
    }

    [Fact]
    public void GroundZeroRasterUsesTheOfficialFrameAndDoorKeys()
    {
        var map = Load().Single(m => m.Id == "ground-zero");
        Assert.Equal(180, map.Rotation);
        Assert.Equal(180, map.CompassDegrees);
        Assert.Equal(-99, map.MinX);
        Assert.Equal(249, map.MaxX);
        Assert.Equal(-124, map.MinZ);
        Assert.Equal(364, map.MaxZ);
        Assert.Contains("Shebuka", map.Credit);
        var (width, height) = PngSize(map.ImagePath);
        Assert.Equal(2094, width);
        Assert.Equal(2931, height);

        // Customs-style rotation: image u is (249-x)/348. An x+2 shift is not this frame.
        Assert.True(WorldMath.TryImageUv(107.05, 50.64, map.MinX, map.MaxX, map.MinZ, map.MaxZ, map.Rotation, out var u, out var v));
        Assert.Equal((249 - 107.05) / 348.0, u, 6);
        Assert.Equal((50.64 + 124) / 488.0, v, 6);
        Assert.True(WorldMath.TryImageUv(107.05 + 2, 50.64, map.MinX, map.MaxX, map.MinZ, map.MaxZ, map.Rotation, out var shifted, out _));
        Assert.NotEqual(u, shifted);

        Assert.Equal(9, map.Extracts.Length);
        var emercom = map.Extracts.Single(e => e.NameEn == "Emercom Checkpoint");
        Assert.Equal("Пункт МЧС", emercom.NameRu);
        Assert.Equal("shared", emercom.Faction);
        Assert.Equal(151.63, emercom.X);
        Assert.Equal(24.66, emercom.Y, 2);
        Assert.Equal(-97.46, emercom.Z);
        var nakatani = map.Extracts.Single(e => e.NameEn == "Nakatani Basement Stairs");
        Assert.Equal("Спуск в подвал в Nakatani", nakatani.NameRu);
        Assert.Equal(14.76, nakatani.Y, 2);
        var bunker = map.Extracts.Single(e => e.NameEn == "Scav Bunker");
        Assert.Equal("scav", bunker.Faction);
        Assert.Equal("Бункер Диких", bunker.NameRu);
        Assert.DoesNotContain(map.Labels, l => l.Text == "Scav Bunker");
        Assert.Equal(8, map.Labels.Length);
        Assert.Contains(map.Labels, l => l.Text == "Emercom Checkpoint" && l.TextRu == "Пункт МЧС");

        var outside = new[] { "Pinewood Basement (Co-Op)", "UN Roadblock", "Scav Bunker" };
        foreach (var extract in map.Extracts)
        {
            Assert.False(string.IsNullOrWhiteSpace(extract.NameRu));
            var onImage = WorldMath.TryImageUv(extract.X, extract.Z, map.MinX, map.MaxX, map.MinZ, map.MaxZ, map.Rotation, out var eu, out var ev)
                          && eu is >= 0 and <= 1 && ev is >= 0 and <= 1;
            Assert.Equal(!outside.Contains(extract.NameEn), onImage);
        }

        Assert.Equal(5, map.Keycards.Length);
        Assert.Equal(
            new[] { "#ff8c1a", "#ff4d4d", "#3d9bff", "#3ddc5a", "#b05cff" },
            map.Keycards.Select(k => k.Fill).ToArray());
        Assert.All(map.Keycards, k => Assert.False(k.RoomFill));
        Assert.All(map.Keycards, k => Assert.Equal(1, k.Count));
        Assert.Equal("5448ba0b4bdc2d02308b456c", map.Keycards.Single(k => k.Name == "Factory exit").ItemId);
        Assert.Contains("outside the frame", map.Keycards.Single(k => k.Name == "Factory exit").Tip, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("658199aa38c79576a2569e13", map.Keycards.Single(k => k.Name == "Science office").ItemId);
        Assert.DoesNotContain(map.Keycards, k => k.Name.Contains("stairs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WoodsRasterUsesThePackFrameAndDoorKeys()
    {
        var woods = Load().Single(m => m.Id == "woods");
        Assert.Equal(180, woods.Rotation);
        Assert.Equal(180, woods.CompassDegrees);
        Assert.Equal(-761, woods.MinX);
        Assert.Equal(646, woods.MaxX);
        Assert.Equal(-914, woods.MinZ);
        const double z1 = 455.841605;
        Assert.Equal(z1, woods.MaxZ, 4);
        Assert.Contains("Shebuka", woods.Credit);
        var (width, height) = PngSize(woods.ImagePath);
        Assert.Equal(2946, width);
        Assert.Equal(2870, height);

        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(MapsDir(), "underlays.json"))))
        {
            var map = doc.RootElement.GetProperty("maps").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "woods");
            var published = map.GetProperty("publishedBounds");
            Assert.Equal(442, published[1][1].GetDouble());
            Assert.Equal(646, published[0][0].GetDouble());
        }

        // Published south edge stays on the original raster. The pad below it is empty.
        var (_, edgeV) = Uv(woods, 0, 442);
        Assert.Equal(2841.0 / 2870.0, edgeV, 4);

        // Rotation 180: u=(646-x)/1407. ZB-014's door is the magenta marker.
        Assert.True(WorldMath.TryImageUv(448.62, 65.58, woods.MinX, woods.MaxX, woods.MinZ, woods.MaxZ, woods.Rotation, out var lockU, out var lockV));
        Assert.Equal((646 - 448.62) / 1407.0, lockU, 4);
        Assert.InRange(lockU, 0, 1);
        Assert.InRange(lockV, 0, 1);
        var (_, shatunV) = Uv(woods, -513.13, -388.48);
        Assert.True(shatunV < lockV);

        Assert.Equal(24, woods.Extracts.Length);
        Assert.Equal(20, woods.Extracts.Count(e => e.Faction is "pmc" or "scav" or "shared"));
        Assert.Equal(4, woods.Extracts.Count(e => e.Faction == "transit"));
        var gate = woods.Extracts.Single(e => e.NameEn == "RUAF Gate");
        Assert.Equal("Ворота ВС РФ", gate.NameRu);
        Assert.Equal("pmc", gate.Faction);
        Assert.Equal(-142.5, gate.X);
        Assert.Equal(-2.37, gate.Y, 2);
        Assert.Equal(447.58, gate.Z);
        var flare = woods.Extracts.Single(e => e.NameEn == "Power Line Passage (Flare)");
        Assert.Equal("Проход у ЛЭП (Сигнал)", flare.NameRu);
        Assert.Equal(-1.75, flare.Y, 2);
        var bridge = woods.Extracts.Single(e => e.NameEn == "Friendship Bridge (Co-Op)");
        Assert.Equal("Мост дружбы (Совм.)", bridge.NameRu);
        Assert.Equal("shared", bridge.Faction);
        var bunker = woods.Extracts.Single(e => e.NameEn == "Scav Bunker");
        Assert.Equal("Бункер Диких", bunker.NameRu);
        Assert.Equal("scav", bunker.Faction);
        var customs = woods.Extracts.Single(e => e.NameEn == "Transit to Customs");
        Assert.Equal("Переход на Таможню", customs.NameRu);
        Assert.Equal("transit", customs.Faction);
        Assert.Equal("41", customs.Id);
        foreach (var extract in woods.Extracts)
        {
            Assert.False(string.IsNullOrWhiteSpace(extract.NameRu));
            var (eu, ev) = Uv(woods, extract);
            Assert.InRange(eu, 0, 1);
            Assert.InRange(ev, 0, 1);
            if (extract.NameEn == "RUAF Gate")
                Assert.True(ev > edgeV, "RUAF Gate sits in the padded strip, past the published edge");
            else
                Assert.True(ev <= edgeV + 1e-4, extract.NameEn);
        }

        Assert.Equal(14, woods.Labels.Length);
        Assert.Contains(woods.Labels, l => l.Text == "Power Line Passage (Flare)" && l.TextRu == "Проход у ЛЭП (Сигнал)");
        Assert.Contains(woods.Labels, l => l.Text == "Transit to Factory" && l.TextRu == "Переход на Завод");
        Assert.Single(woods.Labels, l => l.Text == "UN Roadblock");
        Assert.Single(woods.Labels, l => l.Text == "Outskirts");
        Assert.DoesNotContain(woods.Labels, l => l.Text is "Scav Bunker" or "Eastern Rocks" or "Sawmill" or "Cultist Village" or "wood_sniper_exit" or "East Gate");

        Assert.Equal(4, woods.Keycards.Length);
        Assert.Equal(
            new[] { "#ff3f3f", "#3f77ff", "#afff3f", "#ff3fe7" },
            woods.Keycards.Select(k => k.Fill).ToArray());
        Assert.All(woods.Keycards, k => Assert.False(k.RoomFill));
        Assert.All(woods.Keycards, k => Assert.Equal(1, k.Count));
        Assert.Equal("6761a6ccd9bbb27ad703c48a", woods.Keycards.Single(k => k.Name == "Old house room").ItemId);
        Assert.Equal("6939948dc2ceb9acd7082eb2", woods.Keycards.Single(k => k.Name == "Old house toilet").ItemId);
        Assert.Equal("664d3db6db5dea2bad286955", woods.Keycards.Single(k => k.Name == "Shatun's hideout").ItemId);
        Assert.Equal("591afe0186f77431bd616a11", woods.Keycards.Single(k => k.Name == "ZB-014").ItemId);
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
    public void FactoryRasterUsesThePublishedFrameAndKeyColors()
    {
        var factory = Load().Single(m => m.Id == "factory");
        Assert.Equal(90, factory.Rotation);
        Assert.Equal(90, factory.CompassDegrees);
        Assert.Equal(-65.5, factory.MinX, 3);
        Assert.Equal(77, factory.MaxX, 3);
        Assert.Equal(-64.5, factory.MinZ, 3);
        Assert.Equal(67.4, factory.MaxZ, 3);
        Assert.Contains("Shebuka", factory.Credit);
        var (width, height) = PngSize(factory.ImagePath);
        Assert.Equal(1047, width);
        Assert.Equal(1130, height);

        Assert.True(WorldMath.TryImageUv(-19.08, -48.57, factory.MinX, factory.MaxX, factory.MinZ, factory.MaxZ, factory.Rotation, out var u, out var v));
        Assert.Equal((67.4 - (-48.57)) / 131.9, u, 4);
        Assert.Equal((77 - (-19.08)) / 142.5, v, 4);

        Assert.Equal(10, factory.Extracts.Length);
        var med = factory.Extracts.Single(e => e.NameEn == "Med Tent Gate");
        Assert.Equal("Ворота у медицинской палатки", med.NameRu);
        Assert.Equal("pmc", med.Faction);
        Assert.Equal(1.99, med.Y, 2);
        var cellars = factory.Extracts.Single(e => e.NameEn == "Cellars");
        Assert.Equal("Подвалы", cellars.NameRu);
        Assert.Equal(-3.29, cellars.Y, 2);
        var office = factory.Extracts.Single(e => e.NameEn == "Office Window");
        Assert.Equal("Офисное окно", office.NameRu);
        Assert.Equal("scav", office.Faction);
        Assert.Equal(8.73, office.Y, 2);
        var gate3 = factory.Extracts.Where(e => e.NameEn == "Gate 3").ToArray();
        Assert.Equal(2, gate3.Length);
        Assert.Contains(gate3, e => e.Faction == "pmc" && e.Y > 3);
        Assert.Contains(gate3, e => e.Faction == "scav" && e.Y < 3);
        Assert.Contains(factory.Extracts, e => e.NameEn == "Gate 2" && e.Faction == "pmc");
        var courtyard = factory.Extracts.Single(e => e.NameEn == "Courtyard Gate");
        Assert.Equal("Ворота во дворе", courtyard.NameRu);
        foreach (var extract in factory.Extracts)
        {
            Assert.True(WorldMath.TryImageUv(extract.X, extract.Z, factory.MinX, factory.MaxX, factory.MinZ, factory.MaxZ, factory.Rotation, out var eu, out var ev));
            Assert.False(string.IsNullOrWhiteSpace(extract.NameRu));
            if (extract.NameEn == "Courtyard Gate")
            {
                Assert.InRange(eu, -0.02, 0);
                Assert.InRange(ev, 0, 1);
            }
            else
            {
                Assert.InRange(eu, 0, 1);
                Assert.InRange(ev, 0, 1);
            }
        }

        Assert.Equal(7, factory.Labels.Length);
        Assert.Contains(factory.Labels, l => l.Text == "Cellars" && l.TextRu == "Подвалы");
        Assert.DoesNotContain(factory.Labels, l => l.Text == "Office Window");

        Assert.Equal(2, factory.Keycards.Length);
        var emergency = factory.Keycards.Single(k => k.Name == "Emergency exit");
        var storage = factory.Keycards.Single(k => k.Name == "TerraGroup storage");
        Assert.Equal("#ff8c1a", emergency.Fill);
        Assert.Equal(3, emergency.Count);
        Assert.False(emergency.RoomFill);
        Assert.Equal("5448ba0b4bdc2d02308b456c", emergency.ItemId);
        Assert.Equal("#b05cff", storage.Fill);
        Assert.Equal(1, storage.Count);
        Assert.False(storage.RoomFill);
        Assert.Equal("66acd6702b17692df20144c0", storage.ItemId);
        Assert.DoesNotContain(factory.Keycards, k => k.Name.Contains("stairs", StringComparison.OrdinalIgnoreCase));
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
