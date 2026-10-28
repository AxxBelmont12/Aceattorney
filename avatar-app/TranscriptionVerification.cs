using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AvatarStudio;
public static class TranscriptionVerification
{
    static void Check(bool value,string name){if(!value)throw new InvalidDataException(name);}
    public static async Task RunAsync(AssetStore assets)
    {
        string fixtures=Environment.GetEnvironmentVariable("TRANSCRIPTION_FIXTURES")??"avatar-work/transcription",folder=Environment.GetEnvironmentVariable("TRANSCRIPTION_QA_DIR")??"avatar-work/transcription-qa";Directory.CreateDirectory(folder);
        await LocalTranscription.InstallModel(Path.Combine(fixtures,"ggml-base.bin"));Check(await LocalTranscription.ModelReady(),"Verified multilingual model cache");
        var voice=await RecordedVoice.ImportAsync(Path.Combine(fixtures,"es-prueba.wav"));var spanish=await LocalTranscription.Transcribe(voice,"es","",null,CancellationToken.None);spanish.Validate();
        Check(spanish.Text.Contains("prueba",StringComparison.OrdinalIgnoreCase) && spanish.Text.Contains("fiscal",StringComparison.OrdinalIgnoreCase),"Real local Spanish transcription");
        var englishVoice=await RecordedVoice.ImportAsync(Path.Combine(fixtures,"jfk.wav"));var english=await LocalTranscription.Transcribe(englishVoice,"en","",null,CancellationToken.None);
        Check(english.Text.Contains("country",StringComparison.OrdinalIgnoreCase),"Real local English transcription");
        File.WriteAllText(Path.Combine(folder,"transcripcion-es.json"),JsonSerializer.Serialize(spanish,AssetStore.Json));
        var style=new DialogueProject{Text="Anterior",DefaultPose="pl0000"};var project=spanish.Single(style,voice);project.Validate(assets.Library);var timeline=DialogueCompiler.Compile(project,assets.Library);
        Check(project.SyncSpeechWords && project.SpeechWords.Count>10 && Math.Abs(timeline.Duration-voice.Duration)<.001,"Single-dialogue transcription has word timing and exact audio duration");
        foreach(var w in project.SpeechWords)Check(Math.Abs(timeline.TimeFor(w.StartChar)-w.Start)<1e-6,"Typed word follows recognized start time");
        var names=project.SpeechWords.Select(w=>w.Start).ToArray();DialogueEdits.AdjustRanges(project,project.Text,project.Text.Replace("Apolo","Apollo",StringComparison.Ordinal));project.Validate(assets.Library);
        Check(project.SpeechWords.Select(w=>w.Start).SequenceEqual(names),"Correcting names preserves timings");
        string original=project.Text;DialogueEdits.AdjustRanges(project,project.Text,"Bueno, "+project.Text);project.Validate(assets.Library);Check(project.SpeechWords.Count>names.Length && project.SpeechWords[0].Start==names[0],"Changing word count redistributes within original time range");
        project=spanish.Single(style,voice);var pieces=spanish.Fragments(style,voice);Check(pieces.Count==spanish.Parts.Count && pieces.Count>1,"Automatic fragments follow recognized phrases");
        var sequence=new DialogueSequence{Clips=pieces.Select((p,i)=>new DialogueShot{Project=p,Name="Voz "+i}).ToList()};var joined=sequence.Compile(assets.Library);
        Check(Math.Abs(joined.Timeline.Duration-voice.Duration)<.001,"Automatic fragments preserve complete recording duration");
        string full=Path.Combine(folder,"completo.wav"),split=Path.Combine(folder,"fragmentos.wav");DialogueAudio.Write(full,project,DialogueCompiler.Compile(project,assets.Library),assets.Library,voice.Duration);DialogueAudio.Write(split,joined.Project,joined.Timeline,assets.Library,voice.Duration);
        Check(File.ReadAllBytes(full).SequenceEqual(File.ReadAllBytes(split)),"Automatic fragment audio is sample-identical to original");
        var divided=new DialogueSequence{Clips=new(){new(){Project=project.Copy()}}};var before=divided.Compile(assets.Library);divided.Split(divided.Clips[0].Id,1.25,assets.Library);var after=divided.Compile(assets.Library);
        foreach(double at in new[]{1.2,1.25,1.3,2.0})Check(DialogueRenderer.StraightBgra(new DialogueRenderer(assets).Render(before.Project,before.Timeline,at)).SequenceEqual(DialogueRenderer.StraightBgra(new DialogueRenderer(assets).Render(after.Project,after.Timeline,at))),"Splitting synchronized text preserves pixels");
        var saved=JsonSerializer.Deserialize<DialogueProject>(JsonSerializer.Serialize(project,AssetStore.Json),AssetStore.Json)!;Check(saved.SpeechWords.SequenceEqual(project.SpeechWords) && saved.Recording!.Pcm.SequenceEqual(voice.Pcm),"Saved projects retain words and voice");
        var clipped=TranscriptDocument.Slice(voice,1,2);voice.Speaking(.5);Check(clipped.Pcm.Length==48000*2 && Math.Abs(clipped.SampleAt(.2)-voice.SampleAt(1.2))<.00001,"Voice slices map to source samples");
        string recording=Path.Combine(folder,"grabacion.wav");VoiceFile.SaveWav(clipped,recording);var roundtrip=await RecordedVoice.ImportAsync(recording);Check(roundtrip.Pcm.SequenceEqual(clipped.Pcm),"Saved microphone WAV preserves PCM samples");
        using(var c=new CancellationTokenSource()){c.Cancel();var prior=File.ReadAllBytes(recording);bool cancelled=false;try{VoiceFile.SaveWav(voice,recording,c.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled && prior.SequenceEqual(File.ReadAllBytes(recording)) && !Directory.EnumerateFiles(folder,"grabacion.wav.*.part").Any(),"Cancelled WAV save preserves existing file and cleans temporary output");}
        using(var c=new CancellationTokenSource()){c.Cancel();bool cancelled=false;try{await LocalTranscription.Transcribe(voice,"es","",null,c.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Transcription cancellation");}
        using(var c=new CancellationTokenSource()){c.Cancel();bool cancelled=false;try{await MicrophoneCapture.Record(uint.MaxValue,null,c.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Cancel recording before any microphone capture");}
        var missingAudio=new TranscriptionWindow(null,false,false,"");await missingAudio.VerifyModelFeedback();missingAudio.Close();
        var window=new TranscriptionWindow(voice,true,false,"Apollo Justice, Klavier Gavin");await window.VerifyModelFeedback();window.TestReview(spanish);var root=(FrameworkElement)window.Content;root.Measure(new Size(1080,840));root.Arrange(new Rect(0,0,1080,840));root.UpdateLayout();window.VerifyReviewLayout();SaveVisual(root,Path.Combine(folder,"audio-a-texto.png"));root.Measure(new Size(910,620));root.Arrange(new Rect(0,0,910,620));root.UpdateLayout();window.VerifyReviewLayout();SaveVisual(root,Path.Combine(folder,"audio-a-texto-compacto.png"));window.Close();
        var editor=new DialogueSequenceWindow(assets,style,testing:true);editor.VerifyTranscriptApply(new(voice,spanish,true,false),style);editor.VerifyTranscriptApply(new(voice,spanish,false,true),style);editor.Close();
        await DialogueExport.ExportAsync(project,DialogueCompiler.Compile(project,assets.Library),assets,Path.Combine(folder,"transcripcion-video.mp4"),Application.Current.Dispatcher,null,CancellationToken.None);
        File.WriteAllText(Path.Combine(folder,"verification.json"),JsonSerializer.Serialize(new{version=typeof(MainWindow).Assembly.GetName().Version!.ToString(3),spanish=true,english=true,verifiedModel=true,wordTiming=true,nameEditsPreserveTiming=true,automaticFragments=true,fragmentAudioIdentical=true,saveLoad=true,undoApply=true,cancellation=true,microphoneDevicesEnumerated=MicrophoneCapture.Devices().Count,microphonePhysicalCapture="Pendiente de prueba por el usuario; las pruebas no capturan su micrófono.",spanishText=spanish.Text,englishText=english.Text},AssetStore.Json));
    }
    static void SaveVisual(FrameworkElement root,string path){var b=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawRectangle(new VisualBrush(root){Stretch=Stretch.Fill},null,new Rect(0,0,root.ActualWidth,root.ActualHeight));b.Render(visual);var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(b));using var s=File.Create(path);e.Save(s);}
}
public sealed partial class DialogueSequenceWindow
{
    public void VerifyTranscriptApply(TranscriptionChoice choice,DialogueProject style)
    {
        int previous=sequence.Clips.Count;ApplyTranscript(choice,style);int expected=previous+(choice.Replace?-1:0)+(choice.Fragments?choice.Document.Parts.Count:1);if(sequence.Clips.Count!=expected || !Selected!.Project.SyncSpeechWords)throw new InvalidDataException("Transcript editor application");History(false);if(sequence.Clips.Count!=previous)throw new InvalidDataException("Undo complete transcription application");
    }
}
