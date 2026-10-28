using System.IO;
using System.Text.Json;

namespace AvatarStudio;
public sealed class DialogueShot
{
    public string Id { get; set; }=Guid.NewGuid().ToString("N");
    public string Name { get; set; }="Nuevo clip";
    public string SceneName {get;set;}="Escena 1";
    public DialogueProject Project { get; set; }=new();
    public double In { get; set; }
    public double? Out { get; set; }
    public double Duration(Library library)=>(Out??DialogueCompiler.Compile(Project,library).Duration)-In;
    public double ReadingStart(Library library)
    {
        var t=DialogueCompiler.Compile(Project,library);double speech=Math.Max(t.LastWritten,Project.Recording?.Duration??0);
        return Math.Clamp(speech-In,0,Duration(library));
    }
    public void Resize(double length,Library library)
    {
        if(!double.IsFinite(length))throw new InvalidDataException("Duración no válida.");
        var natural=Project.Copy();natural.FinalHold=0;var t=DialogueCompiler.Compile(natural,library);
        double minimum=Out!=null?In+.1:Math.Max(In+.1,Math.Max(t.LastWritten,Project.Recording?.Duration??0)+.1);
        double finish=Math.Clamp(In+length,minimum,t.Duration+30);
        if(finish>=t.Duration){Project.FinalHold=finish-t.Duration;Out=null;}else Out=finish;
    }
}
public sealed class DialogueSequence
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Name {get;set;}="Mi video";
    public CourtCast Court {get;set;}=new();
    public int Version { get; set; }=1;
    public List<DialogueShot> Clips { get; set; }=new();
    public int Width { get; set; }=1280;
    public int Height { get; set; }=720;
    public int Fps { get; set; }=30;
    public Dictionary<string,ActorPreset> Actors { get; set; }=new();
    public static T Clone<T>(T value)
    {
        object result=value switch
        {
            DialogueProject p=>p.Copy(),
            DialogueShot c=>new DialogueShot{Id=c.Id,Name=c.Name,SceneName=c.SceneName,In=c.In,Out=c.Out,Project=c.Project.Copy()},
            DialogueSequence s=>new DialogueSequence{Id=s.Id,Name=s.Name,Court=s.Court with {},Version=s.Version,Width=s.Width,Height=s.Height,Fps=s.Fps,Clips=s.Clips.Select(Clone).ToList(),Actors=s.Actors.ToDictionary(p=>p.Key,p=>p.Value with {})},
            _=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value,AssetStore.Json),AssetStore.Json)!
        };
        return (T)result;
    }
    public void Validate(Library library)
    {
        if(!Guid.TryParse(Id,out _) || Name.Length>120 || Clips.Any(c=>c.Name.Length>150 || c.SceneName.Length>120))throw new InvalidDataException("Nombre de proyecto o escena no válido.");
        Court.Validate(library);
        if(Version!=1 || Clips.Count>100)throw new InvalidDataException("El montaje admite hasta 100 clips.");
        if(Clips.Select(c=>c.Id).Distinct().Count()!=Clips.Count)throw new InvalidDataException("Identificadores de clips repetidos.");
        if(!((Width==1280 && Height==720)||(Width==1920 && Height==1080)) || Fps is not (30 or 60))throw new InvalidDataException("Formato de montaje no válido.");
        if(Clips.Sum(c=>(long)(c.Project.Recording?.Pcm.Length??0)+(c.Project.Scene.Image?.Png.Length??0)+c.Project.Scene.Layers.Sum(l=>(long)(l.Image?.Png.Length??0)))>128_000_000)throw new InvalidDataException("Divide las grabaciones en montajes de menos de 128 MB de audio e imágenes.");
        foreach(var preset in Actors.Values)preset.Validate(library);
        foreach(var clip in Clips)
        {
            clip.Project.Validate(library);
            double duration=DialogueCompiler.Compile(clip.Project,library).Duration,end=clip.Out??duration;
            if(string.IsNullOrWhiteSpace(clip.Project.Text) || !double.IsFinite(clip.In) || !double.IsFinite(end) || clip.In<0 || end>duration+.000001 || end-clip.In<.1)
                throw new InvalidDataException($"Revisa el inicio y final de «{clip.Name}». El clip debe durar al menos 0,1 s.");
        }
    }
    public (DialogueProject Project,DialogueTimeline Timeline) Compile(Library library)
    {
        Validate(library);if(Clips.Count==0)throw new InvalidDataException("Añade un clip a la línea de tiempo.");
        var output=Clone(Clips[0].Project);output.Text="Montaje";output.Ranges.Clear();output.Scene=new();output.Recording=null;output.FitTextToVoice=false;output.FinalHold=0;output.Width=Width;output.Height=Height;output.Fps=Fps;
        var timeline=new DialogueTimeline();double cursor=0;
        foreach(var shot in Clips)
        {
            var source=DialogueCompiler.Compile(shot.Project,library);double end=shot.Out??source.Duration;
            foreach(var page in source.Pages.Where(p=>p.End>shot.In && p.Start<end))
            {
                var copy=new DialoguePage{Text=page.Text,Start=cursor+Math.Max(0,page.Start-shot.In),End=cursor+Math.Min(end,page.End)-shot.In};
                foreach(var g in page.Glyphs)
                {
                    // Preserve pre-trim glyphs for already visible text, but mute their audio.
                    copy.Glyphs.Add(new(){Text=g.Text,OriginalStart=g.OriginalStart,OriginalEnd=g.OriginalEnd,RenderStart=g.RenderStart,RenderEnd=g.RenderEnd,
                        Binding=g.Binding with {Id=shot.Id+":"+g.Binding.Id,Mirror=shot.Project.Mirror,AvatarX=shot.Project.AvatarX,AvatarY=shot.Project.AvatarY,AvatarScale=shot.Project.AvatarScale},At=cursor+g.At-shot.In,Duration=g.Duration,SpeakingDuration=g.SpeakingDuration,
                        SegmentStart=cursor+g.SegmentStart-shot.In,AnimationStart=cursor+g.AnimationStart-shot.In,VoiceOverride=g.At<shot.In || g.At>=end?"Sin sonido":shot.Project.Voice,VolumeOverride=shot.Project.Volume});
                }
                timeline.Pages.Add(copy);
            }
            foreach(var scene in source.Scenes){double a=Math.Max(scene.Start,shot.In),b=Math.Min(scene.End,end);if(b>a)timeline.Scenes.Add(new(cursor+a-shot.In,cursor+b-shot.In,scene.SourceOffset+a-scene.Start,scene.SourceDuration,scene.Design));}
            foreach(var voice in source.Recordings)
            {
                double a=Math.Max(voice.Start,shot.In),b=Math.Min(voice.End,end);
                if(b>a)timeline.Recordings.Add(new(cursor+a-shot.In,cursor+b-shot.In,voice.SourceOffset+a-voice.Start,voice.Voice));
            }
            cursor+=end-shot.In;
        }
        if(cursor>1800)throw new InvalidDataException("Divide el montaje en videos de menos de 30 minutos.");
        return(output,timeline);
    }
    public DialogueShot Split(string id,double offset,Library library)
    {
        if(Clips.Count>=100)throw new InvalidDataException("Máximo 100 clips por montaje.");
        var shot=Clips.First(c=>c.Id==id);double finish=shot.Out??DialogueCompiler.Compile(shot.Project,library).Duration;
        double cut=shot.In+offset;
        if(!double.IsFinite(cut) || cut-shot.In<.1 || finish-cut<.1)throw new InvalidDataException("Coloca el cursor dentro del clip, a más de 0,1 s de sus bordes.");
        var right=Clone(shot);right.Id=Guid.NewGuid().ToString("N");right.Name+=" · parte 2";right.In=cut;
        shot.Out=cut;Clips.Insert(Clips.IndexOf(shot)+1,right);return right;
    }
    public bool Move(string id,int newIndex)
    {
        int old=Clips.FindIndex(c=>c.Id==id);if(old<0 || newIndex<0 || newIndex>=Clips.Count || old==newIndex)return false;
        var clip=Clips[old];Clips.RemoveAt(old);Clips.Insert(newIndex,clip);return true;
    }
}
