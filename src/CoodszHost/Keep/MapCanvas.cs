// The live map's drawing surface. What is drawn at each zoom, the party marks and the kingdom
// names over their land follow CoopKeep's map page (https://github.com/Aderimo/bannerlord-coopkeep,
// src/CoopKeep.App/Views/Pages/MapPage.axaml); CoopKeep draws over a downloaded map picture, this
// draws everything from the server's data (see LiveMap.Territory).
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;

namespace CoodszHost.Keep;

/// <summary>
/// Draws the campaign map: territory, settlements, parties, names and a hover card. Drag pans, the
/// wheel zooms around the mouse, a click picks a settlement or party.
/// </summary>
public sealed class MapCanvas : FrameworkElement
{
    private static readonly Typeface Body = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface Italic = new(new FontFamily("Segoe UI"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Region = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Light, FontStretches.Normal);

    // The paper map (MapPaper) under ink marks and names; the hover card and the player names use the window's own colours.
    private static readonly Brush PaperEdge = Frozen(new SolidColorBrush(MapPaper.PaperColor));
    private static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0x24, 0x16)));
    private static readonly Brush InkSoft = Frozen(new SolidColorBrush(Color.FromArgb(0xC8, 0x3A, 0x2E, 0x1E)));
    private static readonly Brush PaperLight = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xEA, 0xD3)));
    private static readonly Pen GridPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x1C, 0x2E, 0x24, 0x16))), 1));
    private static readonly Pen Halo = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0xEE, 0xE5, 0xCB))), 3.2) { LineJoin = PenLineJoin.Round });
    private static readonly Pen RegionHalo = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0xEE, 0xE5, 0xCB))), 2.6) { LineJoin = PenLineJoin.Round });
    private static readonly Pen Outline = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xE6, 0x2E, 0x24, 0x16))), 1));
    private static readonly Pen ThinOutline = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0x2E, 0x24, 0x16))), 0.8));
    private static readonly Pen SiegePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xB0, 0x3A, 0x2E))), 1.8));
    private static readonly Pen PlayerPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xE2, 0xC2, 0x7A))), 1.6) { LineJoin = PenLineJoin.Round });
    private static readonly Brush PlayerGlow = Frozen(new SolidColorBrush(Color.FromArgb(0x60, 0x15, 0x1D, 0x24)));
    private static readonly Pen SelectOuter = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0xF2, 0xEA, 0xD3))), 4));
    private static readonly Pen SelectInner = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x79))), 2));
    private static readonly Pen HoverPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0x2E, 0x24, 0x16))), 1.2) { DashStyle = DashStyles.Dash });
    private static readonly Brush TextMain = Frozen(new SolidColorBrush(Color.FromRgb(0xD5, 0xDE, 0xE6)));
    private static readonly Brush TextDim = Frozen(new SolidColorBrush(Color.FromRgb(0x8E, 0xA0, 0xAE)));
    private static readonly Brush TextRed = Frozen(new SolidColorBrush(Color.FromRgb(0xE2, 0x78, 0x6C)));
    private static readonly Brush CardBack = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0x15, 0x1D, 0x24)));
    private static readonly Pen CardPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x3A, 0x4C, 0x5C))), 1));
    private static readonly Pen PlatePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x8E, 0xB4, 0xD4))), 1));

    private readonly Dictionary<uint, Brush> _brushes = new();
    private MapData _map = MapData.Empty;
    private MapLayers _layers = new();
    private IReadOnlyList<MapParty> _parties = Array.Empty<MapParty>();
    private IReadOnlyList<MapKingdomLabel> _kingdomLabels = Array.Empty<MapKingdomLabel>();
    private ImageSource? _territory;
    private MapBounds _territoryBounds;
    private ImageSource? _detail;
    private MapBounds _detailBounds;
    private MapBounds _bounds;
    private bool _fitted;
    private bool _streamer;
    private object? _hover;
    private Point _mouse;
    private Point? _down;
    private Point _last;
    private bool _dragged;

    public MapCanvas()
    {
        ClipToBounds = true;
        // The territory picture is small; smooth scaling turns its cells into soft land and borders.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    public MapCamera Camera { get; } = new();

    /// <summary>The picked settlement or party (a <see cref="MapSettlement"/> or <see cref="MapParty"/>), or null.</summary>
    public object? Selected { get; private set; }

    /// <summary>A settlement or party was clicked (null: empty map).</summary>
    public event Action<object?>? Picked;

    /// <summary>The view moved or zoomed (the page shows the zoom).</summary>
    public event Action? ViewChanged;

    /// <summary>New data from the server. The view stays where it is; the first map is fitted.</summary>
    public void SetMap(MapData map, bool streamer)
    {
        _map = map;
        _streamer = streamer;
        _bounds = LiveMap.Bounds(map);
        _kingdomLabels = LiveMap.KingdomLabels(map);
        _parties = LiveMap.PartiesToDraw(map, _layers);
        // A picked party is followed to its new place; one that is gone stays picked as it was.
        Selected = Selected switch
        {
            MapParty p => LiveMap.FindHero(map, p.HeroId) ?? p,
            MapSettlement s => map.SettlementById.TryGetValue(s.Id, out var fresh) ? fresh : s,
            _ => null,
        };
        if (!_fitted && ActualWidth > 0 && !map.IsEmpty)
            FitAll();
        InvalidateVisual();
    }

    /// <summary>A new campaign: nothing drawn, and the next map is fitted again (a mod can bring another map).</summary>
    public void Reset()
    {
        _map = MapData.Empty;
        _parties = Array.Empty<MapParty>();
        _kingdomLabels = Array.Empty<MapKingdomLabel>();
        _territory = null;
        _detail = null;
        _hover = null;
        _fitted = false;
        _pendingCenter = null;
        Selected = null;
        InvalidateVisual();
    }

    /// <summary>The territory picture over <paramref name="bounds"/> (made by the page from <see cref="LiveMap.Territory"/>).</summary>
    public void SetTerritory(ImageSource? picture, MapBounds bounds)
    {
        _territory = picture;
        _territoryBounds = bounds;
        _detail = null;
        InvalidateVisual();
    }

    /// <summary>
    /// A sharper territory picture of the part in view (close up the whole map picture shows its
    /// cells); null takes it away.
    /// </summary>
    public void SetDetail(ImageSource? picture, MapBounds bounds)
    {
        _detail = picture;
        _detailBounds = bounds;
        InvalidateVisual();
    }

    /// <summary>The part of the world in view, in game coordinates.</summary>
    public MapBounds VisibleBounds()
    {
        var (x1, y1) = Camera.ToWorld(0, Camera.ViewHeight);
        var (x2, y2) = Camera.ToWorld(Camera.ViewWidth, 0);
        return new MapBounds(x1, y1, x2, y2);
    }

    public void SetLayers(MapLayers layers)
    {
        _layers = layers;
        _parties = LiveMap.PartiesToDraw(_map, layers);
        InvalidateVisual();
    }

    public void SetStreamer(bool streamer)
    {
        _streamer = streamer;
        InvalidateVisual();
    }

    public void Select(object? item)
    {
        Selected = item;
        InvalidateVisual();
    }

    public void FitAll()
    {
        Camera.Resize(ActualWidth, ActualHeight);
        Camera.Fit(_bounds);
        _fitted = true;
        Changed();
    }

    public void ZoomBy(double factor)
    {
        Camera.ZoomAt(ActualWidth / 2, ActualHeight / 2, factor);
        Changed();
    }

    // A centre asked for while the canvas has no size yet (the page shows its state card until
    // the first map is read, so Show on map from another page lands here): done once it is laid out.
    private (double X, double Y, double Zoom)? _pendingCenter;

    public void CenterOn(double x, double y, double zoom)
    {
        if (ActualWidth < 2 || ActualHeight < 2)
        {
            // Fitting a canvas without a size gives a camera that draws the whole map squeezed.
            _pendingCenter = (x, y, zoom);
            return;
        }

        if (!_fitted)
            FitAll();
        Camera.CenterOn(x, y, zoom);
        Changed();
    }

    private void Changed()
    {
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        var (cx, cy) = Camera.ToWorld(Camera.ViewWidth / 2, Camera.ViewHeight / 2);
        Camera.Resize(ActualWidth, ActualHeight);
        if (_pendingCenter is { } pending && ActualWidth >= 2 && ActualHeight >= 2)
        {
            _pendingCenter = null;
            CenterOn(pending.X, pending.Y, pending.Zoom);
            return;
        }

        if (!_fitted && !_map.IsEmpty)
            FitAll();
        else if (_fitted)
            Camera.CenterOn(cx, cy);
        InvalidateVisual();
    }


    // --- drawing -------------------------------------------------------------

    // Screen boxes already taken by a name or an important mark this frame: a name that would
    // cover one of them is left out, so names never pile up on top of each other.
    private readonly List<Rect> _taken = new();
    private readonly List<Rect> _plates = new();

    protected override void OnRender(DrawingContext dc)
    {
        var size = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(PaperEdge, null, size);
        if (_map.IsEmpty || size.Width < 2)
            return;
        Camera.Resize(ActualWidth, ActualHeight);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var zoom = Camera.Zoom;
        _taken.Clear();
        _plates.Clear();

        if (_territory != null)
        {
            var whole = ScreenRect(_territoryBounds);
            if (_detail != null)
            {
                // The sharp picture of the part in view replaces the whole map picture there.
                var sharp = ScreenRect(_detailBounds);
                dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(size), new RectangleGeometry(sharp)));
                dc.DrawImage(_territory, whole);
                dc.Pop();
                dc.DrawImage(_detail, sharp);
            }
            else
                dc.DrawImage(_territory, whole);
        }

        DrawGrid(dc, size);


        var marks = Math.Clamp(0.85 + zoom * 0.12, 0.85, 1.8);
        var visible = new Rect(-40, -40, size.Width + 80, size.Height + 80);
        var drawnSettlements = new List<(MapSettlement S, Point At)>();
        foreach (var kind in new[] { "village", "castle", "town" })
            foreach (var s in _map.Settlements)
            {
                if (s.Kind != kind || !LiveMap.Shows(s, _layers))
                    continue;
                var (sx, sy) = Camera.ToScreen(s.X, s.Y);
                if (!visible.Contains(sx, sy))
                    continue;
                DrawSettlement(dc, s, new Point(sx, sy), marks, zoom);
                drawnSettlements.Add((s, new Point(sx, sy)));
            }

        var drawnParties = new List<(MapParty P, Point At)>();
        foreach (var p in _parties)
        {
            var (px, py) = Camera.ToScreen(p.X, p.Y);
            if (!visible.Contains(px, py))
                continue;
            if (!p.IsPlayer)
                DrawLord(dc, p, new Point(px, py), marks);
            drawnParties.Add((p, new Point(px, py)));
        }

        // Players on top of everything, with their name on a plate in the window's colours.
        foreach (var (p, at) in drawnParties.Where(d => d.P.IsPlayer))
            DrawPlayer(dc, p, at, marks);
        foreach (var (p, at) in drawnParties.Where(d => d.P.IsPlayer))
            DrawPlayerName(dc, p, at, marks, dpi);

        // Towns and castles keep their marks clear of later names.
        foreach (var (s, at) in drawnSettlements.Where(d => !d.S.IsVillage))
        {
            var r = Radius(s) * marks + 1;
            _taken.Add(new Rect(at.X - r, at.Y - r, r * 2, r * 2.2));
        }

        // Names in order of importance; each one tries below, above, right and left of its mark.
        foreach (var (s, at) in drawnSettlements.Where(d => d.S.IsTown).OrderBy(d => d.S.Name, StringComparer.Ordinal))
            if (LiveMap.ShowsName(s, zoom))
                PlaceName(dc, s.Name, Bold, 12, Ink, at, Radius(s) * marks + 2, dpi);
        foreach (var (s, at) in drawnSettlements.Where(d => d.S.IsCastle))
            if (LiveMap.ShowsName(s, zoom))
                PlaceName(dc, s.Name, Body, 11, Ink, at, Radius(s) * marks + 2, dpi);
        if (zoom >= 2.5)
            foreach (var (p, at) in drawnParties.Where(d => d.P.LeadsArmy && !d.P.IsPlayer).OrderByDescending(d => d.P.ArmyTroops))
            {
                var words = LiveMap.ShowsLordName(zoom)
                    ? p.Name + "  " + p.ArmyTroops.ToString("N0", CultureInfo.CurrentCulture)
                    : p.ArmyTroops.ToString("N0", CultureInfo.CurrentCulture);
                PlaceName(dc, words, Bold, 10.5, InkSoft, at, 5 * marks + 1, dpi);
            }

        foreach (var (s, at) in drawnSettlements.Where(d => d.S.IsVillage))
            if (LiveMap.ShowsName(s, zoom))
                PlaceName(dc, s.Name, Italic, 10.5, InkSoft, at, Radius(s) * marks + 2, dpi);
        if (LiveMap.ShowsLordName(zoom))
            foreach (var (p, at) in drawnParties.Where(d => !d.P.LeadsArmy && !d.P.IsPlayer))
                PlaceName(dc, p.Name, Body, 10, InkSoft, at, 3 * marks + 1, dpi);

        // Kingdom names across their land while the map is far out, faint like on a paper map; a
        // name moves up or down off the place names, or is left out.
        if (_layers.Territory && zoom < 2.6)
        {
            dc.PushOpacity(zoom < 1.5 ? 0.8 : 0.5);
            foreach (var label in _kingdomLabels)
            {
                var (lx, ly) = Camera.ToScreen(label.X, label.Y);
                var text = Text(Spaced(label.Name.ToUpper(CultureInfo.CurrentCulture)), Region, zoom < 1.5 ? 14 : 17, Ink, dpi);
                foreach (var shift in new[] { 0, 1.3, -1.3, 2.6, -2.6 })
                {
                    var spot = new Point(lx - text.Width / 2, ly - text.Height / 2 + shift * text.Height);
                    var box = new Rect(spot, new Size(text.Width, text.Height));
                    if (Overlaps(box))
                        continue;
                    _taken.Add(box);
                    var shape = text.BuildGeometry(spot);
                    dc.DrawGeometry(null, RegionHalo, shape);
                    dc.DrawGeometry(Brush(Mix(label.Color, 0xFF2E2416, 0.55)), null, shape);
                    break;
                }
            }

            dc.Pop();
        }

        if (Selected != null && Where(Selected) is { } picked)
        {
            var (x, y) = Camera.ToScreen(picked.X, picked.Y);
            var r = 11 * marks;
            dc.DrawEllipse(null, SelectOuter, new Point(x, y), r, r);
            dc.DrawEllipse(null, SelectInner, new Point(x, y), r, r);
        }

        if (_hover != null && !_dragged)
        {
            if (Where(_hover) is { } over && !ReferenceEquals(_hover, Selected))
            {
                var (x, y) = Camera.ToScreen(over.X, over.Y);
                dc.DrawEllipse(null, HoverPen, new Point(x, y), 9 * marks, 9 * marks);
            }

            DrawCard(dc, _hover, dpi, size);
        }
    }

    /// <summary>Draws a name next to its mark where it covers no other name, or leaves it out.</summary>
    private bool PlaceName(DrawingContext dc, string name, Typeface face, double fontSize, Brush brush, Point at, double gap, double dpi)
    {
        var text = Text(name, face, fontSize, brush, dpi);
        var w = text.Width;
        var h = text.Height;
        Span<Point> spots = stackalloc Point[]
        {
            new(at.X - w / 2, at.Y + gap),
            new(at.X - w / 2, at.Y - gap - h),
            new(at.X + gap + 1, at.Y - h / 2),
            new(at.X - gap - 1 - w, at.Y - h / 2),
            new(at.X + gap * 0.6, at.Y + gap * 0.6),
            new(at.X - gap * 0.6 - w, at.Y + gap * 0.6),
            new(at.X + gap * 0.6, at.Y - gap * 0.6 - h),
            new(at.X - gap * 0.6 - w, at.Y - gap * 0.6 - h),
        };
        foreach (var spot in spots)
        {
            var box = new Rect(spot.X - 2, spot.Y, w + 4, h);
            // Boxes that only touch do not count as covering each other.
            if (Overlaps(Rect.Inflate(box, -1.5, -1.5)))
                continue;
            _taken.Add(box);
            var shape = text.BuildGeometry(spot);
            dc.DrawGeometry(null, Halo, shape);
            dc.DrawGeometry(brush, null, shape);
            return true;
        }

        return false;
    }

    private bool Overlaps(Rect box)
    {
        foreach (var taken in _taken)
            if (taken.IntersectsWith(box))
                return true;
        return false;
    }

    private Rect ScreenRect(MapBounds bounds)
    {
        var (x1, y1) = Camera.ToScreen(bounds.MinX, bounds.MaxY);
        var (x2, y2) = Camera.ToScreen(bounds.MaxX, bounds.MinY);
        return new Rect(new Point(x1, y1), new Point(x2, y2));
    }

    private void DrawGrid(DrawingContext dc, Rect size)
    {
        // A faint graticule every 100 or 200 game units, like the lines on a paper map; it also shows the map moving.
        var step = Camera.Scale * 100 < 90 ? 200 : Camera.Scale * 50 < 90 ? 100 : 50;
        var (wx1, wy1) = Camera.ToWorld(0, size.Height);
        var (wx2, wy2) = Camera.ToWorld(size.Width, 0);
        for (var x = Math.Floor(wx1 / step) * step; x <= wx2; x += step)
        {
            var (sx, _) = Camera.ToScreen(x, 0);
            dc.DrawLine(GridPen, new Point(sx, 0), new Point(sx, size.Height));
        }

        for (var y = Math.Floor(wy1 / step) * step; y <= wy2; y += step)
        {
            var (_, sy) = Camera.ToScreen(0, y);
            dc.DrawLine(GridPen, new Point(0, sy), new Point(size.Width, sy));
        }
    }

    private static double Radius(MapSettlement s) => s.IsTown ? 5.2 : s.IsCastle ? 3.8 : 1.7;

    private void DrawSettlement(DrawingContext dc, MapSettlement s, Point at, double marks, double zoom)
    {
        var fill = Brush(LiveMap.ColorOf(_map, s.KingdomId, s.Color));
        var r = Radius(s) * marks;
        if (s.IsTown)
        {
            // A town is a small shield in its kingdom's colour with an ink edge and a light band.
            var shield = new StreamGeometry();
            using (var g = shield.Open())
            {
                g.BeginFigure(new Point(at.X - r, at.Y - r), true, true);
                g.LineTo(new Point(at.X + r, at.Y - r), true, true);
                g.LineTo(new Point(at.X + r, at.Y + r * 0.15), true, true);
                g.QuadraticBezierTo(new Point(at.X + r * 0.9, at.Y + r * 0.95), new Point(at.X, at.Y + r * 1.3), true, true);
                g.QuadraticBezierTo(new Point(at.X - r * 0.9, at.Y + r * 0.95), new Point(at.X - r, at.Y + r * 0.15), true, true);
            }

            shield.Freeze();
            dc.DrawGeometry(fill, Outline, shield);
            dc.DrawRectangle(PaperLight, null, new Rect(at.X - r * 0.62, at.Y - r * 0.45, r * 1.24, r * 0.32));
        }
        else if (s.IsCastle)
        {
            // A castle is a small tower with two merlons.
            var tower = new StreamGeometry();
            using (var g = tower.Open())
            {
                var top = at.Y - r * 1.1;
                g.BeginFigure(new Point(at.X - r, at.Y + r), true, true);
                g.LineTo(new Point(at.X - r, top), true, true);
                g.LineTo(new Point(at.X - r * 0.4, top), true, true);
                g.LineTo(new Point(at.X - r * 0.4, top + r * 0.45), true, true);
                g.LineTo(new Point(at.X + r * 0.4, top + r * 0.45), true, true);
                g.LineTo(new Point(at.X + r * 0.4, top), true, true);
                g.LineTo(new Point(at.X + r, top), true, true);
                g.LineTo(new Point(at.X + r, at.Y + r), true, true);
            }

            tower.Freeze();
            dc.DrawGeometry(fill, Outline, tower);
        }
        else
        {
            // A village is a small dot; far out it is only a speck.
            var dot = zoom < 1.2 ? r * 0.8 : r;
            dc.DrawEllipse(fill, ThinOutline, at, dot, dot);
        }

        if (s.UnderSiege)
            dc.DrawEllipse(null, SiegePen, at, r + 4, r + 4);
    }

    private void DrawLord(DrawingContext dc, MapParty p, Point at, double marks)
    {
        var color = Brush(LiveMap.ColorOf(_map, p.KingdomId, p.Color));
        // Lords: a small diamond; an army leader a bigger one with a light centre.
        var size = (p.LeadsArmy ? 4.6 : 2.6) * marks;
        var diamond = new StreamGeometry();
        using (var g = diamond.Open())
        {
            g.BeginFigure(new Point(at.X, at.Y - size), true, true);
            g.LineTo(new Point(at.X + size, at.Y), true, true);
            g.LineTo(new Point(at.X, at.Y + size), true, true);
            g.LineTo(new Point(at.X - size, at.Y), true, true);
        }

        diamond.Freeze();
        dc.DrawGeometry(color, p.LeadsArmy ? Outline : ThinOutline, diamond);
        if (p.LeadsArmy)
            dc.DrawEllipse(PaperLight, null, at, size * 0.28, size * 0.28);
        if (p.InBattle || p.Besieging)
            dc.DrawEllipse(null, SiegePen, at, size + 3, size + 3);
    }

    private void DrawPlayer(DrawingContext dc, MapParty p, Point at, double marks)
    {
        // Players: a star in their colour with a gold edge on a soft dark glow, always on top.
        var r = 7.5 * marks;
        dc.DrawEllipse(PlayerGlow, null, at, r * 1.45, r * 1.45);
        var star = Star(at, r);
        dc.DrawGeometry(null, Outline, star);
        dc.DrawGeometry(Brush(LiveMap.ColorOf(_map, p.KingdomId, p.Color)), PlayerPen, star);
        if (p.InBattle || p.Besieging)
            dc.DrawEllipse(null, SiegePen, at, r * 1.6, r * 1.6);
        _taken.Add(new Rect(at.X - r, at.Y - r, r * 2, r * 2));
    }

    private void DrawPlayerName(DrawingContext dc, MapParty p, Point at, double marks, double dpi)
    {
        var r = 7.5 * marks;
        var name = Text(Translator.T(LiveMap.ShownName(_map, p, _streamer)), Bold, 12, TextMain, dpi);
        var plate = new Rect(at.X - name.Width / 2 - 6, at.Y - r * 1.5 - name.Height - 4, name.Width + 12, name.Height + 3);
        // Two players in one place: the next plate goes above the one already there.
        while (_plates.Any(t => t.IntersectsWith(plate)))
            plate.Offset(0, -(name.Height + 6));
        dc.DrawRectangle(CardBack, PlatePen, plate);
        dc.DrawText(name, new Point(plate.X + 6, plate.Y + 1.5));
        _plates.Add(plate);
        _taken.Add(plate);
    }

    private static Geometry Star(Point at, double r)
    {
        var star = new StreamGeometry();
        using (var g = star.Open())
        {
            for (var i = 0; i < 10; i++)
            {
                var angle = -Math.PI / 2 + i * Math.PI / 5;
                var radius = i % 2 == 0 ? r : r * 0.45;
                var point = new Point(at.X + Math.Cos(angle) * radius, at.Y + Math.Sin(angle) * radius);
                if (i == 0)
                    g.BeginFigure(point, true, true);
                else
                    g.LineTo(point, true, true);
            }
        }

        star.Freeze();
        return star;
    }

    /// <summary>The hover card next to the mouse: name, what it is, and one more line.</summary>
    private void DrawCard(DrawingContext dc, object item, double dpi, Rect size)
    {
        var lines = MapText.Card(_map, item, _streamer);
        if (lines.Count == 0)
            return;
        var texts = lines.Select((line, i) => Text(line.Text, i == 0 ? Bold : Body, i == 0 ? 13 : 11.5,
            line.Warning ? TextRed : i == 0 ? TextMain : TextDim, dpi)).ToList();
        var width = texts.Max(t => t.Width) + 20;
        var height = texts.Sum(t => t.Height) + 14;
        var x = _mouse.X + 16;
        var y = _mouse.Y + 12;
        if (x + width > size.Width - 4)
            x = _mouse.X - width - 12;
        if (y + height > size.Height - 4)
            y = _mouse.Y - height - 8;
        dc.DrawRectangle(CardBack, CardPen, new Rect(x, y, width, height));
        var top = y + 7;
        foreach (var text in texts)
        {
            dc.DrawText(text, new Point(x + 10, top));
            top += text.Height;
        }
    }

    private static (double X, double Y)? Where(object item) => item switch
    {
        MapSettlement s => (s.X, s.Y),
        MapParty p => (p.X, p.Y),
        _ => null,
    };

    private Brush Brush(uint argb)
    {
        if (!_brushes.TryGetValue(argb, out var brush))
            _brushes[argb] = brush = Frozen(new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb)));
        return brush;
    }

    // In Chinese, Japanese or Korean the picked language chooses the fallback font, as the windows do.
    private static FormattedText Text(string text, Typeface face, double size, Brush brush, double dpi) =>
        new(text, Translator.EastAsian ? CultureInfo.GetCultureInfo(Translator.Current) : CultureInfo.CurrentCulture, Translator.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, face, size, brush, dpi);

    private static string Spaced(string text) => string.Join(" ", text.ToCharArray()).Replace("   ", "  ");

    /// <summary>Two colours mixed (<paramref name="t"/> of <paramref name="b"/>), fully opaque.</summary>
    private static uint Mix(uint a, uint b, double t)
    {
        uint Channel(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000u | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    // --- mouse ---------------------------------------------------------------

    /// <summary>The settlement or party under a screen point: players first, then lords, towns, castles, villages.</summary>
    public object? HitTest(Point point)
    {
        if (_map.IsEmpty)
            return null;
        object? best = null;
        var bestDistance = double.MaxValue;
        void Try(object item, double x, double y, double reach, double bias)
        {
            var (sx, sy) = Camera.ToScreen(x, y);
            var d = Math.Sqrt((sx - point.X) * (sx - point.X) + (sy - point.Y) * (sy - point.Y));
            if (d <= reach && d + bias < bestDistance)
            {
                bestDistance = d + bias;
                best = item;
            }
        }

        foreach (var p in _parties)
            Try(p, p.X, p.Y, p.IsPlayer ? 16 : 9, p.IsPlayer ? -6 : -2);
        foreach (var s in _map.Settlements)
            if (LiveMap.Shows(s, _layers))
                Try(s, s.X, s.Y, s.IsVillage ? 6 : 11, s.IsVillage ? 3 : 0);
        return best;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        _down = e.GetPosition(this);
        _last = _down.Value;
        _dragged = false;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouse = e.GetPosition(this);
        if (_down is { } down && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_dragged && (Math.Abs(_mouse.X - down.X) > 3 || Math.Abs(_mouse.Y - down.Y) > 3))
            {
                _dragged = true;
                Cursor = Cursors.SizeAll;
            }

            if (_dragged)
            {
                Camera.Pan(_mouse.X - _last.X, _mouse.Y - _last.Y);
                _last = _mouse;
                Changed();
                return;
            }
        }

        var hover = HitTest(_mouse);
        if (!Equals(hover, _hover) || hover != null)
        {
            _hover = hover;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        Cursor = Cursors.Hand;
        var wasDrag = _dragged;
        _down = null;
        _dragged = false;
        if (wasDrag)
            return;
        Selected = HitTest(e.GetPosition(this));
        InvalidateVisual();
        Picked?.Invoke(Selected);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var at = e.GetPosition(this);
        Camera.ZoomAt(at.X, at.Y, e.Delta > 0 ? 1.25 : 0.8);
        e.Handled = true;
        Changed();
    }
}

/// <summary>The words the map shows for a settlement or party, in the picked language.</summary>
public static class MapText
{
    public sealed record Line(string Text, bool Warning = false);

    public static string Kind(MapSettlement s) => Translator.T(s.IsTown ? "Town" : s.IsCastle ? "Castle" : "Village");

    public static string Activity(MapData map, MapParty party) => Translator.T(LiveMap.Activity(map, party));

    public static string Troops(int count) => Translator.T("Troops") + ": " + count.ToString("N0", CultureInfo.CurrentCulture);

    public static IReadOnlyList<Line> Card(MapData map, object item, bool streamer)
    {
        switch (item)
        {
            case MapSettlement s:
            {
                var lines = new List<Line> { new(s.Name) };
                var owner = new List<string> { Kind(s) };
                if (s.OwnerClanName.Length > 0)
                    owner.Add(s.OwnerClanName);
                if (map.KingdomById.TryGetValue(s.KingdomId, out var k))
                    owner.Add(k.Name);
                lines.Add(new Line(string.Join("  ·  ", owner)));
                if (s.UnderSiege)
                    lines.Add(new Line(Translator.T("Under siege"), true));
                return lines;
            }
            case MapParty p:
            {
                var lines = new List<Line> { new(Translator.T(LiveMap.ShownName(map, p, streamer))) };
                var who = new List<string> { Translator.T(p.IsPlayer ? "Player" : p.LeadsArmy ? "Army leader" : "Lord") };
                if (p.ClanName.Length > 0 && !(streamer && p.IsPlayer))
                    who.Add(p.ClanName);
                who.Add(Troops(p.LeadsArmy ? p.ArmyTroops : p.Troops));
                lines.Add(new Line(string.Join("  ·  ", who)));
                lines.Add(new Line(Activity(map, p), p.InBattle || p.Besieging));
                return lines;
            }
            default:
                return Array.Empty<Line>();
        }
    }
}
