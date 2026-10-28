using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AvatarStudio;

public sealed partial class DialogueSequenceWindow
{
    readonly CheckBox repeatPlayback=new(){Content="Repetir",Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,8,0)};
    double playbackFrom,playbackTo;
    bool playingSelection;
    (double Start,double End) SelectedBounds()
    {
        var shot=Selected??throw new InvalidOperationException("Selecciona un fragmento de la línea de tiempo.");
        double start=ClipStart(shot.Id);return(start,start+shot.Duration(assets.Library));
    }
    void PlaybackTick()
    {
        if(!playing)return;position=startedAt+watch.Elapsed.TotalSeconds;
        double end=playingSelection?playbackTo:Compile().Timeline.Duration;
        if(position>=end)
        {
            if(repeatPlayback.IsChecked==true){position=playingSelection?playbackFrom:0;startedAt=position;watch.Restart();media.Position=TimeSpan.FromSeconds(position);media.Play();}
            else{Pause();position=Math.Max(0,end-.000001);}
        }
        Render();
    }
    void ShowTemplates()
    {
        var dialog=new Window{Title="Plantillas de conversación",Owner=this,Width=530,Height=520,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=MainWindow.Navy,Foreground=Brushes.White,FontFamily=new("Segoe UI"),FontSize=14};
        var root=new StackPanel{Margin=new Thickness(24)};dialog.Content=root;
        root.Children.Add(Text("PREPARA TU CONVERSACIÓN",21,Brushes.Gold));
        root.Children.Add(Text("La plantilla añade fragmentos con personaje, posición y orientación listos. Después escribe tus diálogos.",13,Brushes.LightSteelBlue));
        var kind=new ComboBox{Foreground=MainWindow.Navy,ItemsSource=new[]{"Dos personajes enfrentados","Narrador central","Tres personajes"},SelectedIndex=0,Margin=new Thickness(0,10,0,10)};root.Children.Add(kind);
        var actors=assets.Library.Poses.Select(p=>p.Character).Distinct().OrderBy(c=>c).ToList();var fields=new List<ComboBox>();var labels=new List<TextBlock>();
        foreach(var choice in new[]{"Apollo Justice","Daryan Crescend","Trucy Wright"})
        {
            var label=Text("Personaje "+(fields.Count+1),12);labels.Add(label);root.Children.Add(label);
            var combo=new ComboBox{Foreground=MainWindow.Navy,ItemsSource=actors,SelectedItem=actors.Contains(choice)?choice:actors[fields.Count%actors.Count],Margin=new Thickness(0,2,0,8)};fields.Add(combo);root.Children.Add(combo);
        }
        void Layout(){int count=kind.SelectedIndex==1?1:kind.SelectedIndex==2?3:2;for(int i=0;i<3;i++)fields[i].Visibility=labels[i].Visibility=i<count?Visibility.Visible:Visibility.Collapsed;}
        kind.SelectionChanged+=(_,_)=>Layout();Layout();
        root.Children.Add(Text("← Izquierda     ·     Centro     ·     Derecha →",13,Brushes.Gold));
        var create=new Button{Content="Crear fragmentos con esta plantilla",Margin=new Thickness(0,18,0,0),Padding=new Thickness(12),Background=Brushes.Gold,Foreground=MainWindow.Navy};root.Children.Add(create);
        create.Click+=(_,_)=>{try{ApplyConversationTemplate(kind.SelectedIndex,fields.Select(f=>(string)f.SelectedItem).ToArray());dialog.Close();}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"Plantilla");}};
        dialog.ShowDialog();
    }
    void ApplyConversationTemplate(int kind,string[] characters)
    {
        Pause();var candidate=DialogueSequence.Clone(sequence);int count=kind==1?1:kind==2?3:2;
        if(candidate.Clips.Count+count>100)throw new InvalidOperationException("El montaje admite hasta 100 fragmentos.");
        for(int i=0;i<count;i++)
        {
            var pose=assets.Library.Poses.First(p=>p.Character==characters[i]);
            var p=new DialogueProject{DefaultPose=pose.Id,Text=$"Escribe aquí el diálogo de {characters[i]}.",AvatarX=count==1?0:i==0?-.25:i==1?.25:0,Mirror=i==1,AvatarScale=count==3?.85:1};
            if(candidate.Actors.TryGetValue(characters[i],out var saved)){p.CharactersPerSecond=saved.Speed;p.PageHold=saved.Hold;p.DefaultPose=saved.PoseId;}
            candidate.Clips.Add(new DialogueShot{Project=p,Name=characters[i]});candidate.Actors[characters[i]]=ActorPreset.From(p);
        }
        candidate.Validate(assets.Library);Remember();sequence=candidate;compiled=null;Refresh(sequence.Clips[^count].Id);Changed();status.Text="Plantilla creada. Selecciona cada tarjeta y sustituye su texto por tu diálogo.";
    }
}
