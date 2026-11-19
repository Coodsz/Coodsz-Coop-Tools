using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BannerlordCoopLauncher.Core.Keep;
using CoodszHost.Services;

namespace CoodszHost.Keep;

/// <summary>
/// One game picture on screen. It loads in the background; until then (or when no picture was
/// built) <see cref="HasImage"/> is false and the card shows its text instead.
/// </summary>
public sealed class GameImage : INotifyPropertyChanged
{
    private ImageSource? _source;

    public ImageSource? Source
    {
        get => _source;
        set
        {
            _source = value;
            OnChanged();
            OnChanged(nameof(HasImage));
            OnChanged(nameof(NoImage));
        }
    }

    public bool HasImage => _source != null;
    public bool NoImage => _source == null;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// The screen side of <see cref="GameImageStore"/>: each picture is decoded once, small, frozen
/// and shared by every window (troop portraits, item pictures, later lord portraits and banners).
/// </summary>
public static class GameImages
{
    private static readonly ConcurrentDictionary<string, GameImage> Cache = new();

    public static GameImage Get(GameImageKind kind, string? id)
    {
        var key = kind + ":" + (id ?? "");
        return Cache.GetOrAdd(key, __ =>
        {
            var image = new GameImage();
            if (!string.IsNullOrWhiteSpace(id))
                _ = LoadAsync(image, kind, id!);
            return image;
        });
    }

    /// <summary>After Build pictures: pictures that were missing are looked for again.</summary>
    public static void Refresh()
    {
        foreach (var (key, image) in Cache)
        {
            if (image.HasImage)
                continue;
            var at = key.IndexOf(':');
            if (at > 0 && Enum.TryParse<GameImageKind>(key[..at], out var kind))
                _ = LoadAsync(image, kind, key[(at + 1)..]);
        }
    }

    private static async Task LoadAsync(GameImage target, GameImageKind kind, string id)
    {
        try
        {
            var source = await Task.Run(() => PictureBuilder.Store.Find(kind, id) is { } path ? Decode(path) : null);
            if (source != null)
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => target.Source = source);
        }
        catch (Exception)
        {
            // No picture: the card keeps showing its text.
        }
    }

    /// <summary>Reads the file once (it is never locked) and decodes it at card size.</summary>
    private static ImageSource? Decode(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 256;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
