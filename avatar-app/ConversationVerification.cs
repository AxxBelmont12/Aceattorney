using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AvatarStudio;

public static class ConversationVerification
{
    static void Check(bool value,string name){if(!value)throw new InvalidDataException(name);}
    public static async Task RunAsync(AssetStore assets,string folder)
    {
        var bytes=new byte[(int)(1.2*DialogueAudio.Rate)*2];
        for(int i=0;i<bytes.Length/2;i++)
        {
            double at=i/(double)DialogueAudio.Rate;
            short sample=(short)((at>=.1 && at<.4 || at>=.75 && at<1)?Math.Sin(i*2*Math.PI*220/DialogueAudio.Rate)*12000:0);
            BitConverter.TryWriteBytes(bytes.AsSpan(i*2,2),sample);
        }
        var voice=new RecordedVoice{Name="Prueba técnica de audio.wav",Pcm=bytes};voice.Validate();
        Check(voice.Speaking(.2) && !voice.Speaking(.65) && !voice.Speaking(1.15),"Voice envelope follows sound and silence");
        var project=new DialogueProject{Text="Una grabación propia mueve la boca con el sonido.",Recording=voice,FitTextToVoice=true,FinalHold=.3};
        var line=DialogueCompiler.Compile(project,assets.Library);
        Check(Math.Abs(line.Duration-1.5)<1e-8 && line.At(.2,project).Speaking && !line.At(.65,project).Speaking && !line.At(1.4,project).Speaking,"Voice/text duration and silence mouth state");
        var gesture=project.Copy();gesture.DefaultMode="Gesto";var rest=project.Copy();rest.DefaultMode="Reposo";
        Check(DialogueRenderer.StraightBgra(new DialogueRenderer(assets).Render(gesture,DialogueCompiler.Compile(gesture,assets.Library),.6)).SequenceEqual(DialogueRenderer.StraightBgra(new DialogueRenderer(assets).Render(rest,DialogueCompiler.Compile(rest,assets.Library),.6))),"Voice silence closes gesture mouth on poses with a separate idle sequence");
        Check(DialogueAudio.Events(project,line,assets.Library).Count==0,"Voice replaces original blips");
        string wav=Path.Combine(folder,"voz-prueba.wav");DialogueAudio.Write(wav,project,line,assets.Library,line.Duration);
        var recorded=await RecordedVoice.ImportAsync(wav);
        Check(recorded.Duration==1.5 && recorded.Speaking(.2) && !recorded.Speaking(.65),"Real WAV import through bundled decoder");
        string mp3=Path.Combine(folder,"voz-prueba.mp3");
        var start=new ProcessStartInfo(await DialogueEncoder.PathAsync()){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-hide_banner","-loglevel","error","-y","-i",wav,"-c:a","libmp3lame",mp3})start.ArgumentList.Add(arg);
        using(var process=Process.Start(start)!){var errors=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();Check(process.ExitCode==0,"MP3 fixture: "+await errors);}
        var importedMp3=await RecordedVoice.ImportAsync(mp3);Check(importedMp3.Duration>1.45 && importedMp3.Duration<1.6 && importedMp3.Speaking(.2),"MP3 voice import");
        var saved=JsonSerializer.Deserialize<DialogueProject>(JsonSerializer.Serialize(project,AssetStore.Json),AssetStore.Json)!;
        Check(saved.Recording!.Pcm.SequenceEqual(bytes),"Voice is embedded and portable in saved JSON");
        var montage=new DialogueSequence{Clips=new(){new(){Name="Voz propia",Project=project.Copy()},new(){Name="Voz y espejo",Project=project.Copy()}}};
        montage.Clips[1].Project.Mirror=true;montage.Clips[1].Project.AvatarX=.25;
        var joined=montage.Compile(assets.Library);string before=Path.Combine(folder,"voz-montaje.wav");DialogueAudio.Write(before,joined.Project,joined.Timeline,assets.Library,joined.Timeline.Duration);
        montage.Split(montage.Clips[0].Id,.52,assets.Library);var split=montage.Compile(assets.Library);
        string after=Path.Combine(folder,"voz-montaje-dividido.wav");DialogueAudio.Write(after,split.Project,split.Timeline,assets.Library,split.Timeline.Duration);
        Check(File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(after)),"Split preserves exact recorded PCM audio");
        foreach(double at in new[]{.2,.51,.52,.65,1.4,1.7,2.15})
        {
            Check(joined.Timeline.At(at,joined.Project).Speaking==split.Timeline.At(at,split.Project).Speaking,"Voice mouth continuity after split");
            Check(DialogueRenderer.StraightBgra(new DialogueRenderer(assets).Render(joined.Project,joined.Timeline,at)).SequenceEqual(DialogueRenderer.StraightBgra(new DialogueRenderer(assets).Render(split.Project,split.Timeline,at))),"Voice split pixel continuity");
        }
        var trimmed=new DialogueSequence{Clips=new(){new(){Project=project.Copy(),In=.15,Out=.9}}};var trim=trimmed.Compile(assets.Library);
        Check(trim.Timeline.Recordings[0].SourceOffset==.15 && trim.Timeline.At(.5,trim.Project).Speaking==false,"Voice trim source offset and silence");
        var bad=voice with {Pcm=new byte[3]};bool rejected=false;try{bad.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Reject malformed PCM");
        var window=new DialogueSequenceWindow(assets,new DialogueProject{Text="Uno dos tres cuatro."},testing:true);
        window.VerifyConversationTools();window.ShowConversationFixture(voice);
        var root=(FrameworkElement)window.Content;root.Measure(new Size(1280,900));root.Arrange(new Rect(0,0,1280,900));root.UpdateLayout();Save(root,Path.Combine(folder,"conversacion.png"));
        root.Measure(new Size(1000,700));root.Arrange(new Rect(0,0,1000,700));root.UpdateLayout();Save(root,Path.Combine(folder,"conversacion-compacta.png"));window.Close();
        SaveBitmap(new DialogueRenderer(assets).Render(project,line,.2),Path.Combine(folder,"voz-hablando.png"));
        SaveBitmap(new DialogueRenderer(assets).Render(project,line,.6),Path.Combine(folder,"voz-silencio.png"));
        await DialogueExport.ExportAsync(project,line,assets,Path.Combine(folder,"ejemplo-voz.mov"),Application.Current.Dispatcher,null,CancellationToken.None);
        await DialogueExport.ExportAsync(project,line,assets,Path.Combine(folder,"ejemplo-voz.webm"),Application.Current.Dispatcher,null,CancellationToken.None);
        File.WriteAllText(Path.Combine(folder,"verificacion-conversacion.json"),JsonSerializer.Serialize(new{actorMemory=true,visualCards=true,resizeReadingTime=true,wordAnimations=true,selectedClipPlaybackAndLoop=true,embeddedVoice=true,wavAndMp3Import=true,silenceLipSync=true,voiceSplitExactPcm=true,voiceTrim=true,templates=true,voiceMovAndWebm=true},AssetStore.Json));
    }
    static void SaveBitmap(BitmapSource image,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(path);encoder.Save(stream);}
    static void Save(FrameworkElement visual,string path)
    {var bitmap=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);}
}

public sealed partial class DialogueSequenceWindow
{
    static void AssertConversation(bool value,string name){if(!value)throw new InvalidDataException(name);}
    public void VerifyConversationTools()
    {
        quickTemplate!.Ranges.Clear();quickDirty=true;CommitQuick();
        quickPose.SelectedItem=assets.Library.Poses.First(p=>p.Id=="pl0007");quickSpeed.Value=44;quickHold.Value=1.2;CommitQuick();SetPlacement(-.2,.03,.85);
        quickCharacter.SelectedItem="Daryan Crescend";CommitQuick();SetPlacement(.25,0,1.1);
        quickCharacter.SelectedItem="Apollo Justice";CommitQuick();
        AssertConversation(Selected!.Project.DefaultPose=="pl0007" && avatarX.Value==-.2 && avatarSize.Value==.85 && quickSpeed.Value==44,"Remember actor pose, position, size and speed");
        var memory=JsonSerializer.Deserialize<DialogueSequence>(JsonSerializer.Serialize(sequence,AssetStore.Json),AssetStore.Json)!;
        AssertConversation(memory.Actors["Apollo Justice"].X==-.2,"Actor memory survives project serialization");
        var shot=Selected;double duration=shot.Duration(assets.Library);var timing=DialogueCompiler.Compile(shot.Project,assets.Library).Pages.SelectMany(p=>p.Glyphs).Select(g=>g.At).ToArray();
        ResizeClip(shot.Id,duration+1.25);shot=Selected!;
        AssertConversation(Math.Abs(shot.Duration(assets.Library)-duration-1.25)<1e-8,"Reading handle extends duration");
        AssertConversation(timing.SequenceEqual(DialogueCompiler.Compile(shot.Project,assets.Library).Pages.SelectMany(p=>p.Glyphs).Select(g=>g.At)),"Reading handle keeps writing times");
        History(false);AssertConversation(Math.Abs(Selected!.Duration(assets.Library)-duration)<1e-8,"Undo duration handle");History(true);
        var card=(Card)track.SelectedItem;AssertConversation(card.Thumbnail.PixelWidth>0 && card.ReadingWidth>0 && card.Title.Contains("Apollo"),"Visual card portrait, identity and reading band");
        var root=(FrameworkElement)Content;root.Measure(new Size(1280,900));root.Arrange(new Rect(0,0,1280,900));root.UpdateLayout();
        var container=(DependencyObject)track.ItemContainerGenerator.ContainerFromItem(track.SelectedItem);var handle=FindResizeThumb(container)??throw new InvalidDataException("Missing duration drag handle");
        double beforeDrag=Selected!.Duration(assets.Library);int historyBefore=undo.Count;
        handle.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});
        handle.RaiseEvent(new DragDeltaEventArgs(zoom.Value*.6,0){RoutedEvent=Thumb.DragDeltaEvent});
        handle.RaiseEvent(new DragCompletedEventArgs(zoom.Value*.6,0,false){RoutedEvent=Thumb.DragCompletedEvent});
        AssertConversation(Math.Abs(Selected!.Duration(assets.Library)-beforeDrag-.6)<1e-8 && undo.Count==historyBefore+1,"Real thumb events adjust duration in one undo step");
        wordMode.IsChecked=true;quickText.Select(4,3);quickPose.SelectedItem=assets.Library.Poses.First(p=>p.Id=="pl0005");AssignQuickWords(false);
        AssertConversation(Selected!.Project.DefaultPose=="pl0007" && Selected.Project.Ranges.Any(r=>r.Start==4 && r.End==7 && r.PoseId=="pl0005"),"Word assignment keeps base pose");
        var text=Selected.Project.Text;quickText.Text="¡"+text;CommitQuick();AssertConversation(Selected!.Project.Ranges.Any(r=>r.Start==5 && r.End==8),"Word animation follows text edits");
        wordMode.IsChecked=false;ApplyConversationTemplate(0,new[]{"Apollo Justice","Daryan Crescend","Trucy Wright"});
        var left=sequence.Clips[^2];var right=sequence.Clips[^1];AssertConversation(left.Project.AvatarX==-.25 && right.Project.AvatarX==.25 && right.Project.Mirror,"Two actor conversation template");
        ApplyConversationTemplate(1,new[]{"Apollo Justice","Daryan Crescend","Trucy Wright"});AssertConversation(sequence.Clips[^1].Project.AvatarX==0,"Narrator template");
        ApplyConversationTemplate(2,new[]{"Apollo Justice","Daryan Crescend","Trucy Wright"});AssertConversation(sequence.Clips[^1].Project.AvatarX==0 && sequence.Clips[^2].Project.AvatarX==.25 && sequence.Clips[^3].Project.AvatarX==-.25,"Three actor template");
        var bounds=SelectedBounds();playingSelection=true;playbackFrom=bounds.Start;playbackTo=bounds.End;repeatPlayback.IsChecked=true;playing=true;startedAt=playbackTo+1;watch.Reset();PlaybackTick();
        AssertConversation(Math.Abs(position-playbackFrom)<.01 && playing,"Selected clip loop returns to its own beginning");
        repeatPlayback.IsChecked=false;startedAt=playbackTo+1;watch.Reset();PlaybackTick();AssertConversation(!playing && position<playbackTo && position>playbackFrom,"Selected clip stops without flashing next actor");
        NewClip();AssertConversation(quickTemplate!.Recording==null && quickTemplate.FinalHold==0,"Next clip starts without previous recording or extra tail");
    }
    static Thumb? FindResizeThumb(DependencyObject node)
    {if(node is Thumb thumb)return thumb;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var found=FindResizeThumb(VisualTreeHelper.GetChild(node,i));if(found!=null)return found;}return null;}
    public void ShowConversationFixture(RecordedVoice voice)
    {
        sequence=new();ApplyConversationTemplate(2,new[]{"Apollo Justice","Daryan Crescend","Trucy Wright"});
        var p=sequence.Clips[0].Project;p.Text="¡Un momento! Vamos a revisar esa prueba.";p.Recording=voice;p.FitTextToVoice=true;p.FinalHold=.8;
        p.Ranges.Add(new(){Start=0,End=12,PoseId="pl0007",Mode="Gesto",Color="Naranja"});
        sequence.Clips[1].Project.Text="Escucha mi versión de los hechos.";sequence.Clips[2].Project.Text="¡Ahora todo tiene sentido!";
        compiled=null;Refresh(sequence.Clips[0].Id);position=.8;Render();
    }
}
