using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Velopack;
using Velopack.Sources;

namespace AvatarStudio;

public sealed class AppUpdates
{
    public const string Repository="https://github.com/AxxBelmont12/Aceattorney";
    public const string PackageId="AxxBelmont.AvatarStudio";
    public static string Version=>typeof(Program).Assembly.GetName().Version!.ToString(3);
    readonly UpdateManager manager;
    public event Action? Changed;
    public UpdateInfo? Available {get;private set;}
    public bool Busy {get;private set;}
    public bool Downloaded {get;private set;}
    public bool Updating {get;private set;}
    public bool Restarting {get;private set;}
    public bool Failed {get;private set;}
    public int Progress {get;private set;}
    public string Status {get;private set;}="Busca nuevas versiones publicadas en GitHub Releases.";
    public bool Installed=>manager.IsInstalled && !manager.IsPortable;
    public string? AvailableVersion=>Available?.TargetFullRelease.Version.ToString();
    public AppUpdates(UpdateManager? manager=null)=>this.manager=manager??new UpdateManager(new GithubSource(Repository,null,false));
    public async Task Check()
    {
        if(Busy || Updating)return;Busy=true;Failed=false;Status="Buscando actualizaciones…";Changed?.Invoke();
        try
        {
            if(!Installed){Status="Esta copia es portátil. Instala Avatar Studio con el Setup de GitHub Releases para habilitar las actualizaciones.";return;}
            Available=await manager.CheckForUpdatesAsync();Downloaded=Available!=null && manager.UpdatePendingRestart?.Version==Available.TargetFullRelease.Version;
            Status=Available==null?"Tienes la versión más reciente.":$"Disponible: {AvailableVersion}"+(Downloaded?" · lista para instalar.":".");
        }
        catch(Exception ex){Failed=true;Status="No se pudo consultar GitHub. Puedes seguir usando la aplicación y volver a intentarlo. "+Friendly(ex);}
        finally{Busy=false;Changed?.Invoke();}
    }
    public async Task Download(CancellationToken cancel)
    {
        if(Busy || Available==null || !Installed)return;Busy=true;Failed=false;Progress=0;Status="Descargando actualización…";Changed?.Invoke();
        try
        {
            await manager.DownloadUpdatesAsync(Available,n=>{Progress=n;Status=$"Descargando actualización… {n}%";Changed?.Invoke();},cancel);
            Downloaded=true;Status="Descarga verificada. Puedes instalarla y reiniciar cuando estés listo.";
        }
        catch(OperationCanceledException){Status="Descarga cancelada. Tu versión actual sigue disponible.";}
        catch(Exception ex){Failed=true;Status="No se pudo descargar la actualización. "+Friendly(ex);}
        finally{Busy=false;Changed?.Invoke();}
    }
    public void InstallAndRestart()
    {
        if(Busy || !Downloaded || Available==null || !Installed)return;
        // Apply is only reachable from the main window, while its modal editors are closed.
        // Velopack waits for this process to exit before replacing the installed files.
        manager.ApplyUpdatesAndRestart(Available);
    }
    public async Task<bool> UpdateAndRestart(CancellationToken cancel,Action beforeRestart,Action? restartOverride=null)
    {
        if(Busy || Updating || Available==null || !Installed)return false;
        Updating=true;Failed=false;Changed?.Invoke();
        try
        {
            cancel.ThrowIfCancellationRequested();
            if(!Downloaded)await Download(cancel);
            cancel.ThrowIfCancellationRequested();
            if(!Downloaded)return false;
            beforeRestart();cancel.ThrowIfCancellationRequested();
            Restarting=true;Status="Actualización verificada. Reiniciando Avatar Studio…";Changed?.Invoke();
            if(restartOverride!=null)restartOverride();else InstallAndRestart();
            return true;
        }
        catch(OperationCanceledException){Status="Actualización cancelada. Puedes seguir usando esta versión.";return false;}
        catch(Exception ex){Failed=true;Status="No se pudo completar la actualización. "+Friendly(ex);return false;}
        finally{Restarting=false;Updating=false;Changed?.Invoke();}
    }
    static string Friendly(Exception ex)=>ex is HttpRequestException?"Revisa tu conexión a Internet.":ex.Message;
    public static void OpenReleases()=>Process.Start(new ProcessStartInfo(Repository+"/releases/latest"){UseShellExecute=true});
}

public sealed class UpdateNotificationGate
{
    readonly HashSet<string> shown=new(StringComparer.Ordinal);
    public void MarkShown(string? version){if(!string.IsNullOrWhiteSpace(version))shown.Add(version);}
    public bool TryShow(string? version,bool ownerReady)
    {
        if(!ownerReady || string.IsNullOrWhiteSpace(version))return false;
        return shown.Add(version);
    }
}
