using TarkovOverlay.Models;
using TarkovOverlay.Services;
using Xunit;

namespace TarkovOverlay.Tests;

public class GpsTests
{
    private const string Sample = "2026-08-05[14-30]_300.4, 2.1, -50.7_0.0, 0.7, 0.0, 0.7_5.43 (0).png";
    private const string Blog = "2026-09-04[18-33]_7.86, 38.06, -27.57_-0.03307, -0.13322, 0.00384, -0.99053_21.87 (0).png";

    [Fact]
    public void ParsesPositionAndFacingFromFileName()
    {
        Assert.True(ScreenshotNameParser.TryParse(Sample, out var fix));
        Assert.Equal(300.4, fix.Position.X);
        Assert.Equal(2.1, fix.Position.Y);
        Assert.Equal(-50.7, fix.Position.Z);
        Assert.Equal(0.7, fix.Rotation.Y);
        Assert.Equal(0.7, fix.Rotation.W);
        Assert.Equal(5.43, fix.InGameTime);
        Assert.InRange(WorldMath.HeadingDegrees(fix.Rotation), 89, 91);
    }

    [Fact]
    public void ParsesBlogQuaternionWithoutOpeningTheImage()
    {
        Assert.True(ScreenshotNameParser.TryParse(@"C:\shots\" + Blog, out var fix));
        Assert.Equal(7.86, fix.Position.X);
        Assert.Equal(-27.57, fix.Position.Z);
        Assert.InRange(WorldMath.HeadingDegrees(fix.Rotation), 14.5, 16.2);
    }

    [Theory]
    [InlineData("notes.png")]
    [InlineData("2026-08-05[14-30].png")]
    [InlineData("")]
    public void RejectsNamesWithoutCoordinates(string name)
        => Assert.False(ScreenshotNameParser.TryParse(name, out _));

    [Fact]
    public void HeadingZeroFacesPositiveZAndRightIsPositive()
    {
        Assert.InRange(WorldMath.HeadingDegrees(new Quat(0, 0, 0, 1)), -0.01, 0.01);
        var bearing = WorldMath.BearingDegrees(0, 0, 10, 0);
        Assert.InRange(bearing, 89, 91);
        Assert.InRange(WorldMath.RelativeDegrees(bearing, 90), -1, 1);
        Assert.InRange(WorldMath.RelativeDegrees(10, 350), 19, 21);
    }

    [Fact]
    public void ScreenAngleMatchesFacing()
    {
        Assert.InRange(WorldMath.ScreenAngleDegrees(0, 0, 0, 0), -0.1, 0.1);
        Assert.InRange(WorldMath.ScreenAngleDegrees(5, 5, 90, 0), 89, 91);
    }

    [Fact]
    public void OverviewDefaultsToPmcAndShared()
    {
        Assert.True(ExtractNav.OverviewVisible("pmc", null));
        Assert.True(ExtractNav.OverviewVisible("shared", null));
        Assert.True(ExtractNav.OverviewVisible("PMC", "pmc"));
        Assert.False(ExtractNav.OverviewVisible("scav", null));
        Assert.False(ExtractNav.OverviewVisible("scav", "pmc"));
        Assert.True(ExtractNav.OverviewVisible("scav", "scav"));
        Assert.True(ExtractNav.OverviewVisible("shared", "scav"));
        Assert.False(ExtractNav.OverviewVisible("pmc", "scav"));
        Assert.True(ExtractNav.OverviewVisible("transit", null));
        Assert.True(ExtractNav.OverviewVisible("Transit", "pmc"));
        Assert.True(ExtractNav.OverviewVisible("transit", "scav"));
        Assert.False(ExtractNav.FactionVisible("transit", "pmc"));
        Assert.False(ExtractNav.FactionVisible("transit", "scav"));
    }

    [Fact]
    public void TransitStaysOnTheMapAndOffTheArrow()
    {
        var map = new GameMap(
            "lighthouse", "Lighthouse", "maps/lighthouse_preset.bundle", "Lighthouse", "Маяк", 180,
            -545, 515, -998, 725, ["lighthouse"],
            [
                new MapExtract("Armored Train", "Бронепоезд", "shared", 6.27, 13.34, -869.48),
                new MapExtract("Transit to Shoreline", "Переход на Берег", "transit", -338.35, 17.54, -165.53)
            ]);
        var nearest = ExtractNav.Nearest(map, new Vec3(-338, 17, -165), "pmc");
        Assert.Equal("Armored Train", nearest!.Extract.NameEn);
        Assert.True(nearest.DistanceMeters > 100);
    }

    [Fact]
    public void NearestExtractRespectsSide()
    {
        var maps = SampleMaps();
        var customs = MapIndex.Find(maps, "bigmap")!;
        var pmc = ExtractNav.Nearest(customs, new Vec3(0, 0, 0), "pmc");
        var scav = ExtractNav.Nearest(customs, new Vec3(0, 0, 0), "scav");
        Assert.Equal("ZB-1011", pmc!.Extract.NameEn);
        Assert.Equal("Old Gas Station", scav!.Extract.NameEn);
        Assert.InRange(pmc.DistanceMeters, 14, 15);
    }

    [Fact]
    public void MapTokensMatchScenePathAndRcid()
    {
        var maps = SampleMaps();
        Assert.Equal("bigmap", MapIndex.Find(maps, "maps/customs_preset.bundle")!.NameId);
        Assert.Equal("TarkovStreets", MapIndex.Find(maps, "rcid:city.scenespreset.asset")!.NameId);
        Assert.Equal("TarkovStreets", MapIndex.Find(maps, "scene preset path:maps/city_preset.bundle rcid:city.scenespreset.asset")!.NameId);
    }

    [Fact]
    public void RaidLogEntersOnGameStartedAndLeavesOnMatchOver()
    {
        var raid = new RaidLogTracker();
        raid.Ingest(
        [
            Line("2026-08-05 14:00:00.000 +00:00", "Info|application|scene preset path:maps/customs_preset.bundle rcid:bigmap.scenespreset.asset"),
            Line("2026-08-05 14:00:01.000 +00:00", "Debug|application|TRACE-NetworkGameCreate profileStatus: 'Location: bigmap, shortId: AB12CD'"),
            Line("2026-08-05 14:00:02.000 +00:00", "Info|notifications|Got notification | GroupMatchRaidSettings {\"side\":\"Pmc\"}")
        ]);
        Assert.False(raid.InRaid);
        Assert.Equal("maps/customs_preset.bundle", raid.ScenePath);
        Assert.Equal("bigmap", raid.LocationId);
        Assert.Equal("pmc", raid.SideName);

        raid.Ingest([Line("2026-08-05 14:00:08.000 +00:00", "Info|application|GameStarted")]);
        Assert.True(raid.InRaid);

        raid.Ingest([Line("2026-08-05 13:00:00.000 +00:00", "Info|application|GameStarted")]);
        Assert.True(raid.InRaid);

        raid.Ingest([Line("2026-08-05 14:20:00.000 +00:00", "Info|notifications|Got notification | UserMatchOver")]);
        Assert.False(raid.InRaid);
        Assert.Null(raid.LocationId);
    }

    [Fact]
    public void MatchingAbortAndGameStoppingLeaveTheRaid()
    {
        var raid = new RaidLogTracker();
        raid.Ingest([Line("2026-08-05 14:00:08.000 +00:00", "Info|application|GameStarting")]);
        Assert.True(raid.InRaid);
        raid.Ingest([Line("2026-08-05 14:00:09.000 +00:00", "Info|application|Network game matching aborted")]);
        Assert.False(raid.InRaid);

        raid.Ingest([Line("2026-08-05 14:10:00.000 +00:00", "Info|application|GameStarted")]);
        raid.Ingest([Line("2026-08-05 14:40:00.000 +00:00", "Error|output|EFT.NetworkGame`1:GameStopping()")]);
        Assert.False(raid.InRaid);
    }

    [Fact]
    public void ScreenshotKeyDefaultsToPrintScreen()
    {
        Assert.Equal(0x2C, ScreenshotHotkey.ToVirtualKey(null));
        Assert.Equal(0x2C, ScreenshotHotkey.ToVirtualKey("PrintScreen"));
        Assert.Equal(0x7B, ScreenshotHotkey.ToVirtualKey("F12"));
        Assert.Equal((ushort)'G', ScreenshotHotkey.ToVirtualKey("g"));
    }

    [Fact]
    public void DeleteGuardStaysInsideTheScreenshotsFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "tarkov-overlay-gps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var shot = Path.Combine(root, "a.png");
            Assert.True(ScreenshotPath.IsInside(root, shot));
            Assert.False(ScreenshotPath.IsInside(root, Path.Combine(Path.GetTempPath(), "other.png")));
        }
        finally
        {
            Directory.Delete(root);
        }
    }

    private static List<GameMap> SampleMaps()
    {
        const string maps = """
            {"data":{"maps":{
              "c1":{"id":"c1","nameId":"bigmap","normalizedName":"customs","scenePath":"maps/customs_preset.bundle","coordinateToCardinalRotation":180,
                "spawns":[{"position":{"x":0,"y":0,"z":0}},{"position":{"x":100,"y":1,"z":-50}}],
                "extracts":[
                  {"name":"ex_customs_pmc_zb1011","faction":"pmc","position":{"x":10,"y":0,"z":10}},
                  {"name":"ex_customs_scav_gasstation","faction":"scav","position":{"x":80,"y":0,"z":-40}}
                ]},
              "s1":{"id":"s1","nameId":"TarkovStreets","normalizedName":"streets-of-tarkov","scenePath":"maps/city_preset.bundle","coordinateToCardinalRotation":180,
                "spawns":[],
                "extracts":[{"name":"E2","faction":"shared","position":{"x":1,"y":2,"z":3}}]}
            }}}
            """;
        const string en = """
            {"data":{"c1 Name":"Customs","ex_customs_pmc_zb1011":"ZB-1011","ex_customs_scav_gasstation":"Old Gas Station","s1 Name":"Streets of Tarkov","E2":"Sewer River"}}
            """;
        const string ru = """
            {"data":{"c1 Name":"Таможня","ex_customs_pmc_zb1011":"ЗБ-1011","ex_customs_scav_gasstation":"Старая бензоколонка","s1 Name":"Улицы Таркова","E2":"Канализационная река"}}
            """;
        return MapDataParser.Parse(maps, en, ru);
    }

    private static RaidObservation Line(string stamp, string message)
    {
        var text = stamp + "|0.1| " + message;
        Assert.True(RaidLogTracker.TryTimestamp(text, out var when));
        return new RaidObservation(when, text);
    }
}
