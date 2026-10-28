using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AvatarStudio;

public sealed partial class DialogueSequenceWindow
{
    readonly TextBox quickText=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=135,MaxLength=10000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    readonly ComboBox quickCharacter=new(),quickPose=new(),quickMode=new();
    readonly Slider quickSpeed=new(){Minimum=5,Maximum=80,Value=30,TickFrequency=1,IsSnapToTickEnabled=true};
    readonly Slider quickHold=new(){Minimum=.2,Maximum=5,Value=1,TickFrequency=.1,IsSnapToTickEnabled=true};
    readonly TextBlock quickTiming=new(),quickSaved=new();
    readonly DispatcherTimer quickTimer=new(){Interval=TimeSpan.FromMilliseconds(550)};
    string? quickId;
    DialogueProject? quickTemplate;
    bool quickLoading,quickDirty,quickApplying,quickPreviewAtEnd;

    void BuildQuickEditor()
    {
        inspector.Children.Add(Text("1 · CREA UN FRAGMENTO",15,Brushes.Gold));
        inspector.Children.Add(Text("Texto del personaje",12));inspector.Children.Add(quickText);
        inspector.Children.Add(Text("Personaje",12));inspector.Children.Add(quickCharacter);
        inspector.Children.Add(Text("Pose / animación",12));inspector.Children.Add(quickPose);
        quickCharacter.ItemsSource=assets.Library.Poses.Select(p=>p.Character).Distinct().OrderBy(c=>c).ToList();
        quickMode.ItemsSource=new[]{"Hablar","Reposo","Gesto"};inspector.Children.Add(quickMode);
        BuildConversationTools();
        BuildPlacement();BuildStudioInspector();
        inspector.Children.Add(quickTiming);inspector.Children.Add(quickSpeed);inspector.Children.Add(Text("Pausa de lectura al terminar cada página",12));inspector.Children.Add(quickHold);
        quickSaved.Foreground=Brushes.LightSteelBlue;quickSaved.TextWrapping=TextWrapping.Wrap;inspector.Children.Add(quickSaved);
        var next=Button("＋ Siguiente fragmento   Ctrl+Enter",(_,_)=>NewClip());next.Background=Brushes.Gold;next.Foreground=MainWindow.Navy;inspector.Children.Add(next);
        inspector.Children.Add(Text("2 · ORDENA Y REVISA",15,Brushes.Gold));
        inspector.Children.Add(Text("Selecciona un clip abajo para corregirlo. Arrástralo para cambiar su orden. Espacio reproduce el montaje fuera del cuadro de texto.",12,Brushes.LightSteelBlue));
        details.TextWrapping=TextWrapping.Wrap;details.Foreground=Brushes.LightSteelBlue;inspector.Children.Add(details);
        foreach(var input in new Control[]{quickText,quickCharacter,quickPose,quickMode,name,start,end}){input.Background=new SolidColorBrush(Color.FromRgb(23,35,56));input.Foreground=Brushes.White;input.Padding=new Thickness(7);input.Margin=new Thickness(0,2,0,3);}
        foreach(var combo in new[]{quickCharacter,quickPose,quickMode}){combo.Foreground=MainWindow.Navy;combo.Background=Brushes.White;}
        BuildWordMenu();
        quickText.TextChanged+=(_,_)=>QueueQuick();
        quickCharacter.SelectionChanged+=(_,_)=>ChangeQuickCharacter();
        quickPose.SelectionChanged+=(_,_)=>{if(!quickLoading)QueueActorChange();};quickMode.SelectionChanged+=(_,_)=>{if(!quickLoading)QueueActorChange();};
        quickSpeed.ValueChanged+=(_,_)=>QueueQuick();quickHold.ValueChanged+=(_,_)=>QueueQuick();
        quickTimer.Tick+=(_,_)=>{quickTimer.Stop();CommitQuick();};
    }
    void LoadQuick(string? id,DialogueProject p)
    {
        quickTimer.Stop();quickPreviewAtEnd=false;quickLoading=true;quickId=id;quickTemplate=DialogueSequence.Clone(p);quickDirty=false;wordMode.IsChecked=false;
        quickText.Text=p.Text;var pose=assets.Library.Poses.First(x=>x.Id==p.DefaultPose);
        quickCharacter.SelectedItem=pose.Character;quickPose.ItemsSource=assets.Library.Poses.Where(x=>x.Character==pose.Character).ToList();quickPose.SelectedItem=pose;
        LoadPlacement(p);quickMode.SelectedItem=p.DefaultMode;quickSpeed.Value=p.CharactersPerSecond;quickHold.Value=p.PageHold;quickLoading=false;
        RefreshVoice();RefreshWordChips();QuickTiming();quickSaved.Text=id==null?"Se añadirá automáticamente cuando escribas.":"Guardado automáticamente · puedes seguir escribiendo.";
    }
    void QuickTiming()
    {
        bool words=quickTemplate?.SpeechWords.Count>0 && voiceWordSync.IsChecked==true;
        bool fitted=words || quickTemplate?.Recording!=null && voiceFit.IsChecked==true;
        quickSpeed.IsEnabled=quickHold.IsEnabled=!fitted;
        quickTiming.Text=words?"Escritura sincronizada con los tiempos de cada palabra":fitted?$"Escritura ajustada a tu voz · {quickTemplate!.Recording!.Duration:0.00} s":$"Escritura: {quickSpeed.Value:0} letras/s · lectura: {quickHold.Value:0.0} s";
    }
    void QueueQuick()
    {
        if(quickLoading || quickApplying || quickTemplate==null)return;
        QuickTiming();quickDirty=true;quickSaved.Text="Actualizando…";Pause();quickTimer.Stop();quickTimer.Start();
    }
    bool CommitQuick()
    {
        if(!quickDirty || quickApplying || quickTemplate==null)return true;
        quickTimer.Stop();
        if(quickId==null && string.IsNullOrWhiteSpace(quickText.Text)){quickDirty=false;return true;}
        try
        {
            if(string.IsNullOrWhiteSpace(quickText.Text))throw new InvalidDataException("Escribe el diálogo o usa Eliminar para quitar este fragmento.");
            var p=DialogueSequence.Clone(quickTemplate);DialogueEdits.AdjustRanges(p,p.Text,quickText.Text);
            if(!WordMode){p.DefaultPose=(quickPose.SelectedItem as Pose)?.Id??p.DefaultPose;p.DefaultMode=quickMode.SelectedItem as string??"Hablar";}
            p.AvatarX=avatarX.Value;p.AvatarY=avatarY.Value;p.AvatarScale=avatarSize.Value;p.Mirror=placementMirror.IsChecked==true;
            p.CharactersPerSecond=Math.Round(quickSpeed.Value);p.PageHold=Math.Round(quickHold.Value,2);p.SyncSpeechWords=voiceWordSync.IsChecked==true && p.SpeechWords.Count>0;p.FitTextToVoice=voiceFit.IsChecked==true;
            if(p.Recording is {} recording)p.Recording=recording with {Volume=voiceVolume.Value,Threshold=voiceThreshold.Value,LipSync=voiceMouth.IsChecked==true};
            p.Validate(assets.Library);
            var candidate=DialogueSequence.Clone(sequence);var shot=candidate.Clips.FirstOrDefault(c=>c.Id==quickId);
            if(shot==null){if(candidate.Clips.Count>=100)throw new InvalidDataException("Máximo 100 fragmentos.");shot=new DialogueShot{Name=$"Clip {candidate.Clips.Count+1:00}",SceneName=sequence.Clips.LastOrDefault()?.SceneName??"Escena 1"};candidate.Clips.Add(shot);}
            shot.Project=p;candidate.Actors[assets.Library.Poses.First(x=>x.Id==p.DefaultPose).Character]=ActorPreset.From(p);double full=DialogueCompiler.Compile(p,assets.Library).Duration;
            if(shot.In>=full-.1)shot.In=0;if(shot.Out>full || shot.Out<=shot.In)shot.Out=null;
            candidate.Validate(assets.Library);Remember();sequence=candidate;quickId=shot.Id;quickTemplate=DialogueSequence.Clone(p);quickDirty=false;compiled=null;
            quickApplying=true;Refresh(shot.Id);quickApplying=false;
            start.Text=shot.In.ToString("0.###");end.Text=shot.Out?.ToString("0.###")??"";name.Text=shot.Name;
            position=sequence.Clips.TakeWhile(c=>c.Id!=shot.Id).Sum(c=>(c.Out??DialogueCompiler.Compile(c.Project,assets.Library).Duration)-c.In);
            position+=Math.Max(0,(shot.Out??full)-shot.In-.05);
            quickPreviewAtEnd=true;Changed();details.Text=$"{full:0.0} s · {p.Ranges.Count} ajustes por palabras";
            RefreshVoice();RefreshWordChips();quickSaved.Text="Guardado automáticamente"+(p.Ranges.Count>0?" · conserva ajustes por palabras (Avanzados).":"");return true;
        }
        catch(Exception ex){quickApplying=false;quickSaved.Text=ex.Message;return false;}
    }
    public void VerifyQuickEditor()
    {
        var first=sequence.Clips[0];Refresh(first.Id);quickText.Text="Prueba de edición integrada con animación.";
        quickSpeed.Value=40;quickHold.Value=1.5;
        if(!CommitQuick() || sequence.Clips[0].Project.Text!=quickText.Text || sequence.Clips[0].Project.CharactersPerSecond!=40)throw new InvalidDataException("Inline edit not saved");
        int count=sequence.Clips.Count;NewClip();quickText.Text="Segundo fragmento sin abrir otra ventana.";
        if(!CommitQuick() || sequence.Clips.Count!=count+1 || sequence.Clips[^1].Project.CharactersPerSecond!=40)throw new InvalidDataException("Inline next clip / settings inheritance");
        var last=sequence.Clips[^1];quickText.Text="Cambio inmediato antes de seleccionar otro clip.";
        track.SelectedItem=track.Items.Cast<Card>().First(c=>c.Id==first.Id);
        if(sequence.Clips.First(c=>c.Id==last.Id).Project.Text!="Cambio inmediato antes de seleccionar otro clip." || quickId!=first.Id)throw new InvalidDataException("Pending text lost on clip selection");
        History(false); // undo the last text edit before testing clip creation history
        History(false);if(sequence.Clips.Count!=count)throw new InvalidDataException("Undo inline clip");
        History(true);if(sequence.Clips.Count!=count+1)throw new InvalidDataException("Redo inline clip");
        quickText.Text="";if(CommitQuick())throw new InvalidDataException("Empty existing clip accepted");
        quickText.Text="Texto recuperado.";if(!CommitQuick())throw new InvalidDataException("Invalid inline edit recovery");
        NewClip();if(!CommitQuick() || sequence.Clips.Count!=count+1)throw new InvalidDataException("Blank draft added to timeline");
    }
}
