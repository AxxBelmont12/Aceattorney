using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace AvatarStudio;

public sealed partial class DialogueSequenceWindow
{
    readonly CheckBox wordMode=new(){Content="Editar solo las palabras seleccionadas",Foreground=Brushes.White};
    readonly ComboBox wordColor=new(){ItemsSource=new[]{"Blanco","Naranja","Verde","Azul"},SelectedIndex=0,Foreground=MainWindow.Navy};
    readonly TextBlock selectedWords=new(){Foreground=Brushes.Gold,TextWrapping=TextWrapping.Wrap};
    readonly WrapPanel wordChips=new();
    readonly TextBlock voiceInfo=new(){Foreground=Brushes.LightSteelBlue,TextWrapping=TextWrapping.Wrap};
    readonly CheckBox voiceWordSync=new(){Content="Usar tiempos de cada palabra",Foreground=Brushes.White};
    readonly CheckBox voiceFit=new(){Content="Ajustar escritura a la duración de mi voz",Foreground=Brushes.White};
    readonly CheckBox voiceMouth=new(){Content="Mover la boca con mi voz",Foreground=Brushes.White,IsChecked=true};
    readonly Slider voiceVolume=new(){Minimum=0,Maximum=1.5,Value=1};
    readonly Slider voiceThreshold=new(){Minimum=.001,Maximum=.15,Value=.015};
    readonly TextBlock voiceLevels=new(){Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap};
    CancellationTokenSource? voiceImportCancel;
    bool WordMode=>wordMode.IsChecked==true;

    void BuildConversationTools()
    {
        quickText.IsInactiveSelectionHighlightEnabled=true;
        var words=new StackPanel();words.Children.Add(wordMode);words.Children.Add(Text("Activa este modo, selecciona palabras en el texto y elige su personaje, pose y comportamiento arriba.",12,Brushes.LightSteelBlue));words.Children.Add(selectedWords);words.Children.Add(wordColor);
        words.Children.Add(Button("Aplicar animación a estas palabras",(_,_)=>AssignQuickWords(false)));
        words.Children.Add(Button("Quitar cambios de la selección",(_,_)=>AssignQuickWords(true)));
        words.Children.Add(new ScrollViewer{Content=wordChips,MaxHeight=130,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        inspector.Children.Add(new Expander{Header="Animaciones sobre palabras",Foreground=Brushes.White,Content=words});
        quickText.SelectionChanged+=(_,_)=>{if(quickLoading)return;selectedWords.Text=quickText.SelectionLength>0?"«"+quickText.SelectedText+"»":"Selecciona las palabras que quieres animar.";};
        wordMode.Click+=(_,_)=>{if(!WordMode && quickTemplate!=null){quickLoading=true;LoadActorControls(quickTemplate);quickLoading=false;}selectedWords.Text=WordMode?"Selecciona palabras y elige una pose; la pose base se conserva.":"Los controles de personaje vuelven a editar todo el fragmento.";};
        var voice=new StackPanel();voice.Children.Add(Button("Audio a texto…",(_,_)=>OpenTranscription(false)));voice.Children.Add(Button("Audio a texto y crear fragmentos…",(_,_)=>OpenTranscription(true)));voice.Children.Add(voiceInfo);AddWaveform(voice);voice.Children.Add(Button("Importar mi grabación…",async(_,_)=>await ImportVoice()));
        voice.Children.Add(Button("Quitar grabación",(_,_)=>{if(quickTemplate==null)return;quickTemplate.Recording=null;quickTemplate.FitTextToVoice=false;quickTemplate.SyncSpeechWords=false;quickTemplate.SpeechWords.Clear();voiceFit.IsChecked=false;QueueQuick();CommitQuick();RefreshVoice();}));
        voice.Children.Add(Text("WAV, MP3, M4A, OGG o FLAC · hasta 2 minutos por fragmento. El audio se guarda dentro del proyecto.",12,Brushes.LightSteelBlue));
        voice.Children.Add(voiceWordSync);voice.Children.Add(voiceFit);voice.Children.Add(voiceMouth);voice.Children.Add(voiceLevels);voice.Children.Add(Text("Volumen de mi voz",12));voice.Children.Add(voiceVolume);voice.Children.Add(Text("Umbral de la boca · súbelo si se mueve con ruido de fondo",12));voice.Children.Add(voiceThreshold);
        voice.Children.Add(Text("Sin tiempos por palabra, el ajuste de escritura es proporcional. Audio a texto permite reconocer palabras y revisar sus tiempos. Desactiva la sincronización para ajustar manualmente el ritmo. Los bips se sustituyen por tu grabación en este clip.",12,Brushes.LightSteelBlue));
        inspector.Children.Add(new Expander{Header="Mi voz y sincronización",Foreground=Brushes.White,Content=voice});
        voiceWordSync.Click+=(_,_)=>{if(voiceWordSync.IsChecked==true)voiceFit.IsChecked=false;voiceFit.IsEnabled=quickTemplate?.Recording!=null && voiceWordSync.IsChecked!=true;QueueQuick();};voiceFit.Click+=(_,_)=>QueueQuick();voiceMouth.Click+=(_,_)=>QueueQuick();voiceVolume.ValueChanged+=(_,_)=>{VoiceLabels();if(!quickLoading)QueueQuick();};voiceThreshold.ValueChanged+=(_,_)=>{VoiceLabels();if(!quickLoading)QueueQuick();};
    }
    void VoiceLabels()=>voiceLevels.Text=$"Volumen: {voiceVolume.Value*100:0}% · umbral: {voiceThreshold.Value:0.000}";
    void RefreshVoice()
    {
        bool before=quickLoading;quickLoading=true;var voice=quickTemplate?.Recording;
        voiceInfo.Text=voice==null?"Sin grabación · se usan los sonidos del juego.":$"{voice.Name} · archivo {voice.SourceDuration:0.00} s · recorte {voice.TrimLength:0.00} s · inicio {voice.Offset:0.00} s";
        voiceWordSync.IsChecked=quickTemplate?.SyncSpeechWords==true;voiceWordSync.IsEnabled=voice!=null && quickTemplate!.SpeechWords.Count>0;voiceFit.IsChecked=quickTemplate?.FitTextToVoice==true;voiceMouth.IsChecked=voice?.LipSync??true;voiceVolume.Value=voice?.Volume??1;voiceThreshold.Value=voice?.Threshold??.015;
        voiceFit.IsEnabled=voiceMouth.IsEnabled=voiceVolume.IsEnabled=voiceThreshold.IsEnabled=voice!=null;voiceFit.IsEnabled=voice!=null && voiceWordSync.IsChecked!=true;VoiceLabels();RefreshWaveform();quickLoading=before;
    }
    async Task ImportVoice()
    {
        if(busy || quickTemplate==null)return;
        var dialog=new OpenFileDialog{Filter="Grabaciones de voz|*.wav;*.mp3;*.m4a;*.ogg;*.flac;*.opus;*.aac;*.wma|Todos los archivos|*.*"};if(dialog.ShowDialog(this)!=true)return;
        Pause();busy=true;voiceImportCancel=new();inspector.IsEnabled=toolbar.IsEnabled=track.IsEnabled=play.IsEnabled=false;
        try
        {
            status.Text="Preparando tu grabación…";var voice=await RecordedVoice.ImportAsync(dialog.FileName,voiceImportCancel.Token);
            quickTemplate.Recording=voice;quickTemplate.FitTextToVoice=true;quickTemplate.SyncSpeechWords=false;quickTemplate.SpeechWords.Clear();RefreshVoice();QueueQuick();CommitQuick();
            status.Text="Grabación importada. La boca responde al audio; puedes ajustar el umbral si hay ruido.";
        }
        catch(OperationCanceledException){status.Text="Importación cancelada.";}
        catch(Exception ex){status.Text=ex.Message;}
        finally{busy=false;voiceImportCancel?.Dispose();voiceImportCancel=null;inspector.IsEnabled=toolbar.IsEnabled=track.IsEnabled=play.IsEnabled=true;if(closeAfterExport)Close();}
    }
    void LoadActorControls(DialogueProject p)
    {
        var pose=assets.Library.Poses.First(x=>x.Id==p.DefaultPose);quickCharacter.SelectedItem=pose.Character;
        quickPose.ItemsSource=assets.Library.Poses.Where(x=>x.Character==pose.Character).ToList();quickPose.SelectedItem=pose;
        quickMode.SelectedItem=p.DefaultMode;quickSpeed.Value=p.CharactersPerSecond;quickHold.Value=p.PageHold;LoadPlacement(p);
    }
    void ChangeQuickCharacter()
    {
        if(quickLoading || quickCharacter.SelectedItem is not string character)return;
        quickLoading=true;
        var poses=assets.Library.Poses.Where(p=>p.Character==character).ToList();quickPose.ItemsSource=poses;quickPose.SelectedIndex=0;
        if(!WordMode && quickTemplate!=null)
        {
            var p=quickTemplate.Copy();
            if(sequence.Actors.TryGetValue(character,out var remembered))remembered.Apply(p);
            else {p.DefaultPose=poses[0].Id;p.DisplayName="";}
            quickTemplate.DisplayName=p.DisplayName;LoadActorControls(p);
        }
        quickLoading=false;QueueActorChange();
    }
    void QueueActorChange(){if(!WordMode)QueueQuick();else selectedWords.Text="Pose preparada. Pulsa «Aplicar animación a estas palabras».";}
    void RefreshWordChips()
    {
        wordChips.Children.Clear();if(quickTemplate==null)return;
        var timeline=DialogueCompiler.Compile(quickTemplate,assets.Library);
        foreach(var r in quickTemplate.Ranges.OrderBy(r=>r.Start))
        {
            string words=quickTemplate.Text[r.Start..r.End];if(words.Length>30)words=words[..30]+"…";
            var pose=assets.Library.Poses.First(p=>p.Id==r.PoseId);double a=timeline.TimeFor(r.Start);var last=timeline.Pages.SelectMany(p=>p.Glyphs).LastOrDefault(g=>g.OriginalStart<r.End && g.OriginalEnd>r.Start);double b=last==null?a:last.At+last.Duration;
            string timing=(r.PauseAfter>0?$" · pausa {r.PauseAfter:0.##} s":"")+(r.CharactersPerSecond!=null?$" · {r.CharactersPerSecond:0} letras/s":"");
            var chip=Button($"«{words}» · {pose.Name} · {a:0.0}–{b:0.0} s{timing}",(_,_)=>
            {
                var current=quickTemplate?.Ranges.FirstOrDefault(x=>x.Id==r.Id);if(current==null)return;
                quickLoading=true;wordMode.IsChecked=true;quickText.Select(current.Start,current.End-current.Start);
                quickCharacter.SelectedItem=pose.Character;quickPose.ItemsSource=assets.Library.Poses.Where(p=>p.Character==pose.Character).ToList();quickPose.SelectedItem=pose;quickMode.SelectedItem=r.Mode;wordColor.SelectedItem=r.Color;
                quickLoading=false;selectedWords.Text="«"+quickText.SelectedText+"»";Pause();position=ClipStart(quickId)+Math.Max(0,a-(Selected?.In??0));Render();
            });chip.FontSize=11;chip.ToolTip=$"{pose.Character} · {pose.Name} · {r.Mode}";wordChips.Children.Add(chip);
        }
    }
    void AssignQuickWords(bool remove)
    {
        if(Selected is not {} shot || quickText.SelectionLength==0 || quickPose.SelectedItem is not Pose pose){status.Text="Selecciona unas palabras del fragmento y elige una pose.";return;}
        try
        {
            var (a,b)=DialogueEdits.Snap(shot.Project.Text,quickText.SelectionStart,quickText.SelectionLength);
            var p=shot.Project.Copy();var range=new DialogueRange{Start=a,End=b,PoseId=pose.Id,Mode=quickMode.SelectedItem as string??"Hablar",Color=wordColor.SelectedItem as string??"Blanco"};
            DialogueEdits.AssignRange(p,range);if(remove)p.Ranges.Remove(range);p.Validate(assets.Library);
            Remember();shot.Project=p;compiled=null;quickTemplate=p.Copy();RefreshWordChips();Changed();position=ClipStart(shot.Id)+Math.Max(0,DialogueCompiler.Compile(p,assets.Library).TimeFor(a)-shot.In);Render();
            Refresh(shot.Id);quickLoading=true;wordMode.IsChecked=true;quickText.Select(a,b-a);quickLoading=false;position=ClipStart(shot.Id)+Math.Max(0,DialogueCompiler.Compile(p,assets.Library).TimeFor(a)-shot.In);Render();
            status.Text=remove?"Cambios retirados de las palabras seleccionadas.":"Animación asignada. Las tarjetas muestran dónde empieza y termina cada cambio.";
        }
        catch(Exception ex){status.Text=ex.Message;}
    }
    double ClipStart(string? id)=>sequence.Clips.TakeWhile(c=>c.Id!=id).Sum(c=>c.Duration(assets.Library));
}
