using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace AvatarStudio;
public static class UpdateVerification
{
    static void Check(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
    public static async Task RunAsync()
    {
        string root=Path.GetFullPath(Environment.GetEnvironmentVariable("UPDATE_QA_DIR")??"avatar-work/update-qa");Directory.CreateDirectory(root);
        string test=Path.Combine(root,Guid.NewGuid().ToString("N")),feed=Path.Combine(test,"feed"),cache=Path.Combine(test,"packages");Directory.CreateDirectory(feed);Directory.CreateDirectory(cache);
        string package=Path.Combine(feed,AppUpdates.PackageId+"-0.15.0-full.nupkg");
        using(var zip=ZipFile.Open(package,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry(AppUpdates.PackageId+".nuspec").Open());writer.Write("<package><metadata><id>"+AppUpdates.PackageId+"</id><version>0.15.0</version><authors>Axx Belmont</authors><description>Update verification fixture</description></metadata></package>");}
        var asset=new{PackageId=AppUpdates.PackageId,Version="0.15.0",Type="Full",FileName=Path.GetFileName(package),SHA1=Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(package))),SHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))),Size=new FileInfo(package).Length,NotesMarkdown="Prueba de actualización verificada."};
        string feedFile=Path.Combine(feed,"releases.win.json");File.WriteAllText(feedFile,JsonSerializer.Serialize(new{Assets=new[]{asset}}));
        UpdateManager Manager(string version,string packages)=>new(new SimpleFileSource(new DirectoryInfo(feed)),null,new TestVelopackLocator(AppUpdates.PackageId,version,packages,Path.Combine(test,"current"),test,Path.Combine(test,"Update.exe"),"win"));
        var updates=new AppUpdates(Manager("0.14.0",cache));Check(updates.Installed,"Installed locator supported");await updates.Check();Check(updates.AvailableVersion=="0.15.0","Detect higher semantic version");await updates.Download(CancellationToken.None);Check(updates.Downloaded && !updates.Busy,"Verified package download");
        Check(File.ReadAllBytes(Path.Combine(cache,Path.GetFileName(package))).SequenceEqual(File.ReadAllBytes(package)),"Downloaded package matches bytes");
        int restarted=0,saved=0;bool reentrantAccepted=true;
        var automatic=new AppUpdates(Manager("0.14.0",Path.Combine(test,"automatic-cache")));Directory.CreateDirectory(Path.Combine(test,"automatic-cache"));await automatic.Check();
        bool applied=await automatic.UpdateAndRestart(CancellationToken.None,()=>{saved++;reentrantAccepted=automatic.UpdateAndRestart(CancellationToken.None,()=>{},()=>{}).GetAwaiter().GetResult();},()=>restarted++);
        Check(applied && automatic.Downloaded && saved==1 && restarted==1 && !reentrantAccepted && !automatic.Updating,"One action downloads, verifies, saves and restarts once");
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();bool result=await automatic.UpdateAndRestart(cancelled.Token,()=>saved++,()=>restarted++);Check(!result && saved==1 && restarted==1,"Cancelled update never saves or restarts");}
        bool failedSave=await automatic.UpdateAndRestart(CancellationToken.None,()=>throw new IOException("Test: could not save preferences"),()=>restarted++);Check(!failedSave && restarted==1 && automatic.Downloaded,"Save failure prevents restart and preserves verified download");
        var gate=new UpdateNotificationGate();Check(!gate.TryShow(null,true),"No popup without new version");Check(!gate.TryShow("0.15.0",false) && gate.TryShow("0.15.0",true),"Deferred popup is offered when the owner is ready");Check(!gate.TryShow("0.15.0",true),"Dismissed version does not reopen during the session");gate.MarkShown("0.16.0");Check(!gate.TryShow("0.16.0",true),"Manual offer also suppresses duplicate popup");
        var newer=new AppUpdates(Manager("0.16.0",cache));await newer.Check();Check(newer.Available==null,"Never downgrade");
        var same=new AppUpdates(Manager("0.15.0",cache));await same.Check();Check(same.Available==null,"No reinstall same version");
        string badCache=Path.Combine(test,"bad-cache");Directory.CreateDirectory(badCache);File.WriteAllBytes(package,new byte[]{1,2,3,4});var bad=new AppUpdates(Manager("0.14.0",badCache));await bad.Check();await bad.Download(CancellationToken.None);Check(!bad.Downloaded,"Reject corrupted download");
        bool badApplied=await bad.UpdateAndRestart(CancellationToken.None,()=>saved++,()=>restarted++);Check(!badApplied && restarted==1 && saved==1,"Corrupted package cannot reach restart in one-click update");
        var failedWindow=new UpdatesWindow(bad,new Preferences(),testing:true);failedWindow.VerifyFailure();failedWindow.Close();
        File.WriteAllText(feedFile,"{invalid-json");var offline=new AppUpdates(Manager("0.14.0",cache));await offline.Check();Check(!offline.Busy && offline.Status.Contains("No se pudo"),"Failed update check keeps application usable");
        var portable=new AppUpdates();await portable.Check();Check(portable.Status.Contains("portátil"),"Portable migration instructions");
        var window=new UpdatesWindow(updates,new Preferences(),testing:true);window.VerifyOffer();var element=(FrameworkElement)window.Content;element.Measure(new Size(608,548));element.Arrange(new Rect(0,0,608,548));element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawRectangle(new VisualBrush(element),null,new Rect(0,0,element.ActualWidth,element.ActualHeight));bitmap.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(root,"actualizaciones.png")))png.Save(stream);window.Close();
        File.WriteAllText(Path.Combine(root,"verification.json"),JsonSerializer.Serialize(new{version=AppUpdates.Version,upgradeDetected=true,verifiedDownload=true,rejectCorruption=true,noDowngrade=true,noSameVersion=true,failedCheckNonBlocking=true,portableMigration=true,singleActionDownloadVerifyRestart=true,preventDoubleActivation=true,cancellationPreventsRestart=true,saveFailurePreventsRestart=true,deferredPopup=true,oneOfferPerVersionPerSession=true,twoActionOffer=true,realInstallation="La sustitución de archivos se comprobó con el motor nativo; estas pruebas no cierran la aplicación del usuario."},AssetStore.Json));
    }
}
