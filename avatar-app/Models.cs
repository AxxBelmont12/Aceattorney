using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace AvatarStudio;

public sealed class Library
{
    public double Fps { get; set; } = 60;
    public int Width { get; set; }
    public int Height { get; set; }
    public List<Pose> Poses { get; set; } = new();
}
public sealed class Pose
{
    public string Id { get; set; } = "";
    public string Character { get; set; } = "";
    public string Name { get; set; } = "";
    public int Idle { get; set; }
    public int Talk { get; set; }
    public List<string> Frames { get; set; } = new();
    public List<Clip> Clips { get; set; } = new();
    public override string ToString() => Name;
}
public sealed class Clip
{
    public int Total { get; set; }
    public int Restart { get; set; }
    public bool Loop { get; set; }
    public List<Cell> Keys { get; set; } = new();
    public Cell At(double frame)
    {
        // Preserve source key durations. Do not advance one PNG per timer tick.
        var bounded = Math.Clamp(frame, 0, Math.Max(0, Total - 0.001));
        return Keys.Last(k => k.At <= bounded);
    }
}
public static class AnimationClock
{
    public static double Frame(Clip clip,double elapsed)=>elapsed<clip.Total?Math.Max(0,elapsed):clip.Loop?clip.Restart+(elapsed-clip.Total)%Math.Max(1,clip.Total-clip.Restart):clip.Total-.001;
    public static double AnchorAfterSpeedChange(double now,double anchor,double oldSpeed,double newSpeed)
        =>now-Math.Max(0,now-anchor)*oldSpeed/newSpeed;
    public static bool SpeechRestarts(Pose pose)=>pose.Idle!=pose.Talk;
}
public sealed class Cell
{
    public int At { get; set; }
    public int Frame { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Rotation { get; set; }
    public double Sx { get; set; } = 1;
    public double Sy { get; set; } = 1;
}
public sealed class AssetStore : IDisposable
{
    readonly Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Avatars")!;
    readonly ZipArchive zip;
    readonly Dictionary<string, BitmapSource> cache = new();
    public Library Library { get; }
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public AssetStore()
    {
        zip = new ZipArchive(source, ZipArchiveMode.Read);
        using var stream = zip.GetEntry("manifest.json")!.Open();
        Library = JsonSerializer.Deserialize<Library>(stream, Json)!;
    }
    public BitmapSource Frame(string path)
    {
        if (cache.TryGetValue(path, out var bitmap)) return bitmap;
        using var stream = zip.GetEntry(path)!.Open();
        using var memory = new MemoryStream(); stream.CopyTo(memory); memory.Position = 0;
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = memory; image.EndInit(); image.Freeze();
        if(cache.Count>=96)cache.Remove(cache.Keys.First());
        cache[path] = image;
        return image;
    }
    public void ClearCache() => cache.Clear();
    public void Dispose() { zip.Dispose(); source.Dispose(); }
}
public sealed record Shortcut(int Key, uint Modifiers)
{
    public override string ToString() =>
        ((Modifiers & 2) != 0 ? "Ctrl + " : "") + ((Modifiers & 1) != 0 ? "Alt + " : "") +
        ((Modifiers & 4) != 0 ? "Mayús + " : "") + ((Modifiers & 8) != 0 ? "Win + " : "") +
        System.Windows.Input.KeyInterop.KeyFromVirtualKey(Key);
}
public sealed class Preferences
{
    public bool CheckUpdatesOnStart {get;set;}=true;
    public int Revision { get; set; }
    public string Pose { get; set; } = "pl0000";
    public string Mode { get; set; } = "hold";
    public double Speed { get; set; } = 1;
    public bool Mirror { get; set; }
    public string Background { get; set; } = "Verde para OBS";
    public Shortcut Talk { get; set; } = new(0x20, 3);
    public Dictionary<string, Shortcut> Shortcuts { get; set; } = new()
    {
        ["pl0000"] = new(0x70, 3), ["pl21100"] = new(0x71, 3)
    };
    public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AxxBelmont", "AvatarStudio", "settings.json");
    public static Preferences Load()
    {
        Preferences settings;
        try { settings = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path), AssetStore.Json) ?? new(); }
        catch { settings = new(); }
        return settings.Migrate();
    }
    public Preferences Migrate()
    {
        if (Revision < 3)
        {
            var additions = new Dictionary<string, Shortcut>
            {
                ["pl0300"] = new(0x72, 3), ["pl0100"] = new(0x73, 3), ["pl0200"] = new(0x74, 3)
            };
            foreach (var pair in additions)
                if (!Shortcuts.ContainsKey(pair.Key) && !Shortcuts.ContainsValue(pair.Value) && Talk != pair.Value)
                    Shortcuts.Add(pair.Key, pair.Value);
            Revision = 3;
        }
        return this;
    }
    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, AssetStore.Json));
        File.Move(temp, Path, true);
    }
}
