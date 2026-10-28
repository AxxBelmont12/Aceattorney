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
    public int Progress {get;private set;}
    public string Status {get;private set;}="Busca nuevas versiones publicadas en GitHub Releases.";
    public bool Installed=>manager.IsInstalled && !manager.IsPortable;
    public string? AvailableVersion=>Available?.TargetFullRelease.Version.ToString();
    public AppUpdates(UpdateManager? manager=null)=>this.manager=manager??new UpdateManager(new GithubSource(Repository,null,false));
    public async Task Check()
    {
        if(Busy)return;Busy=true;Status="Buscando actualizaciones…";Changed?.Invoke();
        try
        {
            if(!Installed){Status="Esta copia es portátil. Instala Avatar Studio con el Setup de GitHub Releases para habilitar las actualizaciones.";return;}
            Available=await manager.CheckForUpdatesAsync();Downloaded=Available!=null && manager.UpdatePendingRestart?.Version==Available.TargetFullRelease.Version;
            Status=Available==null?"Tienes la versión más reciente.":$"Disponible: {AvailableVersion}"+(Downloaded?" · lista para instalar.":".");
        }
        catch(Exception ex){Status="No se pudo consultar GitHub. Puedes seguir usando la aplicación y volver a intentarlo. "+Friendly(ex);}
        finally{Busy=false;Changed?.Invoke();}
    }
    public async Task Download(CancellationToken cancel)
    {
        if(Busy || Available==null || !Installed)return;Busy=true;Progress=0;Status="Descargando actualización…";Changed?.Invoke();
        try
        {
            await manager.DownloadUpdatesAsync(Available,n=>{Progress=n;Status=$"Descargando actualización… {n}%";Changed?.Invoke();},cancel);
            Downloaded=true;Status="Descarga verificada. Puedes instalarla y reiniciar cuando estés listo.";
        }
        catch(OperationCanceledException){Status="Descarga cancelada. Tu versión actual sigue disponible.";}
        catch(Exception ex){Status="No se pudo descargar la actualización. "+Friendly(ex);}
        finally{Busy=false;Changed?.Invoke();}
    }
    public void InstallAndRestart()
    {
        if(Busy || !Downloaded || Available==null || !Installed)return;
        // Apply is only reachable from the main window, while its modal editors are closed.
        // Velopack waits for this process to exit before replacing the installed files.
        manager.ApplyUpdatesAndRestart(Available);
    }
    static string Friendly(Exception ex)=>ex is HttpRequestException?"Revisa tu conexión a Internet.":ex.Message;
    public static void OpenReleases()=>Process.Start(new ProcessStartInfo(Repository+"/releases/latest"){UseShellExecute=true});
}

public sealed class UpdatesWindow:Window
{
    readonly AppUpdates updates;
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,14,0,10)};
    readonly TextBox notes=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=MainWindow.Navy,Foreground=Brushes.White,BorderThickness=new Thickness(0),MinHeight=80};
    readonly Button check=new(){Content="Buscar ahora"},download=new(){Content="Descargar actualización"},install=new(){Content="Instalar y reiniciar"},cancel=new(){Content="Cancelar descarga"};
    readonly ProgressBar progress=new(){Minimum=0,Maximum=100,Height=8,Margin=new Thickness(0,5,0,8)};
    CancellationTokenSource? stop;
    public UpdatesWindow(AppUpdates updates,Preferences preferences,bool testing=false)
    {
        this.updates=updates;Title="Avatar Studio · Actualizaciones";Width=660;Height=570;MinWidth=580;MinHeight=480;Background=MainWindow.Navy;Foreground=Brushes.White;FontFamily=new("Segoe UI");FontSize=14;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid{Margin=new Thickness(24)};Content=root;root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var top=new StackPanel();root.Children.Add(top);top.Children.Add(new TextBlock{Text="AVATAR STUDIO",FontSize=26,Foreground=Brushes.Gold});top.Children.Add(new TextBlock{Text="Versión instalada: "+AppUpdates.Version,Margin=new Thickness(0,8,0,6)});
        var auto=new CheckBox{Content="Buscar actualizaciones al abrir la aplicación",IsChecked=preferences.CheckUpdatesOnStart,Foreground=Brushes.White,Margin=new Thickness(0,10,0,2)};auto.Click+=(_,_)=>{preferences.CheckUpdatesOnStart=auto.IsChecked==true;if(!testing)preferences.Save();};top.Children.Add(auto);top.Children.Add(status);top.Children.Add(progress);
        var explanation=new TextBlock{Text="La aplicación consulta las versiones publicadas en GitHub. La descarga se verifica antes de instalar. Tú eliges cuándo reiniciar; los proyectos y ajustes se conservan.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)};top.Children.Add(explanation);
        Grid.SetRow(notes,1);root.Children.Add(notes);
        var buttons=new WrapPanel{Margin=new Thickness(0,10,0,0)};Grid.SetRow(buttons,2);root.Children.Add(buttons);
        foreach(var b in new[]{check,download,install,cancel}){b.Padding=new Thickness(12,8,12,8);b.Margin=new Thickness(0,4,8,4);b.Background=new SolidColorBrush(Color.FromRgb(28,40,66));b.Foreground=Brushes.White;b.BorderThickness=new Thickness(0);buttons.Children.Add(b);}
        install.Background=Brushes.Gold;install.Foreground=MainWindow.Navy;
        check.Click+=async(_,_)=>await updates.Check();
        download.Click+=async(_,_)=>{stop=new();try{await updates.Download(stop.Token);}finally{stop.Dispose();stop=null;}};
        cancel.Click+=(_,_)=>stop?.Cancel();
        install.Click+=(_,_)=>{if(MessageBox.Show(this,"Se cerrará Avatar Studio para instalar la actualización y volver a abrirlo. ¿Continuar?","Instalar actualización",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;try{preferences.Save();updates.InstallAndRestart();}catch(Exception ex){MessageBox.Show(this,ex.Message,"No se pudo instalar");}};
        var releases=new Button{Content="Ver versiones en GitHub",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,4,8,4)};releases.Click+=(_,_)=>{try{AppUpdates.OpenReleases();}catch(Exception ex){status.Text=ex.Message;}};buttons.Children.Add(releases);
        updates.Changed+=OnChanged;Closing+=(_,e)=>{if(stop!=null){e.Cancel=true;stop.Cancel();status.Text="Cancelando descarga; espera un momento antes de cerrar.";}};Closed+=(_,_)=>updates.Changed-=OnChanged;Refresh();
        Loaded+=async(_,_)=>{if(!testing && !updates.Busy && updates.Available==null)await updates.Check();};
    }
    void OnChanged(){if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(Refresh);}
    void Refresh(){status.Text=updates.Status;progress.Value=updates.Progress;progress.IsIndeterminate=updates.Busy && stop==null;progress.Visibility=updates.Busy?Visibility.Visible:Visibility.Collapsed;check.IsEnabled=!updates.Busy;download.IsEnabled=!updates.Busy && updates.Available!=null && !updates.Downloaded;install.IsEnabled=!updates.Busy && updates.Downloaded;cancel.Visibility=stop!=null?Visibility.Visible:Visibility.Collapsed;notes.Text=updates.Available?.TargetFullRelease.NotesMarkdown??"Las novedades de la próxima versión aparecerán aquí.";}
}
