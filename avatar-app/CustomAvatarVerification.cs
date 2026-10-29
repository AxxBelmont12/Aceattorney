using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AvatarStudio;
public static class CustomAvatarVerification
{
    static void Check(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
    public static void Run(AssetStore original,string folder)
    {
        string root=Path.Combine(folder,"custom-avatars",Guid.NewGuid().ToString("N")),installed=Path.Combine(root,"installed");Directory.CreateDirectory(root);
        var frame=original.Frame(original.Library.Poses[0].Frames[0]);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(frame));using var bytes=new MemoryStream();encoder.Save(bytes);
        Library Fixture(string id="qa_original_avatar",string path="frames/normal.png")=>new(){Width=original.Library.Width,Height=original.Library.Height,Fps=original.Library.Fps,Poses=new(){new(){Id=id,Character="Avatar de prueba",Name="Normal",Idle=0,Talk=0,Frames=new(){path},Clips=new(){new(){Total=120,Restart=0,Loop=true,Keys=new(){new(){At=0,Frame=0}}}}}}};
        string Write(string name,Library library){string file=Path.Combine(root,name+".avavatar.zip");using var z=ZipFile.Open(file,ZipArchiveMode.Create);using(var s=z.CreateEntry("manifest.json").Open())JsonSerializer.Serialize(s,library,AssetStore.Json);using(var s=z.CreateEntry(library.Poses[0].Frames[0]).Open())s.Write(bytes.ToArray());return file;}
        string valid=Write("valid",Fixture());
        using var store=new AssetStore(loadCustom:false);int count=store.Library.Poses.Count;var pose=store.ImportAvatarPack(valid,installed);
        Check(store.Library.Poses.Count==count+1 && store.Frame(pose.Frames[0]).PixelWidth==frame.PixelWidth,"Custom avatar imports and decodes");
        store.ImportAvatarPack(valid,installed);Check(store.Library.Poses.Count==count+1 && Directory.GetFiles(installed).Length==1,"Reimport replaces pack without duplicate poses");
        using(var restored=new AssetStore(loadCustom:false)){restored.LoadCustomAvatars(installed);Check(restored.Library.Poses.Any(p=>p.Id==pose.Id),"Imported avatar survives application restart");}
        foreach(var item in new[]{("escape",Fixture("qa_escape","../escape.png")),("collision",Fixture("pl0000")),("badframe",Fixture("qa_badframe"))})
        {
            if(item.Item1=="badframe")item.Item2.Poses[0].Clips[0].Keys[0].Frame=99;
            bool rejected=false;try{store.ImportAvatarPack(Write(item.Item1,item.Item2),installed);}catch(InvalidDataException){rejected=true;}Check(rejected && store.Library.Poses.Count==count+1 && Directory.GetFiles(installed).Length==1,"Invalid imports are atomic: "+item.Item1);
        }
        File.WriteAllText(Path.Combine(installed,"broken.avavatar.zip"),"Not a zip file");using(var restored=new AssetStore(loadCustom:false)){restored.LoadCustomAvatars(installed);Check(restored.CustomAvatarWarnings.Count==1 && restored.Library.Poses.Count==count+1,"Broken optional pack does not break application startup");}
        string? real=Environment.GetEnvironmentVariable("AVATAR_CUSTOM_QA_PACK");bool realTest=false;
        if(real!=null && File.Exists(real))
        {
            var imported=store.ImportAvatarPack(real,installed);var p=new DialogueProject{DefaultPose=imported.Id,Text="Este es mi avatar. Probando la animación.",DisplayName="Axx Belmont"};p.Validate(store.Library);var t=DialogueCompiler.Compile(p,store.Library);var rendered=new DialogueRenderer(store).Render(p,t,.6);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(rendered));using(var output=File.Create(Path.Combine(folder,"avatar-personal-dialogo.png")))png.Save(output);
            var main=new MainWindow(store,new Preferences{Pose=imported.Id},testing:true);var visual=(FrameworkElement)main.Content;visual.Measure(new Size(1050,760));visual.Arrange(new Rect(0,0,1050,760));visual.UpdateLayout();var bitmap=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var preview=new PngBitmapEncoder();preview.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(folder,"avatar-personal-estudio.png")))preview.Save(output);main.Close();realTest=true;
        }
        File.WriteAllText(Path.Combine(folder,"custom-avatar-verification.json"),JsonSerializer.Serialize(new{import=true,reimport=true,persistent=true,invalidPathsRejected=true,poseCollisionRejected=true,invalidFrameRejected=true,brokenOptionalPackIgnored=true,actualUserPack=realTest},AssetStore.Json));
    }
}
