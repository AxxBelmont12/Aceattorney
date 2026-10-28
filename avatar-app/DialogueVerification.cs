using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AvatarStudio;
public static class DialogueVerification
{
    static void Check(bool condition,string description){if(!condition)throw new InvalidDataException(description);}
    public static async Task RunAsync(AssetStore assets)
    {
        string folder=Environment.GetEnvironmentVariable("DIALOGUE_QA_DIR")??"avatar-work/dialogue-qa";Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"font-info.txt"),string.Join("\n",Fonts.GetFontFamilies(new Uri("pack://application:,,,/assets/dialogue/")).SelectMany(f=>f.GetTypefaces().Select(t=>f.Source+" | "+string.Join(",",f.FamilyNames.Values)+" | "+t.Weight+" | glyph="+t.TryGetGlyphTypeface(out _)))));
        Check(DialogueResources.Typeface.TryGetGlyphTypeface(out var glyphs),"Private game font loading");
        Check(glyphs.FamilyNames.Values.Any(n=>n.Contains("Syntax")),"Original game font selected instead of fallback");
        Check(glyphs.CharacterToGlyphMap.ContainsKey('ñ') && glyphs.CharacterToGlyphMap.ContainsKey('¿'),"Spanish game-font coverage");
        var p=new DialogueProject{Text="¡Mira! Cambio de pose.",DisplayName="Axx Belmont",CharactersPerSecond=20,PageHold=.6,Voice="Original 1"};
        p.Ranges.Add(new(){Start=0,End=6,PoseId="pl0007",Mode="Gesto",Color="Naranja",DisplayName="Axx Belmont"});
        var timeline=DialogueCompiler.Compile(p,assets.Library);
        Check(timeline.At(.16,p).Binding.PoseId=="pl0007","Range begins with selected phrase");
        Check(timeline.At(timeline.TimeFor(8)+.01,p).Binding.PoseId=="pl0000","Range ends and base pose returns");
        var last=timeline.At(timeline.Duration-.1,p);Check(last.Visible==last.Page.Text.Length && !last.Speaking,"Final complete text and idle state");
        var longProject=new DialogueProject{Text=string.Join(" ",Enumerable.Repeat("¿Qué ocurre? Aquí hay tildes, ñ y una explicación suficientemente larga para varias páginas.",12))};
        var longTimeline=DialogueCompiler.Compile(longProject,assets.Library);Check(longTimeline.Pages.Count>3,"Automatic pagination");
        Check(longTimeline.Pages.All(page=>DialogueCompiler.Format(page.Text).Height<=DialogueCompiler.MaxHeight+.2),"No page exceeds three lines");
        Check(string.Concat(longTimeline.Pages.SelectMany(pg=>pg.Glyphs).Select(g=>longProject.Text[g.OriginalStart..g.OriginalEnd]))==longProject.Text,"No text lost during pagination");
        var edit=new DialogueProject{Text="Hola mundo 👩‍⚖️."};edit.Ranges.Add(new(){Start=5,End=10,PoseId="pl0007"});
        DialogueEdits.AdjustRanges(edit,edit.Text,"¡Hola mundo 👩‍⚖️.");Check(edit.Ranges[0].Start==6 && edit.Text[edit.Ranges[0].Start..edit.Ranges[0].End]=="mundo","Edit before range preserves words");
        DialogueEdits.AdjustRanges(edit,edit.Text,"¡Hola mundito 👩‍⚖️.");Check(edit.Text[edit.Ranges[0].Start..edit.Ranges[0].End]=="mundito","Edit inside range adjusts it");
        int emoji=edit.Text.IndexOf("👩",StringComparison.Ordinal);var snapped=DialogueEdits.Snap(edit.Text,emoji+1,1);Check(snapped.Start==emoji && snapped.End>emoji+2,"Emoji grapheme selection");
        edit.Validate(assets.Library);
        var windows=new DialogueProject{Text="Uno\nDos"};windows.Ranges.Add(new(){Start=4,End=7,PoseId="pl0007"});DialogueEdits.NormalizeNewlines(windows);
        Check(windows.Text=="Uno\r\nDos" && windows.Text[windows.Ranges[0].Start..windows.Ranges[0].End]=="Dos","Windows newline selection offsets");windows.Validate(assets.Library);
        var loaded=JsonSerializer.Deserialize<DialogueProject>(JsonSerializer.Serialize(p,AssetStore.Json),AssetStore.Json)!;loaded.Validate(assets.Library);
        Check(DialogueCompiler.Compile(loaded,assets.Library).Duration==timeline.Duration,"Saved-project timing round trip");
        var timed=new DialogueProject{Text="Uno dos"};timed.Ranges.Add(new(){Start=0,End=3,PoseId="pl0000",CharactersPerSecond=10,PauseAfter=1.5});
        var timedLine=DialogueCompiler.Compile(timed,assets.Library);
        Check(Math.Abs(timedLine.TimeFor(4)-(.15+3.0/10+1.5+1.0/30))<.00001,"Fragment speed and pause affect subsequent words");
        Check(!timedLine.At(.8,timed).Speaking,"Mouth rests during fragment pause");
        var timedSaved=JsonSerializer.Deserialize<DialogueProject>(JsonSerializer.Serialize(timed,AssetStore.Json),AssetStore.Json)!;
        Check(DialogueCompiler.Compile(timedSaved,assets.Library).Duration==timedLine.Duration,"Per-fragment timing survives save / load");
        var sequence=new DialogueSequence();
        var first=DialogueSequence.Clone(p);first.Voice="Original 1";first.Volume=.3;
        var second=DialogueSequence.Clone(p);second.DefaultPose="pl21100";second.Ranges.Clear();second.Text="Segundo clip.";second.Voice="Original 2";second.Volume=.7;
        sequence.Clips.Add(new(){Name="Apollo",Project=first});sequence.Clips.Add(new(){Name="Daryan",Project=second});
        var montage=sequence.Compile(assets.Library);double boundary=DialogueCompiler.Compile(first,assets.Library).Duration;
        Check(montage.Timeline.At(boundary+.16,montage.Project).Binding.PoseId=="pl21100","Sequence cut selects next actor");
        Check(Math.Abs(montage.Timeline.Duration-boundary-DialogueCompiler.Compile(second,assets.Library).Duration)<.000001,"Sequence duration equals sum of clips");
        var montageAudio=DialogueAudio.Events(montage.Project,montage.Timeline,assets.Library);
        Check(montageAudio.Any(e=>e.At<boundary && e.Voice==0 && e.Volume==.3) && montageAudio.Any(e=>e.At>boundary && e.Voice==1 && e.Volume==.7),"Each clip preserves original voice and volume");
        var mirroredSequence=DialogueSequence.Clone(sequence);mirroredSequence.Clips[1].Project.Mirror=true;
        var mirroredMontage=mirroredSequence.Compile(assets.Library);var mirroredSource=mirroredSequence.Clips[1].Project;
        Check(DialogueRenderer.StraightBgra(new DialogueRenderer(assets).Render(mirroredMontage.Project,mirroredMontage.Timeline,boundary+.5)).SequenceEqual(DialogueRenderer.StraightBgra(new DialogueRenderer(assets).Render(mirroredSource,DialogueCompiler.Compile(mirroredSource,assets.Library),.5))),"Each clip preserves mirroring in actual rendered pixels");
        // A split is an edit point, not a new animation or text sequence.
        var divided=DialogueSequence.Clone(sequence);double cut=.77;
        divided.Split(divided.Clips[0].Id,cut,assets.Library);
        var splitLine=divided.Compile(assets.Library);
        Check(divided.Clips.Count==3 && Math.Abs(splitLine.Timeline.Duration-montage.Timeline.Duration)<1e-8,"Split preserves montage duration");
        var splitAudio=DialogueAudio.Events(splitLine.Project,splitLine.Timeline,assets.Library);
        Check(splitAudio.Count==montageAudio.Count && splitAudio.Zip(montageAudio).All(pair=>Math.Abs(pair.First.At-pair.Second.At)<1e-8 && pair.First.Voice==pair.Second.Voice && pair.First.Volume==pair.Second.Volume),"Split preserves every audio event");
        foreach(double time in new[]{cut-.001,cut,cut+.001,cut+.15,boundary-.01})
        {
            var before=montage.Timeline.At(time,montage.Project);var after=splitLine.Timeline.At(time,splitLine.Project);
            Check(before.Visible==after.Visible && before.Speaking==after.Speaking && before.Binding.PoseId==after.Binding.PoseId && Math.Abs(before.AnimationSeconds-after.AnimationSeconds)<1e-8,"Split has no text or animation jump");
            Check(DialogueRenderer.StraightBgra(new DialogueRenderer(assets).Render(montage.Project,montage.Timeline,time)).SequenceEqual(DialogueRenderer.StraightBgra(new DialogueRenderer(assets).Render(splitLine.Project,splitLine.Timeline,time))),"Split produces identical pixels at the cut");
        }
        var nested=DialogueSequence.Clone(sequence);nested.Clips[0].In=.2;nested.Clips[0].Out=1.3;
        var nestedBefore=nested.Compile(assets.Library);nested.Split(nested.Clips[0].Id,.4,assets.Library);
        Check(Math.Abs(nested.Compile(assets.Library).Timeline.Duration-nestedBefore.Timeline.Duration)<1e-8 && nested.Clips[1].In==.2+.4 && nested.Clips[1].Out==1.3,"Split inside an already trimmed clip");
        int safeCount=divided.Clips.Count;bool rejected=false;try{divided.Split(divided.Clips[0].Id,.01,assets.Library);}catch(InvalidDataException){rejected=true;}
        Check(rejected && divided.Clips.Count==safeCount,"Invalid split leaves sequence untouched");
        var restored=DialogueSequence.Clone(sequence);restored.Move(restored.Clips[1].Id,0);
        Check(restored.Compile(assets.Library).Timeline.At(.16,montage.Project).Binding.PoseId=="pl21100","Clip reorder changes rendered order");
        restored.Clips[0].In=.2;restored.Clips[0].Out=.8;var trimmed=restored.Compile(assets.Library);
        Check(Math.Abs(trimmed.Timeline.Duration-(.6+boundary))<.00001,"Trimmed clip duration / save round trip");
        Check(DialogueAudio.Events(trimmed.Project,trimmed.Timeline,assets.Library).All(e=>e.At>=0 && e.At<trimmed.Timeline.Duration),"Trim discards out-of-range audio");
        var pagedSpeech=new DialogueProject{DefaultPose="pl21100",Text="Primera página.\n\nSegunda página."};var pagedLine=DialogueCompiler.Compile(pagedSpeech,assets.Library);
        Check(pagedLine.At(pagedLine.Pages[1].Start+.2,pagedSpeech).AnimationSeconds>pagedLine.Pages[1].Start,"Hair / body clock continues across pages of same pose");
        var speech=new DialogueProject{DefaultPose="pl21100",Text="HOLA, pelo continuo y sin saltos."};var speechLine=DialogueCompiler.Compile(speech,assets.Library);
        double comma=speechLine.Pages[0].Glyphs.First(g=>g.Text==",").At;
        Check(speechLine.At(comma+.05,speech).Speaking,"Punctuation keeps continuous speech animation");
        var repeatedGesture=new DialogueProject{Text="Uno.Dos."};
        repeatedGesture.Ranges.Add(new(){Start=0,End=4,PoseId="pl0005",Mode="Gesto"});
        repeatedGesture.Ranges.Add(new(){Start=4,End=8,PoseId="pl0005",Mode="Gesto"});
        var gestures=DialogueCompiler.Compile(repeatedGesture,assets.Library);double secondGesture=gestures.TimeFor(4);
        Check(Math.Abs(gestures.At(secondGesture+.01,repeatedGesture).AnimationSeconds-.01)<1e-8,"Separate explicit fragments replay the same gesture");
        var continuousGesture=DialogueSequence.Clone(longProject);continuousGesture.Ranges.Add(new(){Start=0,End=continuousGesture.Text.Length,PoseId="pl21108",Mode="Gesto"});
        var continuousGestures=DialogueCompiler.Compile(continuousGesture,assets.Library);
        Check(continuousGestures.At(continuousGestures.Pages[1].Start+.2,continuousGesture).AnimationSeconds>continuousGestures.Pages[1].Start,"A gesture continuing on another page does not restart");
        foreach(var actor in assets.Library.Poses)
        foreach(var clip in actor.Clips)
        foreach(double time in new[]{0d,clip.Total-.01,clip.Total+.5,clip.Total*3+.3})
        {
            double expected=time<clip.Total?time:clip.Loop?clip.Restart+(time-clip.Total)%Math.Max(1,clip.Total-clip.Restart):clip.Total-.001;
            Check(AnimationClock.Frame(clip,time)==expected,"Live / video animation loop parity");
        }
        foreach(double oldSpeed in new[]{.5,1,2})
        foreach(double nextSpeed in new[]{.5,1,2})
        {
            double anchor=AnimationClock.AnchorAfterSpeedChange(8,2,oldSpeed,nextSpeed);
            Check(Math.Abs((8-anchor)*nextSpeed-6*oldSpeed)<1e-8,"Speed changes preserve animation position");
        }
        var positioned=new DialogueSequence();
        foreach(double x in new[]{-.25,0,.25})
        {
            var actor=DialogueSequence.Clone(p);actor.Ranges.Clear();actor.Text="Un personaje aparece solo durante su propio diálogo.";
            actor.AvatarX=x;actor.AvatarY=.04;actor.AvatarScale=.75;
            positioned.Clips.Add(new DialogueShot{Project=actor});
        }
        var restoredPlacement=DialogueSequence.Clone(positioned);Check(restoredPlacement.Clips[2].Project.AvatarX==.25 && restoredPlacement.Clips[2].Project.AvatarScale==.75,"Position JSON roundtrip");
        foreach(int width in new[]{1280,1920})
        {
            positioned.Width=width;positioned.Height=width*9/16;var output=positioned.Compile(assets.Library);double at=0;
            for(int i=0;i<positioned.Clips.Count;i++)
            {
                var actor=DialogueSequence.Clone(positioned.Clips[i].Project);actor.Width=width;actor.Height=width*9/16;
                var t=DialogueCompiler.Compile(actor,assets.Library);var r=new DialogueRenderer(assets);
                var individual=r.Render(actor,t,.35);var joined=r.Render(output.Project,output.Timeline,at+.35);
                Check(DialogueRenderer.StraightBgra(individual).SequenceEqual(DialogueRenderer.StraightBgra(joined)),"Per-clip position/scale render parity at "+width);
                if(width==1280)Save(joined,Path.Combine(folder,"posicion-"+i+".png"));
                at+=t.Duration;
            }
        }
        positioned.Width=1280;positioned.Height=720;var original=positioned.Clips[0];
        var splitPart=positioned.Split(original.Id,.5,assets.Library);
        Check(splitPart.Project.AvatarX==original.Project.AvatarX && splitPart.Project.AvatarScale==original.Project.AvatarScale,"Split preserves placement");
        var invalid=DialogueSequence.Clone(p);invalid.AvatarX=double.NaN;bool invalidPlacementRejected=false;
        try{invalid.Validate(assets.Library);}catch(InvalidDataException){invalidPlacementRejected=true;}Check(invalidPlacementRejected,"Reject nonfinite placement");
        var montageWindow=new DialogueSequenceWindow(assets,p,testing:true);montageWindow.TestSequence(DialogueSequence.Clone(sequence),boundary+.5);montageWindow.VerifyHistory();montageWindow.VerifyEasyDeletion();montageWindow.VerifyQuickEditor();montageWindow.VerifyPlacementEditor();montageWindow.TestSequence(sequence,boundary+.5);
        Save(new DialogueRenderer(assets).Render(montage.Project,montage.Timeline,boundary+.5),Path.Combine(folder,"montage-frame.png"));
        File.WriteAllText(Path.Combine(folder,"montage-boundary.txt"),boundary.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var montageVisual=(FrameworkElement)montageWindow.Content;montageVisual.Measure(new Size(1280,860));montageVisual.Arrange(new Rect(0,0,1280,860));montageVisual.UpdateLayout();SaveVisual(montageVisual,Path.Combine(folder,"montaje.png"));montageVisual.Measure(new Size(1000,700));montageVisual.Arrange(new Rect(0,0,1000,700));montageVisual.UpdateLayout();SaveVisual(montageVisual,Path.Combine(folder,"montaje-compacto.png"));montageWindow.Close();
        File.WriteAllText(Path.Combine(folder,"ejemplo.avmontage.json"),JsonSerializer.Serialize(sequence,AssetStore.Json));
        await DialogueExport.ExportAsync(montage.Project,montage.Timeline,assets,Path.Combine(folder,"ejemplo-montaje.mov"),Application.Current.Dispatcher,null,CancellationToken.None);
        var events=DialogueAudio.Events(p,timeline,assets.Library);Check(events.Count>3,"Original letter blips scheduled");
        Check(events.All(e=>timeline.Pages.SelectMany(pg=>pg.Glyphs).Any(g=>g.At==e.At && g.MakesSound)),"No sounds on whitespace or held pages");
        var renderer=new DialogueRenderer(assets);var image=renderer.Render(p,timeline,.5);var raw=DialogueRenderer.StraightBgra(image);
        byte Alpha(int x,int y)=>raw[(y*p.Width+x)*4+3];
        Check(Alpha(0,0)==0 && Alpha(1279,0)==0,"Transparent video background");Check(Alpha(50,690)==210,"Translucent dialogue panel preserved");
        Check(Enumerable.Range(0,raw.Length/4).Any(i=>raw[i*4+3]==255),"Opaque character pixels preserved");
        Save(image,Path.Combine(folder,"dialogue-frame.png"));
        var hd=JsonSerializer.Deserialize<DialogueProject>(JsonSerializer.Serialize(p,AssetStore.Json),AssetStore.Json)!;hd.Width=1920;hd.Height=1080;hd.Fps=60;
        var hdImage=renderer.Render(hd,DialogueCompiler.Compile(hd,assets.Library),.5);Check(hdImage.PixelWidth==1920 && hdImage.PixelHeight==1080,"1080p rendering");
        var window=new DialogueWindow(assets,"pl0000",testing:true);window.VerifyFragmentWorkflow();window.TestProject(p,.5);var visual=(FrameworkElement)window.Content;
        visual.Measure(new Size(1220,840));visual.Arrange(new Rect(0,0,1220,840));visual.UpdateLayout();SaveVisual(visual,Path.Combine(folder,"editor.png"));visual.Measure(new Size(1050,700));visual.Arrange(new Rect(0,0,1050,700));visual.UpdateLayout();SaveVisual(visual,Path.Combine(folder,"editor-compacto.png"));window.TestTab(1);visual.UpdateLayout();SaveVisual(visual,Path.Combine(folder,"editor-ritmo.png"));window.Close();
        File.WriteAllText(Path.Combine(folder,"ejemplo.avdialog.json"),JsonSerializer.Serialize(p,AssetStore.Json));
        await DialogueExport.ExportAsync(p,timeline,assets,Path.Combine(folder,"ejemplo-transparente.mov"),Application.Current.Dispatcher,null,CancellationToken.None);
        await DialogueExport.ExportAsync(p,timeline,assets,Path.Combine(folder,"ejemplo-transparente.webm"),Application.Current.Dispatcher,null,CancellationToken.None);
        string keep=Path.Combine(folder,"cancelled-output.mov");await File.WriteAllTextAsync(keep,"KEEP PREVIOUS VIDEO");
        using(var cancel=new CancellationTokenSource())
        {
            bool wasCancelled=false;var progress=new Progress<(int Current,int Total)>(v=>{if(v.Current>=6)cancel.Cancel();});
            try{await DialogueExport.ExportAsync(p,timeline,assets,keep,Application.Current.Dispatcher,progress,cancel.Token);}catch(OperationCanceledException){wasCancelled=true;}
            Check(wasCancelled && await File.ReadAllTextAsync(keep)=="KEEP PREVIOUS VIDEO","Cancellation preserves previous output");
        }
        Check(!Directory.EnumerateFiles(folder,".avatar-render-*").Any(),"No partial videos left behind");
        await ConversationVerification.RunAsync(assets,folder);
        await StudioVerification.RunAsync(assets,folder);
        File.WriteAllText(Path.Combine(folder,"verification.json"),JsonSerializer.Serialize(new{version=typeof(MainWindow).Assembly.GetName().Version!.ToString(3),perClipPlacement=true,positionPersistence=true,placementRenderParity720and1080=true,positionUndoRedo=true,inlineClipEditing=true,automaticClipSave=true,nextClipInheritsSettings=true,inlineUndoRedo=true,montageSplit=true,splitPixelsAndAudioIdentical=true,montageOrder=true,montageTrim=true,perClipAudio=true,animationClockParity=true,speedChangesPreservePosition=true,explicitGestureReplay=true,gesturePageContinuity=true,fragmentWorkflow=true,perFragmentTiming=true,rangeBinding=true,unicodeEditing=true,automaticPagination=true,gameFont=true,originalBlips=true,
            alphaFrameChecked=true,movExport=true,webmExport=true,cancellationPreservesOutput=true,duration=timeline.Duration,frames=(int)Math.Ceiling(timeline.Duration*p.Fps),audioEvents=events.Count},AssetStore.Json));
    }
    static void Save(BitmapSource bitmap,string path){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);png.Save(stream);}
    static void SaveVisual(FrameworkElement visual,string path){var bitmap=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(visual);Save(bitmap,path);}
}
