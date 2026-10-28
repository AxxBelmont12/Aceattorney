using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AvatarStudio;
public static class StudioVerification
{
    static void Check(bool value,string name){if(!value)throw new InvalidDataException(name);}
    public static async Task RunAsync(AssetStore assets,string folder)
    {
        var p=new DialogueProject{Text="Escena de prueba.",CharactersPerSecond=45,PageHold=.3};
        var image=new ImageAsset{Name="Fondo portable.png",Png=Png(SceneAssets.Image("court-lobby"))};image.Validate();
        p.Scene=new(){Background="Imagen",Image=image,Layers=new(){new(){Kind="Sello",Text="PRUEBA",X=.8,Y=.2,Width=.2},new(){Kind="Marco",X=.8,Y=.2,Width=.23,Front=false}}};
        var line=DialogueCompiler.Compile(p,assets.Library);var renderer=new DialogueRenderer(assets);var frame=renderer.Render(p,line,.5);Save(frame,Path.Combine(folder,"estudio-elementos.png"));
        Check(Pixel(frame,0,0)[3]==255 && Pixel(renderer.Render(p,line,.5,avatarOnly:true),0,0)[3]==0,"Opaque custom background and isolated avatar hit mask");
        var portable=JsonSerializer.Deserialize<DialogueProject>(JsonSerializer.Serialize(p,AssetStore.Json),AssetStore.Json)!;portable.Validate(assets.Library);
        Check(portable.Scene.Image!.Png.SequenceEqual(image.Png) && portable.Scene.Layers.Count==2,"Embedded background and elements JSON portability");
        p.Scene=p.Scene with {Background="Transparente",Entry="Aparición suave",Exit="Desde la derecha",TransitionSeconds=.3};line=DialogueCompiler.Compile(p,assets.Library);
        Check(DialogueRenderer.StraightBgra(renderer.Render(p,line,0,avatarOnly:true)).Where((v,i)=>i%4==3).All(a=>a==0),"Entry fade starts with an invisible avatar");
        var sequence=new DialogueSequence{Name="Prueba de estudio",Clips=new(){new(){Name="Saludo",SceneName="Entrada",Project=p.Copy()},new(){Name="Respuesta",SceneName="Juicio",Project=p.Copy()},new(){Name="Despedida",SceneName="Entrada",Project=p.Copy()}}};
        var joined=sequence.Compile(assets.Library);var split=DialogueSequence.Clone(sequence);split.Split(split.Clips[0].Id,.44,assets.Library);var divided=split.Compile(assets.Library);
        foreach(double at in new[]{.1,.3,.44,.6,line.Duration-.1})Check(DialogueRenderer.StraightBgra(renderer.Render(joined.Project,joined.Timeline,at)).SequenceEqual(DialogueRenderer.StraightBgra(renderer.Render(divided.Project,divided.Timeline,at))),"Transitions and layers do not restart at a split");
        string lib=Path.Combine(folder,"fragment-library-test");ProjectStorage.SaveFragment(new("Saludo reusable",sequence.Clips[0]),lib);var saved=ProjectStorage.Fragments(assets.Library,lib);
        Check(saved.Any(f=>f.Fragment.Name=="Saludo reusable" && f.Fragment.Shot.Project.Scene.Layers.Count==2),"Reusable fragment stores all presentation data");
        string history=Path.Combine(folder,"history-test"),last="";for(int i=0;i<7;i++){sequence.Name="Versión "+i;last=ProjectStorage.WriteVersion(sequence,JsonSerializer.Serialize(sequence,AssetStore.Json),history);}
        Check(Directory.GetFiles(Path.Combine(history,sequence.Id),"*.gz").Length==5,"History retention bound");Check(ProjectStorage.ReadVersion(last,assets.Library).Name=="Versión 6","Recover compressed project version");
        var jobs=VideoExportPlan.Create(sequence,true,true,true);Check(jobs.Count==6 && jobs.Last().Sequence.Clips.Count==1 && jobs[^2].Sequence.Clips.Count==2,"Whole, clip and grouped scene export planning");
        Check(VideoExportPlan.SafeName("Escena:/\\prueba").IndexOfAny(Path.GetInvalidFileNameChars())<0,"Batch output names are portable Windows names");
        var voice=new RecordedVoice{Pcm=new byte[48000*2],TrimStart=.1,TrimEnd=.8,Offset=.2};voice.Validate();Check(Math.Abs(voice.Duration-.9)<1e-8 && voice.SampleAt(.1)==0,"Voice trim and offset duration");
        var tone=new byte[48000*2];for(int i=0;i<tone.Length/2;i++)BitConverter.TryWriteBytes(tone.AsSpan(i*2,2),(short)10000);
        var moved=voice with {Pcm=tone};Check(moved.SampleAt(.1)==0 && moved.SampleAt(.3)>.2 && moved.SampleAt(.91)==0,"Audio sample mapping respects trim and offset");
        var word=new DialogueSequenceWindow(assets,new DialogueProject{Text="Uno dos tres cuatro."},testing:true);word.VerifyStudioCommands();word.MakeCourtFixture();
        var root=(FrameworkElement)word.Content;root.Measure(new Size(1280,900));root.Arrange(new Rect(0,0,1280,900));root.UpdateLayout();SaveVisual(root,Path.Combine(folder,"modo-juicio-editor.png"));word.SaveCourtFrames(folder);
        word.VerifyJudgePlacement();word.MakeColorFixture();await word.TestStudioBatch(folder);word.Close();
        var editor=new SceneEditorWindow(assets,portable);var sceneRoot=(FrameworkElement)editor.Content;sceneRoot.Measure(new Size(1140,760));sceneRoot.Arrange(new Rect(0,0,1140,760));sceneRoot.UpdateLayout();SaveVisual(sceneRoot,Path.Combine(folder,"editor-fondos.png"));editor.Close();
        File.WriteAllText(Path.Combine(folder,"verificacion-estudio.json"),JsonSerializer.Serialize(new{reusableFragments=true,waveformTrimAndOffset=true,wordTimingAndEmphasis=true,transitionsPreservedAtCuts=true,backgroundsAndLayers=true,projectScenesAndRecovery=true,batchVideoExports=true,courtCastingAndCameras=true,embeddedMediaPortable=true},AssetStore.Json));
    }
    static byte[] Png(BitmapSource image){var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(image));using var s=new MemoryStream();e.Save(s);return s.ToArray();}
    static byte[] Pixel(BitmapSource image,int x,int y){var b=new byte[4];image.CopyPixels(new Int32Rect(x,y,1,1),b,4,0);return b;}
    internal static void Save(BitmapSource image,string path)=>File.WriteAllBytes(path,Png(image));
    static void SaveVisual(FrameworkElement root,string path){var b=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);b.Render(root);Save(b,path);}
}
public sealed partial class DialogueSequenceWindow
{
    public void VerifyStudioCommands()
    {
        quickText.Select(4,3);ApplyWordStyle(pause:.5);var range=Selected!.Project.Ranges.Single();if(range.Start!=4 || range.End!=7 || range.PauseAfter!=.5)throw new InvalidDataException("Word context pause");
        quickText.Select(0,11);ApplyWordStyle(rate:15,color:"Naranja");if(Selected!.Project.Ranges.Any(r=>r.Start<11 && (r.CharactersPerSecond!=15 || r.Color!="Naranja")))throw new InvalidDataException("Context speed and emphasis preserve segment boundaries");
        var recording=new RecordedVoice{Pcm=new byte[48000*2]};quickTemplate!.Recording=recording;quickTemplate.FitTextToVoice=false;RefreshVoice();QueueQuick();CommitQuick();
        voiceIn.Text="0,1";voiceOut.Text="0,8";voiceOffset.Text="0,2";ApplyVoiceTrim();if(Math.Abs(Selected!.Project.Recording!.Duration-.9)>1e-8)throw new InvalidDataException("Waveform trim inspector");
        var stored=new StoredFragment("Reutilizado",DialogueSequence.Clone(Selected));int count=sequence.Clips.Count;InsertStoredFragment(stored);if(sequence.Clips.Count!=count+1 || Selected!.Project.Recording==null)throw new InvalidDataException("Insert reusable clip with audio");
        History(false);if(sequence.Clips.Count!=count)throw new InvalidDataException("Undo reusable clip insertion");
    }
    public void MakeCourtFixture()
    {
        sequence=new();foreach(var role in new[]{"Defensa","Fiscalía","Acusado / testigo","Juez"})AddCourtShot(role);
        foreach(var c in sequence.Clips)c.Project.Text="¡Un momento! Revisemos esa prueba.";compiled=null;Refresh(sequence.Clips[0].Id);position=.6;Render();
    }
    public void SaveCourtFrames(string folder)
    {
        for(int i=0;i<sequence.Clips.Count;i++){var p=sequence.Clips[i].Project;var t=DialogueCompiler.Compile(p,assets.Library);StudioVerification.Save(new DialogueRenderer(assets).Render(p,t,.6),Path.Combine(folder,$"juicio-{i}.png"));}
    }
    public void VerifyJudgePlacement()
    {
        var p=sequence.Clips.First(c=>c.Name=="Juez").Project.Copy();p.Scene=p.Scene with {ShowDialogue=false};var t=DialogueCompiler.Compile(p,assets.Library);var r=new DialogueRenderer(assets);var actor=r.Render(p,t,.6,avatarOnly:true);var desk=SceneAssets.Image("desk-judge");var a=new byte[actor.PixelWidth*actor.PixelHeight*4];actor.CopyPixels(a,actor.PixelWidth*4,0);var d=new byte[desk.PixelWidth*desk.PixelHeight*4];desk.CopyPixels(d,desk.PixelWidth*4,0);
        int x=640;int body=Enumerable.Range(0,720).Where(y=>a[(y*1280+x)*4+3]>200).Max();int top=Enumerable.Range(0,720).First(y=>d[(y*1280+x)*4+3]>200);
        if(body<top+3)throw new InvalidDataException("Judge bust ends above the foreground desk");
        var rendered=r.Render(p,t,.6);var pixel=new byte[4];rendered.CopyPixels(new Int32Rect(640,600,1,1),pixel,4,0);if(!pixel.SequenceEqual(d.AsSpan((600*1280+640)*4,4).ToArray()))throw new InvalidDataException("Desk does not correctly occlude the judge");
    }
    public void MakeColorFixture()
    {
        sequence=new(){Name="Lotes de prueba"};for(int i=0;i<2;i++)sequence.Clips.Add(new(){Name="Plano "+(i+1),SceneName="Escena "+(i+1),Project=new(){Text="Corte.",CharactersPerSecond=80,PageHold=.2,Scene=new(){Background="Color",Color=i==0?"#183052":"#4A253B"}}});compiled=null;Refresh(sequence.Clips[0].Id);
    }
    public async Task TestStudioBatch(string folder)
    {
        string output=Path.Combine(folder,"batch-test");await ExportJobs(VideoExportPlan.Create(sequence,true,true,true),output,".mp4");
        var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"exportacion.json")));if(manifest.RootElement.GetProperty("state").GetString()!="completo" || manifest.RootElement.GetProperty("completed").GetArrayLength()!=5)throw new InvalidDataException("Batch completed outputs manifest");
    }
}
