using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AvatarStudio;
public sealed record CourtCast
{
    public string Defense {get;init;}="pl0000";
    public string Prosecutor {get;init;}="pl0200";
    public string Accused {get;init;}="pl21100";
    public string Judge {get;init;}="pl0600";
    public void Validate(Library library){foreach(var id in new[]{Defense,Prosecutor,Accused,Judge})if(!library.Poses.Any(p=>p.Id==id))throw new InvalidDataException("No se encuentra un personaje del reparto del juicio.");}
}
public sealed partial class DialogueSequenceWindow
{
    sealed record CourtChoice(string Id,string Name){public override string ToString()=>Name;}
    void ConfigureCourt()
    {
        var dialog=new Window{Owner=this,Title="Modo juicio · reparto",Width=540,Height=600,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=MainWindow.Navy,Foreground=Brushes.White,FontFamily=new("Segoe UI")};
        var panel=new StackPanel{Margin=new Thickness(22)};dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};panel.Children.Add(Text("PREPARA EL JUICIO",23,Brushes.Gold));panel.Children.Add(Text("Elige el reparto. Cada intervención añade un plano que puedes ordenar, animar y editar como cualquier fragmento.",13,Brushes.LightSteelBlue));
        var choices=assets.Library.Poses.GroupBy(p=>p.Character).Select(g=>g.First()).Concat(assets.Library.Poses.Where(p=>p.Id=="pl11300" || p.Id=="pl1400")).Select(p=>new CourtChoice(p.Id,p.Character+(p.Id=="pl11300"?" · abogado":p.Id=="pl0300"?" · con gorro":p.Id=="pl1400"?" · alternativa":""))).OrderBy(p=>p.Name).ToList();
        var fields=new List<ComboBox>();foreach(var role in new[]{("Defensa",sequence.Court.Defense),("Fiscalía",sequence.Court.Prosecutor),("Acusado / testigo",sequence.Court.Accused),("Juez",sequence.Court.Judge)})
        {panel.Children.Add(Text(role.Item1,13,Brushes.Gold));var combo=new ComboBox{ItemsSource=choices,SelectedItem=choices.FirstOrDefault(p=>p.Id==role.Item2),Foreground=MainWindow.Navy,Margin=new Thickness(0,3,0,9)};fields.Add(combo);panel.Children.Add(combo);}
        void Store(){if(fields.Any(f=>f.SelectedItem==null))throw new InvalidDataException("Selecciona los cuatro personajes.");Remember();sequence.Court=new(){Defense=((CourtChoice)fields[0].SelectedItem).Id,Prosecutor=((CourtChoice)fields[1].SelectedItem).Id,Accused=((CourtChoice)fields[2].SelectedItem).Id,Judge=((CourtChoice)fields[3].SelectedItem).Id};Changed();}
        panel.Children.Add(Button("Guardar reparto",(_,_)=>{Store();dialog.Close();}));panel.Children.Add(Button("Guardar y crear defensa → fiscalía → testigo → juez",(_,_)=>{if(sequence.Clips.Count>96){status.Text="No hay espacio para cuatro planos.";return;}Store();foreach(var role in new[]{"Defensa","Fiscalía","Acusado / testigo","Juez"})AddCourtShot(role);dialog.Close();}));
        panel.Children.Add(Text("Los fondos y estrados proceden del primer Apollo Justice. El reparto usa los personajes disponibles en esta aplicación.",12,Brushes.LightSteelBlue));dialog.ShowDialog();
    }
    void BuildStudioInspector()
    {
        inspector.Children.Add(Button("Fondo, elementos y transiciones…",(_,_)=>EditScene()));
        var court=new WrapPanel();foreach(var role in new[]{"Defensa","Fiscalía","Acusado / testigo","Juez","Plano general"})court.Children.Add(Button("＋ "+role,(_,_)=>AddCourtShot(role)));
        var panel=new StackPanel();panel.Children.Add(Button("Elegir reparto del juicio…",(_,_)=>ConfigureCourt()));panel.Children.Add(court);inspector.Children.Add(new Expander{Header="Modo juicio · intercalar planos",Foreground=Brushes.White,Content=panel});
    }
    void AddCourtShot(string role)
    {
        if(sequence.Clips.Count>=100){status.Text="Máximo 100 fragmentos.";return;}
        string pose=role=="Defensa"?sequence.Court.Defense:role=="Fiscalía"?sequence.Court.Prosecutor:role=="Acusado / testigo"?sequence.Court.Accused:sequence.Court.Judge;
        string bg=role=="Defensa"?"court-defense":role=="Fiscalía"?"court-prosecutor":role=="Acusado / testigo"?"court-witness":role=="Juez"?"court-judge":"court-wide";
        string fg=role=="Juez"?"desk-judge":role=="Acusado / testigo"?"desk-witness":role=="Plano general"?"":"desk-front";
        var p=new DialogueProject{DefaultPose=pose,Text=$"Escribe la intervención de {role.ToLowerInvariant()}.",AvatarX=role=="Acusado / testigo"?-.08:0,AvatarY=role=="Juez"?.08:0,AvatarScale=role=="Juez"?1.2:1,Mirror=role=="Fiscalía" && !new[]{"pl0200","pl0700"}.Contains(pose),Scene=new(){Background=bg,Foreground=fg,ShowAvatar=role!="Plano general"}};
        Add(p);if(Selected is {} shot){shot.Name=role;shot.SceneName="Juicio";Refresh(shot.Id);Changed();}status.Text="Plano de "+role+" añadido. Edita su texto y animación, o añade la siguiente intervención.";
    }
    void EditScene()
    {
        if(quickTemplate==null)return;Pause();var editor=new SceneEditorWindow(assets,quickTemplate){Owner=this};editor.ShowDialog();if(editor.Result==null)return;
        quickTemplate.Scene=editor.Result.Copy();QueueQuick();CommitQuick();status.Text="Fondo, elementos y transiciones guardados para este fragmento.";
    }
}
