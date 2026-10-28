using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AvatarStudio;
public sealed partial class DialogueSequenceWindow
{
    void BuildWordMenu()
    {
        var menu=new ContextMenu();quickText.ContextMenu=menu;
        foreach(var item in new[]{("Cortar",ApplicationCommands.Cut),("Copiar",ApplicationCommands.Copy),("Pegar",ApplicationCommands.Paste)})menu.Items.Add(new MenuItem{Header=item.Item1,Command=item.Item2,CommandTarget=quickText});menu.Items.Add(new Separator());
        var pauses=new MenuItem{Header="Pausa después de la selección o del cursor"};foreach(double seconds in new[]{.25,.5,1,2}){var item=new MenuItem{Header=$"{seconds:0.##} segundos"};item.Click+=(_,_)=>ApplyWordStyle(pause:seconds);pauses.Items.Add(item);}menu.Items.Add(pauses);
        var speed=new MenuItem{Header="Velocidad de la selección"};foreach(int rate in new[]{10,20,30,45,60}){var item=new MenuItem{Header=$"{rate} letras por segundo"};item.Click+=(_,_)=>ApplyWordStyle(rate:rate);speed.Items.Add(item);}menu.Items.Add(speed);
        var colors=new MenuItem{Header="Destacar palabras"};foreach(string color in new[]{"Naranja","Verde","Azul","Blanco"}){var item=new MenuItem{Header=color};item.Click+=(_,_)=>ApplyWordStyle(color:color);colors.Items.Add(item);}menu.Items.Add(colors);
    }
    void ApplyWordStyle(double? pause=null,double? rate=null,string? color=null)
    {
        int start=quickText.SelectionStart,length=quickText.SelectionLength;
        if(length==0){if(start==0){status.Text="Selecciona palabras o coloca el cursor después de una letra para insertar la pausa.";return;}start--;length=1;}
        if(!CommitQuick() || Selected is not {} shot)return;
        try
        {
            var p=shot.Project.Copy();var (a,b)=DialogueEdits.Snap(p.Text,start,length);
            // Split at authored pose boundaries so timing/emphasis never removes a pose.
            var boundaries=p.Ranges.SelectMany(r=>new[]{r.Start,r.End}).Where(n=>n>a && n<b).Append(a).Append(b).Distinct().OrderBy(n=>n).ToArray();
            for(int i=0;i<boundaries.Length-1;i++)
            {
                int from=boundaries[i],to=boundaries[i+1];var old=p.Ranges.FirstOrDefault(r=>r.Start<=from && r.End>=to);
                var r=old?.Copy()??new DialogueRange{PoseId=p.DefaultPose,Mode=p.DefaultMode,Color=p.DefaultColor,DisplayName=p.DisplayName};
                r.Id=Guid.NewGuid().ToString("N");r.Start=from;r.End=to;if(rate!=null)r.CharactersPerSecond=rate;if(color!=null)r.Color=color;
                if(old!=null && to!=old.End)r.PauseAfter=0;if(pause!=null && to==b)r.PauseAfter=pause.Value;DialogueEdits.AssignRange(p,r);
            }
            if(pause!=null || rate!=null){p.FitTextToVoice=false;p.SyncSpeechWords=false;}p.Validate(assets.Library);Remember();shot.Project=p;compiled=null;Refresh(shot.Id);Changed();quickText.Select(a,b-a);position=ClipStart(shot.Id)+Math.Max(0,DialogueCompiler.Compile(p,assets.Library).TimeFor(a)-shot.In);Render();
            status.Text=pause!=null || rate!=null?"Ritmo aplicado. La escritura usa tiempos manuales en este fragmento; la grabación conserva su posición.":"Énfasis aplicado sin cambiar las poses.";
        }
        catch(Exception ex){status.Text=ex.Message;}
    }
}
