using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace AvatarStudio;
public sealed record VideoJob(string Name,DialogueSequence Sequence);
public static class VideoExportPlan
{
    public static string SafeName(string value)
    {var invalid=Path.GetInvalidFileNameChars();string clean=new(value.Select(c=>invalid.Contains(c)?'_':c).ToArray());clean=clean.Trim().TrimEnd('.');return string.IsNullOrWhiteSpace(clean)?"Video":clean[..Math.Min(80,clean.Length)];}
    public static List<VideoJob> Create(DialogueSequence source,bool whole,bool clips,bool scenes)
    {
        var jobs=new List<VideoJob>();if(whole)jobs.Add(new("00-"+SafeName(source.Name),DialogueSequence.Clone(source)));
        DialogueSequence Part(IEnumerable<DialogueShot> shots){var copy=DialogueSequence.Clone(source);copy.Clips=shots.Select(DialogueSequence.Clone).ToList();return copy;}
        if(clips)for(int i=0;i<source.Clips.Count;i++)jobs.Add(new($"clip-{i+1:000}-"+SafeName(source.Clips[i].Name),Part(new[]{source.Clips[i]})));
        if(scenes){int i=0;foreach(var group in source.Clips.GroupBy(c=>c.SceneName))jobs.Add(new($"escena-{++i:000}-"+SafeName(group.Key),Part(group)));}
        return jobs;
    }
}
public sealed partial class DialogueSequenceWindow
{
    void ShowBatchExport()
    {
        if(busy || sequence.Clips.Count==0)return;
        var dialog=ToolWindow("Exportación por lotes",560,450);var panel=new StackPanel{Margin=new Thickness(22)};dialog.Content=panel;
        panel.Children.Add(Text("EXPORTAR CONVERSACIÓN",22,Brushes.Gold));CheckBox whole=new CheckBox{Content="Video completo",IsChecked=true,Foreground=Brushes.White,Margin=new Thickness(0,15,0,10)},clips=new CheckBox{Content="Un video por fragmento",Foreground=Brushes.White,Margin=new Thickness(0,0,0,10)},scenes=new CheckBox{Content="Un video por escena",Foreground=Brushes.White,Margin=new Thickness(0,0,0,15)};
        panel.Children.Add(whole);panel.Children.Add(clips);panel.Children.Add(scenes);var format=new ComboBox{ItemsSource=new[]{"MOV · ProRes 4444 · transparencia","WebM · VP9 · transparencia","MP4 · H.264 · zonas transparentes en negro"},SelectedIndex=0,Foreground=MainWindow.Navy};panel.Children.Add(format);
        panel.Children.Add(Text("Los archivos se crean en una carpeta nueva, con nombres ordenados. Cancelar conserva los videos que ya hayan terminado.",12,Brushes.LightSteelBlue));
        panel.Children.Add(Button("Elegir carpeta y exportar…",async(_,_)=>
        {
            var jobs=VideoExportPlan.Create(sequence,whole.IsChecked==true,clips.IsChecked==true,scenes.IsChecked==true);if(jobs.Count==0){status.Text="Elige al menos una salida.";return;}
            var folder=new OpenFolderDialog{Title="Carpeta de destino de los videos"};if(folder.ShowDialog(dialog)!=true)return;
            string extension=format.SelectedIndex==1?".webm":format.SelectedIndex==2?".mp4":".mov";
            string destination=Path.Combine(folder.FolderName,VideoExportPlan.SafeName(sequence.Name)+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..5]);dialog.Close();await ExportJobs(jobs,destination,extension);
        }));dialog.ShowDialog();
    }
    async Task ExportJobs(List<VideoJob> jobs,string folder,string extension)
    {
        if(busy)return;Pause();busy=true;toolbar.IsEnabled=inspector.IsEnabled=track.IsEnabled=play.IsEnabled=seek.IsEnabled=false;cancel.Visibility=Visibility.Visible;exportCancel=new();var completed=new List<string>();string state="completo";
        try
        {
            Directory.CreateDirectory(folder);int total=jobs.Sum(j=>{var c=j.Sequence.Compile(assets.Library);return (int)Math.Ceiling(c.Timeline.Duration*c.Project.Fps);}),done=0;
            for(int i=0;i<jobs.Count;i++)
            {
                exportCancel.Token.ThrowIfCancellationRequested();var job=jobs[i];var c=job.Sequence.Compile(assets.Library);string output=Path.Combine(folder,job.Name+extension);int number=i+1,baseCount=done;
                var progress=new Progress<(int Current,int Total)>(p=>status.Text=$"Exportando {number}/{jobs.Count} · {(baseCount+p.Current)*100/Math.Max(1,total)} % · {job.Name}");
                await DialogueExport.ExportAsync(c.Project,c.Timeline,assets,output,Dispatcher,progress,exportCancel.Token);completed.Add(Path.GetFileName(output));done+=(int)Math.Ceiling(c.Timeline.Duration*c.Project.Fps);
            }
            status.Text=$"{completed.Count} videos exportados en {folder}";
        }
        catch(OperationCanceledException){state="cancelado";status.Text=$"Exportación cancelada. Se conservaron {completed.Count} videos terminados en {folder}";}
        catch(Exception ex){state="error";status.Text=ex.Message;}
        finally
        {
            if(Directory.Exists(folder))try{File.WriteAllText(Path.Combine(folder,"exportacion.json"),JsonSerializer.Serialize(new{state,completed,requested=jobs.Select(j=>j.Name+extension)},AssetStore.Json));}catch(IOException){}
            busy=false;toolbar.IsEnabled=inspector.IsEnabled=track.IsEnabled=play.IsEnabled=seek.IsEnabled=true;cancel.Visibility=Visibility.Collapsed;exportCancel?.Dispose();exportCancel=null;if(closeAfterExport)Close();
        }
    }
}
