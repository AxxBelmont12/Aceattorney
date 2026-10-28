using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace AvatarStudio;
public sealed record StoredFragment(string Name,DialogueShot Shot)
{
    public override string ToString()=>Name+" · "+Shot.Project.Text.Replace('\n',' ').Replace('\r',' ');
}
public static class ProjectStorage
{
    public static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AxxBelmont","AvatarStudio");
    public static string LibraryFolder=>Path.Combine(Root,"fragments");
    public static string SaveFragment(StoredFragment fragment,string? folder=null)
    {
        folder??=LibraryFolder;Directory.CreateDirectory(folder);string path=Path.Combine(folder,Guid.NewGuid().ToString("N")+".avfragment.json");File.WriteAllText(path,JsonSerializer.Serialize(fragment,AssetStore.Json));return path;
    }
    public static List<(string Path,StoredFragment Fragment)> Fragments(Library library,string? folder=null)
    {
        folder??=LibraryFolder;if(!Directory.Exists(folder))return new();var list=new List<(string,StoredFragment)>();
        foreach(var path in Directory.EnumerateFiles(folder,"*.avfragment.json").Take(300))try{if(new FileInfo(path).Length>90_000_000)continue;var value=JsonSerializer.Deserialize<StoredFragment>(File.ReadAllText(path),AssetStore.Json)!;value.Shot.Project.Validate(library);list.Add((path,value));}catch(Exception ex) when(ex is IOException or JsonException or InvalidDataException){}
        return list;
    }
    public static string WriteVersion(DialogueSequence project,string json,string? root=null)
    {
        if(!Guid.TryParse(project.Id,out _))throw new InvalidDataException("Identidad de proyecto inválida.");
        string folder=Path.Combine(root??Path.Combine(Root,"history"),project.Id);Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".avmontage.json.gz"),temp=path+".tmp";
        try{using(var stream=File.Create(temp))using(var gzip=new GZipStream(stream,CompressionLevel.Fastest))using(var writer=new StreamWriter(gzip,new UTF8Encoding(false)))writer.Write(json);File.Move(temp,path);}
        finally{if(File.Exists(temp))File.Delete(temp);}
        File.WriteAllText(Path.Combine(folder,"project-info.json"),JsonSerializer.Serialize(new{project.Name,project.Id},AssetStore.Json));
        var files=new DirectoryInfo(folder).GetFiles("*.avmontage.json.gz").OrderByDescending(f=>f.Name).ToList();long total=0;
        for(int i=0;i<files.Count;i++){total+=files[i].Length;if(i>=5 || total>300_000_000 && i>0)files[i].Delete();}
        return path;
    }
    public static DialogueSequence ReadVersion(string path,Library library)
    {
        using var file=File.OpenRead(path);using var gzip=new GZipStream(file,CompressionMode.Decompress);using var reader=new StreamReader(gzip);var builder=new StringBuilder();var block=new char[8192];int count;
        while((count=reader.Read(block,0,block.Length))>0){if(builder.Length+count>250_000_000)throw new InvalidDataException("Versión demasiado grande.");builder.Append(block,0,count);}
        var value=JsonSerializer.Deserialize<DialogueSequence>(builder.ToString(),AssetStore.Json)??throw new InvalidDataException("Versión vacía.");value.Validate(library);return value;
    }
}
