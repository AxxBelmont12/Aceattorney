using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace AvatarStudio;

public sealed class DialogueProject
{
    public int Version { get; set; } = 1;
    public string Text { get; set; } = "¡Un momento! Esto merece una explicación.\nPodemos cambiar de pose mientras aparece el diálogo.";
    public string DefaultPose { get; set; } = "pl0000";
    public string DisplayName { get; set; } = "";
    public string DefaultColor { get; set; } = "Blanco";
    public string DefaultMode { get; set; } = "Hablar";
    public string Voice { get; set; } = "Automático";
    public double CharactersPerSecond { get; set; } = 30;
    public double PageHold { get; set; } = 1;
    public double Volume { get; set; } = .45;
    public List<SpeechWord> SpeechWords {get;set;}=new();
    public bool SyncSpeechWords {get;set;}
    public SceneDesign Scene {get;set;}=new();
    public RecordedVoice? Recording { get; set; }
    public bool FitTextToVoice { get; set; }
    public double FinalHold { get; set; }
    public double AvatarX { get; set; }
    public double AvatarY { get; set; }
    public double AvatarScale { get; set; } = 1;
    public bool Mirror { get; set; }
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int Fps { get; set; } = 30;
    public List<DialogueRange> Ranges { get; set; } = new();

    public DialogueProject Copy()
    {
        var copy=(DialogueProject)MemberwiseClone();copy.Ranges=Ranges.Select(r=>r.Copy()).ToList();copy.Scene=Scene.Copy();copy.SpeechWords=SpeechWords.ToList();return copy;
    }
    public void Validate(Library library)
    {
        if (Version != 1) throw new InvalidDataException("Versión de proyecto no compatible.");
        if (Text.Length > 10000 || Ranges.Count > 500) throw new InvalidDataException("El proyecto es demasiado largo; divídelo en varios diálogos.");
        if (!library.Poses.Any(p=>p.Id==DefaultPose)) throw new InvalidDataException("No se encuentra el personaje base.");
        if (!double.IsFinite(CharactersPerSecond) || CharactersPerSecond<5 || CharactersPerSecond>80 ||
            !double.IsFinite(PageHold) || PageHold<.2 || PageHold>5 || !double.IsFinite(Volume) || Volume<0 || Volume>1)
            throw new InvalidDataException("Velocidad, pausa o volumen fuera de los límites.");
        if (!((Width==1280 && Height==720) || (Width==1920 && Height==1080)) || (Fps!=30 && Fps!=60))
            throw new InvalidDataException("Resolución o fotogramas por segundo no compatibles.");
        if(!double.IsFinite(AvatarX) || !double.IsFinite(AvatarY) || !double.IsFinite(AvatarScale) || Math.Abs(AvatarX)>.75 || Math.Abs(AvatarY)>.5 || AvatarScale<.25 || AvatarScale>2.5)
            throw new InvalidDataException("Posición o tamaño del personaje fuera de los límites.");
        Scene.Validate();Recording?.Validate();if((long)(Recording?.Pcm.Length??0)+(Scene.Image?.Png.Length??0)+Scene.Layers.Sum(l=>(long)(l.Image?.Png.Length??0))>64_000_000)throw new InvalidDataException("Reduce las imágenes o el audio de este fragmento a menos de 64 MB.");
        if(!double.IsFinite(FinalHold) || FinalHold<0 || FinalHold>30)throw new InvalidDataException("La pausa final admite de 0 a 30 segundos.");
        if(SpeechWords.Count>10000)throw new InvalidDataException("Demasiadas palabras sincronizadas.");
        int wordEnd=0;double wordTime=0;foreach(var w in SpeechWords){if(w.StartChar<wordEnd || w.EndChar<=w.StartChar || w.EndChar>Text.Length || !double.IsFinite(w.Start+w.End) || w.Start<wordTime-.001 || w.End<w.Start || w.End>1800)throw new InvalidDataException("Revisa los tiempos de las palabras sincronizadas.");wordEnd=w.EndChar;wordTime=w.Start;}
        int end=0;
        var boundaries=StringInfo.ParseCombiningCharacters(Text).Append(Text.Length).ToHashSet();
        foreach (var range in Ranges.OrderBy(r=>r.Start))
        {
            if (range.Start<end || range.End<=range.Start || range.End>Text.Length || !boundaries.Contains(range.Start) || !boundaries.Contains(range.End))
                throw new InvalidDataException("Hay tramos superpuestos o una selección que divide un carácter.");
            if (!library.Poses.Any(p=>p.Id==range.PoseId)) throw new InvalidDataException("No se encuentra una de las poses del diálogo.");
            if (range.CharactersPerSecond is double rate && (!double.IsFinite(rate) || rate<5 || rate>80) ||
                !double.IsFinite(range.PauseAfter) || range.PauseAfter<0 || range.PauseAfter>10)
                throw new InvalidDataException("El ritmo del fragmento está fuera de los límites.");
            end=range.End;
        }
    }
}
public sealed class DialogueRange
{
    public DialogueRange Copy()=>(DialogueRange)MemberwiseClone();
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Start { get; set; }
    public int End { get; set; }
    public string PoseId { get; set; } = "";
    public string Mode { get; set; } = "Hablar";
    public string Color { get; set; } = "Blanco";
    public string DisplayName { get; set; } = "";
    public double? CharactersPerSecond { get; set; }
    public double PauseAfter { get; set; }
}
public static class DialogueEdits
{
    public static void AssignRange(DialogueProject p,DialogueRange value)
    {
        var result=new List<DialogueRange>();
        foreach(var r in p.Ranges)
        {
            if(r.End<=value.Start || r.Start>=value.End){result.Add(r);continue;}
            if(r.Start<value.Start){var left=r.Copy();left.End=value.Start;left.PauseAfter=0;result.Add(left);}
            if(r.End>value.End){var right=r.Copy();right.Id=Guid.NewGuid().ToString("N");right.Start=value.End;result.Add(right);}
        }
        result.Add(value);p.Ranges=result.OrderBy(r=>r.Start).ToList();
    }
    public static void NormalizeNewlines(DialogueProject project)
    {
        var text=project.Text;var map=new int[text.Length+1];var output=new StringBuilder();
        for(int i=0;i<text.Length;i++)
        {
            map[i]=output.Length;
            if(text[i]=='\r' && i+1<text.Length && text[i+1]=='\n')
            {output.Append("\r\n");map[++i]=output.Length-1;}
            else if(text[i] is '\r' or '\n')output.Append("\r\n");
            else output.Append(text[i]);
        }
        map[text.Length]=output.Length;
        foreach(var range in project.Ranges){range.Start=map[range.Start];range.End=map[range.End];}
        project.Text=output.ToString();
    }
    public static (int Start,int End) Snap(string text,int start,int length)
    {
        var bounds=StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        int lo=Math.Clamp(start,0,text.Length),hi=Math.Clamp(start+length,0,text.Length);
        return (bounds.Last(b=>b<=lo),bounds.First(b=>b>=hi));
    }
    public static void AdjustRanges(DialogueProject p,string before,string after)
    {
        int start=0;
        while(start<before.Length && start<after.Length && before[start]==after[start]) start++;
        int oldEnd=before.Length,newEnd=after.Length;
        while(oldEnd>start && newEnd>start && before[oldEnd-1]==after[newEnd-1]) { oldEnd--;newEnd--; }
        int delta=newEnd-oldEnd;
        foreach(var r in p.Ranges)
        {
            int a=r.Start,b=r.End;
            r.Start=a<start?a:a>=oldEnd?a+delta:start;
            r.End=b<=start?b:b>=oldEnd?b+delta:newEnd;
            if(r.End>r.Start)
            {
                var snapped=Snap(after,r.Start,r.End-r.Start);r.Start=snapped.Start;r.End=snapped.End;
            }
        }
        p.Ranges.RemoveAll(r=>r.End<=r.Start);
        // Combining characters can merge across an edited boundary. Prefer
        // keeping ranges disjoint to silently applying two animations at once.
        int previous=0;
        foreach(var r in p.Ranges.OrderBy(r=>r.Start)) { r.Start=Math.Max(r.Start,previous);previous=r.End; }
        p.Ranges.RemoveAll(r=>r.End<=r.Start);
        if(before!=after && p.SpeechWords.Count>0)p.SpeechWords=SpeechAlignment.Reflow(after,p.SpeechWords);
        p.Text=after;
    }
}
public sealed record DialogueBinding(string Id,string PoseId,string Mode,string Color,string Speaker,bool? Mirror=null,double? AvatarX=null,double? AvatarY=null,double? AvatarScale=null);
public sealed class DialogueGlyph
{
    public required string Text { get; init; }
    public required int OriginalStart { get; init; }
    public required int OriginalEnd { get; init; }
    public required DialogueBinding Binding { get; init; }
    public int RenderStart { get; set; }
    public int RenderEnd { get; set; }
    public double At { get; set; }
    public double Duration { get; set; }
    public string? VoiceOverride { get; set; }
    public double? VolumeOverride { get; set; }
    public double SpeakingDuration { get; set; }
    public double SegmentStart { get; set; }
    public double AnimationStart { get; set; }
    public bool MakesSound => Text.EnumerateRunes().Any(Rune.IsLetterOrDigit);
}
public sealed class DialoguePage
{
    public string Text { get; set; } = "";
    public List<DialogueGlyph> Glyphs { get; set; } = new();
    public double Start { get; set; }
    public double End { get; set; }
}
public sealed record DialogueState(DialoguePage Page,int Visible,DialogueBinding Binding,bool Speaking,double AnimationSeconds,bool PageFinished);
public sealed class DialogueTimeline
{
    public List<DialoguePage> Pages { get; } = new();
    public List<VoiceSpan> Recordings { get; } = new();
    public List<SceneSpan> Scenes {get;}=new();
    public SceneSpan SceneAt(double at,DialogueProject p)=>Scenes.LastOrDefault(s=>s.Start<=at)??new(0,Duration,0,Duration,p.Scene);
    public double LastWritten => Pages.SelectMany(p=>p.Glyphs).Select(g=>g.At+g.Duration).DefaultIfEmpty(0).Max();
    public bool? RecordedSpeech(double seconds)
    {
        var voice=Recordings.FirstOrDefault(v=>seconds>=v.Start && seconds<v.End);
        return voice==null || !voice.Voice.LipSync?null:voice.Voice.Speaking(voice.SourceOffset+seconds-voice.Start);
    }
    public double Duration => Pages[^1].End;
    public DialogueState At(double seconds,DialogueProject project)
    {
        seconds=Math.Clamp(seconds,0,Duration);
        var page=Pages.LastOrDefault(p=>p.Start<=seconds) ?? Pages[0];
        var glyph=page.Glyphs.LastOrDefault(g=>g.At<=seconds);
        var binding=glyph?.Binding ?? page.Glyphs.FirstOrDefault()?.Binding ?? new("base",project.DefaultPose,project.DefaultMode,project.DefaultColor,project.DisplayName);
        // Run the speech clip continuously across letters and punctuation. Switching
        // to idle for each typed space jumps between unrelated hair/body frames.
        var segment=page.Glyphs.Where(g=>g.SegmentStart==(glyph?.SegmentStart??-1)).ToList();
        var final=segment.LastOrDefault();
        bool speaking=glyph!=null && final!=null && seconds<final.At+final.SpeakingDuration;
        bool finished=page.Glyphs.Count==0 || seconds>=page.Glyphs[^1].At+page.Glyphs[^1].Duration;
        return new(page,glyph==null?0:Math.Min(glyph.RenderEnd,page.Text.Length),binding,RecordedSpeech(seconds)??speaking,
            Math.Max(0,seconds-(glyph?.AnimationStart ?? page.Glyphs.FirstOrDefault()?.AnimationStart ?? page.Start)),finished);
    }
    public double TimeFor(int originalOffset) => Pages.SelectMany(p=>p.Glyphs).FirstOrDefault(g=>g.OriginalEnd>originalOffset)?.At ?? Duration;
}
public static class DialogueCompiler
{
    public const double FontSize=34,LineHeight=43,TextWidth=1090,MaxHeight=LineHeight*3;
    public static FormattedText Format(string text) => new(text,CultureInfo.GetCultureInfo("es-MX"),FlowDirection.LeftToRight,
        DialogueResources.Typeface,FontSize,Brushes.White,1) { MaxTextWidth=TextWidth,LineHeight=LineHeight };
    static string Display(IEnumerable<DialogueGlyph> glyphs) => string.Concat(glyphs.Select(g=>g.Text)).TrimEnd();
    public static DialogueTimeline Compile(DialogueProject project,Library library)
    {
        project.Validate(library);
        var tokens=new List<DialogueGlyph>();var iterator=StringInfo.GetTextElementEnumerator(project.Text);
        while(iterator.MoveNext())
        {
            string raw=iterator.GetTextElement();int at=iterator.ElementIndex;
            var r=project.Ranges.SingleOrDefault(r=>r.Start<=at && at<r.End);
            var binding=r==null?new DialogueBinding("base",project.DefaultPose,project.DefaultMode,project.DefaultColor,project.DisplayName):new(r.Id,r.PoseId,r.Mode,r.Color,r.DisplayName);
            tokens.Add(new(){ Text=raw is "\r\n" or "\r"?"\n":raw,OriginalStart=at,OriginalEnd=at+raw.Length,Binding=binding });
        }
        var result=new DialogueTimeline();var current=new List<DialogueGlyph>();
        void Finish()
        {
            if(current.Count==0) return;
            if(current.All(g=>string.IsNullOrWhiteSpace(g.Text))) { current=new();return; }
            result.Pages.Add(new(){Glyphs=current,Text=Display(current)});current=new();
        }
        foreach(var token in tokens)
        {
            if(current.Count>0 && library.Poses.First(p=>p.Id==current[^1].Binding.PoseId).Character != library.Poses.First(p=>p.Id==token.Binding.PoseId).Character) Finish();
            if(token.Text=="\f") { Finish();continue; }
            if(token.Text=="\n" && current.LastOrDefault()?.Text=="\n") { Finish();continue; }
            var candidate=Display(current.Append(token));
            if(current.Count>0 && candidate.Length>0 && Format(candidate).Height>MaxHeight+.2)
            {
                int split=current.FindLastIndex(g=>string.IsNullOrWhiteSpace(g.Text))+1;
                if(split>0 && split<current.Count)
                {
                    var carry=current.Skip(split).ToList();current=current.Take(split).ToList();Finish();current=carry;
                }
                else Finish();
            }
            current.Add(token);
            if(Display(current).Length>0 && Format(Display(current)).Height>MaxHeight+.2)
                throw new InvalidDataException("Una página no cabe en el cuadro. Reduce la palabra o añade un salto de línea.");
        }
        Finish();if(result.Pages.Count==0)result.Pages.Add(new());
        double time=.15;string previousPose="";double animationStart=.15;
        foreach(var page in result.Pages)
        {
            page.Start=time-.15;int render=0;string segment="";double segmentStart=time;
            foreach(var glyph in page.Glyphs)
            {
                if(segment!=glyph.Binding.Id) { segment=glyph.Binding.Id;segmentStart=time; }
                glyph.RenderStart=Math.Min(render,page.Text.Length);render+=glyph.Text.Length;
                glyph.RenderEnd=Math.Min(render,page.Text.Length);glyph.At=time;glyph.SegmentStart=segmentStart;
                // Explicit gesture fragments can replay the same reaction. A
                // continuation page keeps the binding id and its original clock.
                string animationKey=glyph.Binding.PoseId+":"+glyph.Binding.Mode+(glyph.Binding.Mode=="Gesto"?":"+glyph.Binding.Id:"");
                if(previousPose!=animationKey){previousPose=animationKey;animationStart=time;}glyph.AnimationStart=animationStart;
                double pause=glyph.Text is "." or "!" or "?" or "…" ? .22 : glyph.Text is "," or ";" or ":" ? .10 : 0;
                var range=project.Ranges.FirstOrDefault(r=>r.Id==glyph.Binding.Id);
                glyph.SpeakingDuration=1/(range?.CharactersPerSecond??project.CharactersPerSecond);
                double extra=range!=null && glyph.OriginalEnd==range.End?range.PauseAfter:0;
                glyph.Duration=(glyph.Text=="\n"?0:glyph.SpeakingDuration)+pause+extra;
                time+=glyph.Duration;
            }
            time+=project.PageHold;page.End=time;time+=.15;
        }
        if(project.SyncSpeechWords && project.SpeechWords.Count>0)SpeechAlignment.Apply(result,project);
        if(project.Recording is {} voice)
        {
            if(project.FitTextToVoice && !(project.SyncSpeechWords && project.SpeechWords.Count>0))
            {
                double factor=voice.Duration/result.Duration;
                foreach(var page in result.Pages)
                {
                    page.Start*=factor;page.End*=factor;
                    foreach(var g in page.Glyphs){g.At*=factor;g.Duration*=factor;g.SpeakingDuration*=factor;g.SegmentStart*=factor;g.AnimationStart*=factor;}
                }
            }
            result.Pages[^1].End=Math.Max(result.Duration,voice.Duration);
        }
        result.Pages[^1].End+=project.FinalHold;
        result.Scenes.Add(new(0,result.Duration,0,result.Duration,project.Scene));
        if(project.Recording is {} track)result.Recordings.Add(new(0,result.Duration,0,track));
        if(result.Duration>1800)throw new InvalidDataException("Divide el diálogo en videos de menos de 30 minutos.");
        return result;
    }
}
