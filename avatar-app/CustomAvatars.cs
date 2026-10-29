using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace AvatarStudio;
public sealed partial class AssetStore
{
    readonly Dictionary<string,byte[]> customFrames=new(StringComparer.Ordinal);
    readonly Dictionary<string,List<string>> customPoseIds=new(StringComparer.Ordinal);
    public List<string> CustomAvatarWarnings{get;}=new();
    public static string CustomAvatarFolder=>Path.Combine(ProjectStorage.Root,"avatars");
    sealed record AvatarPack(string Key,List<Pose> Poses,Dictionary<string,byte[]> Frames);
    public void LoadCustomAvatars(string? folder=null)
    {
        folder??=CustomAvatarFolder;if(!Directory.Exists(folder))return;
        foreach(var file in Directory.EnumerateFiles(folder,"*.avavatar.zip").Take(32))
            try{ApplyAvatarPack(ReadAvatarPack(file));}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or NotSupportedException or FormatException){CustomAvatarWarnings.Add(Path.GetFileName(file)+": "+ex.Message);}
    }
    public Pose ImportAvatarPack(string file,string? folder=null)
    {
        var pack=ReadAvatarPack(file);folder??=CustomAvatarFolder;Directory.CreateDirectory(folder);
        string destination=Path.Combine(folder,pack.Key+".avavatar.zip"),temp=destination+"."+Guid.NewGuid().ToString("N")+".part";
        if(!Path.GetFullPath(file).Equals(Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase))
            try{File.Copy(file,temp);File.Move(temp,destination,true);}finally{if(File.Exists(temp))File.Delete(temp);}
        ApplyAvatarPack(pack);return pack.Poses[0];
    }
    AvatarPack ReadAvatarPack(string file)
    {
        const long maximum=64*1024*1024;
        if(new FileInfo(file).Length>maximum)throw new InvalidDataException("El paquete supera 64 MB.");
        using var stream=File.OpenRead(file);using var archive=new ZipArchive(stream,ZipArchiveMode.Read);
        if(archive.Entries.Count>128 || archive.Entries.Any(e=>e.Length<0 || e.Length>maximum) || archive.Entries.Sum(e=>e.Length)>maximum)throw new InvalidDataException("El paquete contiene demasiados recursos.");
        var manifest=archive.GetEntry("manifest.json");if(manifest==null || manifest.Length>262144)throw new InvalidDataException("Falta un manifest.json válido en el paquete.");
        var library=JsonSerializer.Deserialize<Library>(ReadEntry(manifest,262144),Json)??throw new InvalidDataException("El catálogo del avatar está vacío.");
        if(library.Width!=Library.Width || library.Height!=Library.Height || library.Fps!=Library.Fps || library.Poses==null || library.Poses.Count is <1 or >32)throw new InvalidDataException($"El paquete debe usar lienzos {Library.Width} × {Library.Height}, {Library.Fps:0} fps y entre 1 y 32 poses.");
        if(library.Poses.Any(p=>p==null || !Regex.IsMatch(p.Id??"",@"^[a-zA-Z0-9_-]{1,64}$")) || library.Poses.Select(p=>p.Id).Distinct().Count()!=library.Poses.Count)throw new InvalidDataException("Las poses necesitan identificadores únicos válidos.");
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",library.Poses.Select(p=>p.Id).OrderBy(id=>id,StringComparer.Ordinal)))))[..20].ToLowerInvariant();
        var replacing=customPoseIds.GetValueOrDefault(key)??new();var existing=Library.Poses.Where(p=>!replacing.Contains(p.Id)).Select(p=>p.Id).ToHashSet();
        if(library.Poses.Any(p=>existing.Contains(p.Id)))throw new InvalidDataException("Una pose del paquete utiliza el identificador de otro personaje.");
        var frames=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        foreach(var pose in library.Poses)
        {
            if(string.IsNullOrWhiteSpace(pose.Character) || pose.Character.Length>100 || string.IsNullOrWhiteSpace(pose.Name) || pose.Name.Length>120 || pose.Frames==null || pose.Frames.Count is <1 or >96 || pose.Clips==null || pose.Clips.Count is <1 or >32 || pose.Idle<0 || pose.Idle>=pose.Clips.Count || pose.Talk<0 || pose.Talk>=pose.Clips.Count)throw new InvalidDataException("Revisa el nombre, las poses y los fotogramas del avatar.");
            foreach(var clip in pose.Clips)
            {
                if(clip==null || clip.Total is <1 or >36000 || clip.Restart<0 || clip.Restart>=clip.Total || clip.Keys==null || clip.Keys.Count is <1 or >2000 || clip.Keys[0].At!=0)throw new InvalidDataException("La animación tiene tiempos no válidos.");
                int previous=-1;foreach(var cell in clip.Keys)
                {
                    if(cell==null || cell.At<=previous || cell.At>=clip.Total || cell.Frame<0 || cell.Frame>=pose.Frames.Count || !double.IsFinite(cell.X+cell.Y+cell.Sx+cell.Sy+cell.Rotation) || Math.Abs(cell.X)>4096 || Math.Abs(cell.Y)>4096 || Math.Abs(cell.Sx) is <.01 or >10 || Math.Abs(cell.Sy) is <.01 or >10 || Math.Abs(cell.Rotation)>3600)throw new InvalidDataException("Un fotograma de la animación no es válido.");previous=cell.At;
                }
            }
            var paths=new List<string>();
            foreach(string path in pose.Frames)
            {
                if(string.IsNullOrWhiteSpace(path) || path.Length>180 || path.Contains('\\') || path.StartsWith('/') || path.Split('/').Any(part=>part is "" or "." or "..") || !path.EndsWith(".png",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("El paquete contiene una ruta de imagen no válida.");
                string virtualPath="custom/"+key+"/"+path;
                if(!frames.ContainsKey(virtualPath))
                {
                    var entry=archive.GetEntry(path);if(entry==null || entry.Length>8*1024*1024)throw new InvalidDataException("Falta una imagen o supera el tamaño permitido.");
                    byte[] bytes=ReadEntry(entry,8*1024*1024);using var imageStream=new MemoryStream(bytes);
                    ReadOnlySpan<byte> png=[137,80,78,71,13,10,26,10];
                    if(bytes.Length<24 || !bytes.AsSpan(0,8).SequenceEqual(png) || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4))!=Library.Width || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4))!=Library.Height)throw new InvalidDataException("La imagen PNG no usa las dimensiones del catálogo.");
                    var decoder=BitmapDecoder.Create(imageStream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                    if(decoder.Frames.Count!=1 || decoder.Frames[0].PixelWidth!=Library.Width || decoder.Frames[0].PixelHeight!=Library.Height)throw new InvalidDataException("Todos los fotogramas deben usar el lienzo del catálogo.");
                    frames.Add(virtualPath,bytes);
                }
                paths.Add(virtualPath);
            }
            pose.Frames=paths;
        }
        return new(key,library.Poses,frames);
    }
    static byte[] ReadEntry(ZipArchiveEntry entry,int maximum)
    {
        using var input=entry.Open();using var output=new MemoryStream();byte[] block=new byte[65536];int count;
        while((count=input.Read(block,0,block.Length))>0){if(output.Length+count>maximum)throw new InvalidDataException("Un recurso del paquete supera el tamaño permitido.");output.Write(block,0,count);}return output.ToArray();
    }
    void ApplyAvatarPack(AvatarPack pack)
    {
        if(customPoseIds.TryGetValue(pack.Key,out var previous))Library.Poses.RemoveAll(p=>previous.Contains(p.Id));
        foreach(string key in customFrames.Keys.Where(k=>k.StartsWith("custom/"+pack.Key+"/",StringComparison.Ordinal)).ToList())customFrames.Remove(key);
        foreach(var pair in pack.Frames)customFrames.Add(pair.Key,pair.Value);Library.Poses.AddRange(pack.Poses);customPoseIds[pack.Key]=pack.Poses.Select(p=>p.Id).ToList();ClearCache();
    }
}
