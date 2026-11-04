using TarkovOverlay.Services;
using Xunit;

namespace TarkovOverlay.Tests;

public class MapCameraTests
{
    private const double W = 400;
    private const double H = 280;

    [Fact]
    public void ZoomOneAtFrameCenterMatchesTheLetterbox()
    {
        var fit = Fit(-372, 698, -307, 237, 180);
        var (cx, cz) = fit.FromScreen(W / 2, H / 2);
        var view = MapCamera.Apply(fit, W, H, 1, cx, cz);
        Assert.Equal(fit.OriginX, view.OriginX, 6);
        Assert.Equal(fit.OriginY, view.OriginY, 6);
        Assert.Equal(fit.Scale, view.Scale, 6);
        Assert.Equal(fit.UsedW, view.UsedW, 6);
        Assert.Equal(fit.UsedH, view.UsedH, 6);

        var (bx, by) = fit.ToScreen(200, 150);
        var (sx, sy) = view.ToScreen(200, 150);
        Assert.Equal(bx, sx, 5);
        Assert.Equal(by, sy, 5);
        var back = view.FromScreen(sx, sy);
        Assert.Equal(200, back.X, 4);
        Assert.Equal(150, back.Z, 4);
    }

    [Fact]
    public void ProjectRoundTripAndRotatedFrameStayOnOneProjection()
    {
        var back = WorldMath.Unproject(WorldMath.Project(12.5, -4, 37).X, WorldMath.Project(12.5, -4, 37).Y, 37);
        Assert.Equal(12.5, back.X, 6);
        Assert.Equal(-4, back.Z, 6);

        var fit = Fit(-10, 90, 5, 40, 37);
        var (cx, cz) = fit.FromScreen(W / 2, H / 2);
        var view = MapCamera.Apply(fit, W, H, 2.5, cx, cz);
        foreach (var (x, z) in new[] { (-10d, 5d), (90d, 40d), (20d, 15d) })
        {
            Assert.True(WorldMath.TryImageUv(x, z, -10, 90, 5, 40, 37, out var u, out var v));
            var (sx, sy) = view.ToScreen(x, z);
            Assert.Equal(view.OriginX + u * view.UsedW, sx, 4);
            Assert.Equal(view.OriginY + v * view.UsedH, sy, 4);
        }
    }

    [Fact]
    public void AnchorStaysCenteredAndZoomScalesAroundIt()
    {
        var fit = Fit(-372, 698, -307, 237, 180);
        const double ax = 180;
        const double az = 40;
        var view = MapCamera.Apply(fit, W, H, 3, ax, az);
        var at = view.ToScreen(ax, az);
        Assert.Equal(W / 2, at.X, 3);
        Assert.Equal(H / 2, at.Y, 3);
        Assert.Equal(fit.Scale * 3, view.Scale, 5);
        Assert.Equal(fit.UsedW * 3, view.UsedW, 4);

        var other = view.ToScreen(220, 40);
        var (nax, naz, zoom) = MapCamera.ZoomAbout(fit, W, H, 3, ax, az, at.X, at.Y, 3 * MapCamera.Step);
        var zoomed = MapCamera.Apply(fit, W, H, zoom, nax, naz);
        var at2 = zoomed.ToScreen(ax, az);
        Assert.Equal(at.X, at2.X, 3);
        Assert.Equal(at.Y, at2.Y, 3);
        Assert.Equal(3 * MapCamera.Step, zoom, 5);
        var other2 = zoomed.ToScreen(220, 40);
        Assert.Equal((other.X - at.X) * MapCamera.Step, other2.X - at2.X, 3);
        Assert.Equal((other.Y - at.Y) * MapCamera.Step, other2.Y - at2.Y, 3);
    }

    [Fact]
    public void ZoomAboutPlayerKeepsThatMarkerFixedWhenItIsOffCenter()
    {
        var fit = Fit(-372, 698, -307, 237, 180);
        var view = MapCamera.Apply(fit, W, H, 2, 0, 0);
        var (px, pz) = view.FromScreen(W / 2 + 30, H / 2 - 12);
        var player = view.ToScreen(px, pz);
        var (nax, naz, zoom) = MapCamera.ZoomAbout(fit, W, H, 2, 0, 0, player.X, player.Y, 4);
        var zoomed = MapCamera.Apply(fit, W, H, zoom, nax, naz);
        var after = zoomed.ToScreen(px, pz);
        Assert.Equal(player.X, after.X, 3);
        Assert.Equal(player.Y, after.Y, 3);
        Assert.Equal(4, zoom, 5);
    }

    [Fact]
    public void ZoomClampsToTheRaidRange()
    {
        Assert.Equal(1, MapCamera.ClampZoom(0.2));
        Assert.Equal(MapCamera.MaxZoom, MapCamera.ClampZoom(40));
        Assert.Equal(1, MapCamera.ClampZoom(double.NaN));
        var fit = Fit(-372, 698, -307, 237, 180);
        var (ax, az, zoom) = MapCamera.ZoomAbout(fit, W, H, 2, 10, 10, W / 2, H / 2, 100);
        Assert.Equal(MapCamera.MaxZoom, zoom);
        var at = MapCamera.Apply(fit, W, H, zoom, ax, az).ToScreen(10, 10);
        Assert.Equal(W / 2, at.X, 3);
        Assert.Equal(H / 2, at.Y, 3);
    }

    [Fact]
    public void PanMovesTheAnchorOppositeThePointer()
    {
        var fit = Fit(-120, 140, -80, 90, 15);
        var before = MapCamera.Apply(fit, W, H, 2, 10, -5);
        var (worldX, worldZ) = before.FromScreen(W / 2 - 36, H / 2 + 18);
        var (ax, az) = MapCamera.Pan(fit, W, H, 2, 10, -5, 36, -18);
        var after = MapCamera.Apply(fit, W, H, 2, ax, az);
        var center = after.ToScreen(worldX, worldZ);
        Assert.Equal(W / 2, center.X, 3);
        Assert.Equal(H / 2, center.Y, 3);
        var player = after.ToScreen(10, -5);
        Assert.Equal(W / 2 + 36, player.X, 3);
        Assert.Equal(H / 2 - 18, player.Y, 3);
    }

    [Fact]
    public void FollowIgnoresJitterInsideTheDeadzoneAndRecentersOutsideIt()
    {
        var fit = Fit(-372, 698, -307, 237, 180);
        const double zoom = 3;
        var centered = MapCamera.Apply(fit, W, H, zoom, 50, -20);
        var (jx, jz) = centered.FromScreen(W / 2 + 10, H / 2 - 6);
        var held = MapCamera.Follow(fit, W, H, zoom, 50, -20, jx, jz, MapCamera.DeadzonePixels);
        Assert.Equal(50, held.X, 5);
        Assert.Equal(-20, held.Z, 5);

        var (fx, fz) = centered.FromScreen(W / 2 + 48, H / 2);
        var pulled = MapCamera.Follow(fit, W, H, zoom, 50, -20, fx, fz, MapCamera.DeadzonePixels);
        Assert.Equal(fx, pulled.X, 4);
        Assert.Equal(fz, pulled.Z, 4);
        var at = MapCamera.Apply(fit, W, H, zoom, pulled.X, pulled.Z).ToScreen(fx, fz);
        Assert.Equal(W / 2, at.X, 3);
        Assert.Equal(H / 2, at.Y, 3);

        var (pax, paz) = MapCamera.Pan(fit, W, H, zoom, fx, fz, 80, 0);
        var resumed = MapCamera.Follow(fit, W, H, zoom, pax, paz, fx, fz, MapCamera.DeadzonePixels);
        var back = MapCamera.Apply(fit, W, H, zoom, resumed.X, resumed.Z).ToScreen(fx, fz);
        Assert.Equal(W / 2, back.X, 3);
        Assert.Equal(H / 2, back.Y, 3);
    }

    [Fact]
    public void DefaultZoomIsLocalOnCustomsAndTighterThanAFullBuildingFit()
    {
        var customs = Fit(-372, 698, -307, 237, 180);
        var overview = MapCamera.DefaultZoom(customs, W, H);
        var overviewMeters = Math.Min(W, H) / (customs.Scale * overview);
        Assert.InRange(overview, MapCamera.MinZoom + 0.5, MapCamera.MaxZoom);
        Assert.InRange(overviewMeters, 180, 260);

        var dorm = Fit(164.25, 201.51, 144.32, 191.08, 180);
        var interior = MapCamera.DefaultZoom(dorm, W, H);
        Assert.Equal(MapCamera.TightFrameZoom, interior, 5);
        var visible = Math.Min(W, H) / (dorm.Scale * interior);
        var frameShort = Math.Min(dorm.UsedW, dorm.UsedH) / dorm.Scale;
        Assert.True(visible < frameShort * 0.75, $"interior still shows {visible:0} m of a {frameShort:0} m frame");
    }

    [Fact]
    public void ShippedDormFloorUsesTheSameZoomedProjectionForRoomsAndWorldPoints()
    {
        var dorm = LoadDorms();
        var floor = dorm.Floors.Single(f => f.Floor == 1);
        var basis = Fit(dorm.MinX, dorm.MaxX, dorm.MinZ, dorm.MaxZ, dorm.Rotation);
        var zoom = MapCamera.DefaultZoom(basis, W, H);
        Assert.True(zoom > 1);
        var playerX = (dorm.X0 + dorm.X1) / 2;
        var playerZ = (dorm.Z0 + dorm.Z1) / 2;
        var view = MapCamera.Apply(basis, W, H, zoom, playerX, playerZ);
        var player = view.ToScreen(playerX, playerZ);
        Assert.Equal(W / 2, player.X, 3);
        Assert.Equal(H / 2, player.Y, 3);

        Assert.True(dorm.TryUv(dorm.X0, dorm.Z0, out var u, out var v));
        var corner = view.ToScreen(dorm.X0, dorm.Z0);
        Assert.Equal(view.OriginX + u * view.UsedW, corner.X, 4);
        Assert.Equal(view.OriginY + v * view.UsedH, corner.Y, 4);

        var room = floor.Labels.Single(l => l.Text == "101").ToImage(floor.Width, floor.Height, english: true);
        var labelX = view.OriginX + room.U * view.UsedW;
        var labelY = view.OriginY + room.V * view.UsedH;
        var still = basis.OriginX + room.U * basis.UsedW;
        Assert.NotEqual(still, labelX, 1);
        Assert.InRange(labelX, Math.Min(view.OriginX, view.OriginX + view.UsedW) - 0.1, Math.Max(view.OriginX, view.OriginX + view.UsedW) + 0.1);
        Assert.InRange(labelY, Math.Min(view.OriginY, view.OriginY + view.UsedH) - 0.1, Math.Max(view.OriginY, view.OriginY + view.UsedH) + 0.1);

        var (nextX, nextZ) = MapCamera.Pan(basis, W, H, zoom, playerX, playerZ, -40, 15);
        var followed = MapCamera.Follow(basis, W, H, zoom, nextX, nextZ, playerX, playerZ, MapCamera.DeadzonePixels);
        var recentered = MapCamera.Apply(basis, W, H, zoom, followed.X, followed.Z).ToScreen(playerX, playerZ);
        Assert.Equal(W / 2, recentered.X, 3);
        Assert.Equal(H / 2, recentered.Y, 3);
    }

    private static MapProjection Fit(double minX, double maxX, double minZ, double maxZ, double rotation)
    {
        Assert.True(WorldMath.TryFit(minX, maxX, minZ, maxZ, rotation, W, H, out var fit));
        return fit;
    }

    private static InteriorBuilding LoadDorms()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "TarkovOverlay", "Maps", "interiors.json");
            if (File.Exists(candidate))
            {
                var buildings = InteriorCatalog.Load(File.ReadAllText(candidate), Path.Combine(dir.FullName, "TarkovOverlay", "Maps", "interiors"));
                return buildings.Single(b => b.Id == "dorms_3story");
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("interiors.json was not found from the test output directory");
    }
}
