using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace AvatarStudio;
public sealed partial class DialogueSequenceWindow
{
    DateTime lastVersion=DateTime.MinValue;
    void ShowFragmentLibrary()
    {
        var dialog=ToolWindow("Biblioteca de fragmentos",650,680);var panel=new StackPanel{Margin=new Thickness(20)};dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(Text("FRAGMENTOS REUTILIZABLES",23,Brushes.Gold));var search=new TextBox{Margin=new Thickness(0,8,0,8)};panel.Children.Add(Text("Buscar por nombre o texto",12));panel.Children.Add(search);var list=new ListBox{Height=300,DisplayMemberPath="Name"};panel.Children.Add(list);
        List<(string Path,StoredFragment Fragment)> entries=new();
        void RefreshLibrary(){entries=ProjectStorage.Fragments(assets.Library).Where(f=>(f.Fragment.Name+f.Fragment.Shot.Project.Text).Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).ToList();list.ItemsSource=entries.Select(f=>f.Fragment).ToList();}
        search.TextChanged+=(_,_)=>RefreshLibrary();
        var title=new TextBox{Text=Selected?.Name??"Mi fragmento",MaxLength=120,Margin=new Thickness(0,8,0,6)};panel.Children.Add(Text("Nombre para guardar el fragmento actual",12));panel.Children.Add(title);
        panel.Children.Add(Button("Guardar fragmento actual en la biblioteca",(_,_)=>{try{if(Selected is not {} shot){status.Text="Selecciona un clip para guardarlo.";return;}ProjectStorage.SaveFragment(new(string.IsNullOrWhiteSpace(title.Text)?shot.Name:title.Text,DialogueSequence.Clone(shot)));RefreshLibrary();}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"Biblioteca");}}));
        panel.Children.Add(Button("Insertar el fragmento seleccionado",(_,_)=>{if(list.SelectedItem is StoredFragment value){InsertStoredFragment(value);dialog.Close();}}));
        panel.Children.Add(Button("Exportar fragmento portable…",(_,_)=>{if(list.SelectedItem is not StoredFragment value)return;var save=new SaveFileDialog{Filter="Fragmento portable|*.avfragment.json",FileName="fragmento.avfragment.json"};if(save.ShowDialog(dialog)==true)try{File.WriteAllText(save.FileName,JsonSerializer.Serialize(value,AssetStore.Json));}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){MessageBox.Show(dialog,ex.Message,"Exportar fragmento");}}));
        panel.Children.Add(Button("Importar fragmento portable…",(_,_)=>{var open=new OpenFileDialog{Filter="Fragmento portable|*.avfragment.json"};if(open.ShowDialog(dialog)!=true)return;try{if(new FileInfo(open.FileName).Length>90_000_000)throw new InvalidDataException("Archivo demasiado grande.");var imported=JsonSerializer.Deserialize<StoredFragment>(File.ReadAllText(open.FileName),AssetStore.Json)!;new DialogueSequence{Clips=new(){imported.Shot}}.Validate(assets.Library);ProjectStorage.SaveFragment(imported);RefreshLibrary();}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"Importar fragmento");}}));
        panel.Children.Add(Text("Conserva texto, animaciones, voz, fondos y elementos. Puedes importar un diálogo desde la barra principal y guardarlo aquí.",12,Brushes.LightSteelBlue));RefreshLibrary();dialog.ShowDialog();
    }
    void InsertStoredFragment(StoredFragment value)
    {
        if(sequence.Clips.Count>=100){status.Text="Máximo 100 fragmentos.";return;}var copy=DialogueSequence.Clone(value.Shot);copy.Id=Guid.NewGuid().ToString("N");copy.Name=value.Name;
        var candidate=DialogueSequence.Clone(sequence);int at=Selected is {} shot?sequence.Clips.IndexOf(shot)+1:sequence.Clips.Count;candidate.Clips.Insert(at,copy);candidate.Validate(assets.Library);Remember();sequence=candidate;compiled=null;Refresh(copy.Id);Changed();
    }
    void ShowOrganizer()
    {
        var dialog=ToolWindow("Proyecto · escenas y búsqueda",780,750);var panel=new StackPanel{Margin=new Thickness(20)};dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(Text("ORGANIZA TU VIDEO",23,Brushes.Gold));var projectName=new TextBox{Text=sequence.Name,MaxLength=120};panel.Children.Add(Text("Nombre del proyecto",12));panel.Children.Add(projectName);
        panel.Children.Add(Button("Guardar nombre del proyecto",(_,_)=>{Remember();sequence.Name=string.IsNullOrWhiteSpace(projectName.Text)?"Mi video":projectName.Text.Trim();Changed();}));
        var search=new TextBox();panel.Children.Add(Text("Buscar texto, personaje, fragmento o escena",12));panel.Children.Add(search);var tree=new TreeView{Height=320,Margin=new Thickness(0,8,0,8)};panel.Children.Add(tree);
        void Populate()
        {
            tree.Items.Clear();foreach(var group in sequence.Clips.Where(c=>(c.Name+" "+c.SceneName+" "+c.Project.Text+" "+assets.Library.Poses.First(p=>p.Id==c.Project.DefaultPose).Character).Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).GroupBy(c=>c.SceneName))
            {var parent=new TreeViewItem{Header=group.Key,IsExpanded=true};foreach(var shot in group){string text=shot.Project.Text.Replace('\n',' ').Replace('\r',' ');if(text.Length>75)text=text[..75]+"…";parent.Items.Add(new TreeViewItem{Header=$"{sequence.Clips.IndexOf(shot)+1:00} · {shot.Name} · {text}",Tag=shot.Id});}tree.Items.Add(parent);}
        }
        search.TextChanged+=(_,_)=>Populate();var sceneName=new TextBox{Text=Selected?.SceneName??"Escena 1",MaxLength=120};panel.Children.Add(Text("Escena del fragmento seleccionado en la lista",12));panel.Children.Add(sceneName);
        tree.SelectedItemChanged+=(_,_)=>{if(tree.SelectedItem is TreeViewItem {Tag:string id})sceneName.Text=sequence.Clips.First(c=>c.Id==id).SceneName;};
        panel.Children.Add(Button("Cambiar escena de ese fragmento",(_,_)=>{if(tree.SelectedItem is not TreeViewItem {Tag:string id})return;Remember();sequence.Clips.First(c=>c.Id==id).SceneName=string.IsNullOrWhiteSpace(sceneName.Text)?"Escena 1":sceneName.Text.Trim();Changed();Populate();Refresh(Selected?.Id);}));
        panel.Children.Add(Button("Ir al fragmento",(_,_)=>{if(tree.SelectedItem is TreeViewItem {Tag:string id}){Refresh(id);dialog.Close();}}));
        panel.Children.Add(Button("Versiones anteriores y recuperación…",(_,_)=>{ShowVersions();Populate();}));Populate();dialog.ShowDialog();
    }
    void ShowVersions()
    {
        var dialog=ToolWindow("Versiones anteriores",640,480);var panel=new StackPanel{Margin=new Thickness(20)};dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(Text("VERSIONES DEL PROYECTO",22,Brushes.Gold));panel.Children.Add(Text("Se conserva una versión al guardar y periódicamente durante la edición. La recuperación también se puede deshacer.",12,Brushes.LightSteelBlue));
        var all=new CheckBox{Content="Mostrar versiones de todos mis proyectos",Foreground=Brushes.White,Margin=new Thickness(0,8,0,8)};panel.Children.Add(all);
        var list=new ListBox{Height=200};panel.Children.Add(list);
        void PopulateVersions()
        {
            string root=Path.Combine(ProjectStorage.Root,"history"),folder=all.IsChecked==true?root:Path.Combine(root,sequence.Id);
            var paths=Directory.Exists(folder)?Directory.GetFiles(folder,"*.avmontage.json.gz",all.IsChecked==true?SearchOption.AllDirectories:SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTime).Take(100).ToList():new List<string>();
            string Label(string path)
            {
                string name=Path.GetFileName(Path.GetDirectoryName(path))??"Proyecto";try{using var meta=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!,"project-info.json")));name=meta.RootElement.GetProperty("Name").GetString()??name;}catch(Exception ex) when(ex is IOException or JsonException or KeyNotFoundException){}
                return name+" · "+File.GetLastWriteTime(path).ToString("dd/MM/yyyy HH:mm:ss")+" · "+(new FileInfo(path).Length/1024)+" KB";
            }
            list.ItemsSource=paths.Select(p=>new VersionChoice(p,Label(p))).ToList();
        }
        all.Click+=(_,_)=>PopulateVersions();PopulateVersions();
        panel.Children.Add(Button("Guardar una versión ahora",(_,_)=>{try{ProjectStorage.WriteVersion(sequence,JsonSerializer.Serialize(sequence,AssetStore.Json));dialog.Close();ShowVersions();}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"Versiones");}}));
        panel.Children.Add(Button("Recuperar versión seleccionada",(_,_)=>{if(list.SelectedItem is not VersionChoice choice)return;try{var value=ProjectStorage.ReadVersion(choice.Path,assets.Library);Remember();sequence=value;compiled=null;Refresh(sequence.Clips.FirstOrDefault()?.Id);Changed();dialog.Close();status.Text="Versión recuperada. Deshacer devuelve el montaje anterior.";}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"Recuperación");}}));dialog.ShowDialog();
    }
    sealed record VersionChoice(string Path,string Label){public override string ToString()=>Label;}
    Window ToolWindow(string title,double width,double height)=>new(){Owner=this,Title=title,Width=Math.Min(width,SystemParameters.WorkArea.Width-30),Height=Math.Min(height,SystemParameters.WorkArea.Height-30),Background=MainWindow.Navy,Foreground=Brushes.White,FontFamily=new("Segoe UI"),WindowStartupLocation=WindowStartupLocation.CenterOwner};
}
