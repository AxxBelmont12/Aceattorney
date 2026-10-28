using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AvatarStudio;
public sealed record SpeechWord(int StartChar,int EndChar,double Start,double End);
public sealed record RecognizedWord(string Text,double Start,double End);
public sealed class TranscriptPart
{
    public string Text {get;set;}="";
    public double Start {get;set;}
    public double End {get;set;}
    public string OriginalText {get;set;}="";
    public List<RecognizedWord> Words {get;set;}=new();
    public override string ToString()=>$"{Start:0.00}–{End:0.00} · {Text}";
    public List<SpeechWord> Timings()
    {
        var matches=Regex.Matches(Text,@"\S+").Cast<Match>().ToList();var original=Regex.Matches(OriginalText,@"\S+").Cast<Match>().ToList();
        if(matches.Count==Words.Count && original.Count==Words.Count && Words.Count>0)
        {
            double first=Words[0].Start,last=Words[^1].End,span=Math.Max(.01,last-first);
            return matches.Select((m,i)=>new SpeechWord(m.Index,m.Index+m.Length,Start+(Words[i].Start-first)/span*(End-Start),Start+(Words[i].End-first)/span*(End-Start))).ToList();
        }
        return SpeechAlignment.Even(Text,Start,End);
    }
}
public sealed class TranscriptDocument
{
    public List<TranscriptPart> Parts {get;set;}=new();
    public double Duration {get;set;}
    public string Text=>string.Join(" ",Parts.Select(p=>p.Text.Trim()));
    public static TranscriptDocument Parse(string json,double duration)
    {
        using var data=JsonDocument.Parse(json);var words=new List<RecognizedWord>();string prefix="";
        if(!data.RootElement.TryGetProperty("transcription",out var entries))throw new InvalidDataException("La transcripción no tiene el formato esperado.");
        foreach(var entry in entries.EnumerateArray())
        {
            string text=(entry.GetProperty("text").GetString()??"").Trim();if(string.IsNullOrWhiteSpace(text) || Regex.IsMatch(text,@"^\[.*\]$"))continue;
            double start=entry.GetProperty("offsets").GetProperty("from").GetDouble()/1000,end=entry.GetProperty("offsets").GetProperty("to").GetDouble()/1000;
            start=Math.Clamp(start,0,duration);end=Math.Clamp(end,start,duration);
            if(!text.Any(char.IsLetterOrDigit) && words.Count>0 && !text.Contains('¿') && !text.Contains('¡')){words[^1]=words[^1] with {Text=words[^1].Text+text,End=Math.Max(words[^1].End,end)};continue;}
            if(text is "¿" or "¡"){prefix+=text;continue;}
            start=Math.Max(words.LastOrDefault()?.End??0,start);end=Math.Max(start,Math.Min(duration,end));if(end-start<.001){if(start>=duration-.001)continue;end=Math.Min(duration,start+.03);}
            words.Add(new(prefix+text,start,end));prefix="";
        }
        if(words.Count==0)throw new InvalidDataException("No se reconocieron palabras. Revisa el idioma, el volumen o el recorte del audio.");
        var result=new TranscriptDocument{Duration=duration};var current=new List<RecognizedWord>();
        void Finish(){if(current.Count==0)return;string text=string.Join(" ",current.Select(w=>w.Text));result.Parts.Add(new(){Text=text,OriginalText=text,Start=current[0].Start,End=current[^1].End,Words=current.ToList()});current.Clear();}
        foreach(var word in words)
        {
            if(current.Count>0 && (word.Start-current[^1].End>.65 || word.End-current[0].Start>8 || current.Sum(w=>w.Text.Length)+word.Text.Length>110 || current.Count>=18))Finish();
            current.Add(word);if(current.Count>=4 && Regex.IsMatch(word.Text,@"[.!?…]$"))Finish();
        }
        Finish();return result;
    }
    public void Validate()
    {
        if(Parts.Count==0 || Parts.Count>100 || !double.IsFinite(Duration) || Duration<.05 || Duration>150)throw new InvalidDataException("La transcripción admite hasta 100 fragmentos.");
        double end=0;foreach(var p in Parts){if(string.IsNullOrWhiteSpace(p.Text) || p.Text.Length>10000 || !double.IsFinite(p.Start+p.End) || p.Start<end-.001 || p.End<=p.Start || p.End>Duration+.001)throw new InvalidDataException("Revisa los tiempos: cada fragmento debe tener texto, estar dentro del audio y no solaparse con el siguiente.");end=p.End;}
        if(Text.Length>10000)throw new InvalidDataException("El texto supera 10.000 caracteres. Usa una grabación más corta.");
    }
    public DialogueProject Single(DialogueProject style,RecordedVoice voice)
    {
        Validate();var p=style.Copy();p.Text=Text;p.Ranges.Clear();p.SpeechWords.Clear();int cursor=0;
        foreach(var part in Parts){string text=part.Text.Trim();var timings=part.Timings();int leading=part.Text.Length-part.Text.TrimStart().Length;foreach(var word in timings){int a=word.StartChar-leading,b=word.EndChar-leading;if(a>=0 && b<=text.Length)p.SpeechWords.Add(word with {StartChar=cursor+a,EndChar=cursor+b});}cursor+=text.Length+1;}
        p.Recording=voice;p.SyncSpeechWords=true;p.FitTextToVoice=false;p.FinalHold=0;return p;
    }
    public List<DialogueProject> Fragments(DialogueProject style,RecordedVoice voice)
    {
        Validate();var output=new List<DialogueProject>();
        for(int i=0;i<Parts.Count;i++)
        {
            double start=i==0?0:Parts[i].Start,end=i+1<Parts.Count?Parts[i+1].Start:Duration;
            if(end-start<.1)throw new InvalidDataException("Hay dos fragmentos demasiado juntos. Une sus textos o separa sus tiempos al menos 0,1 s.");
            var part=Parts[i];var p=style.Copy();p.Text=part.Text.Trim();p.Ranges.Clear();int leading=part.Text.Length-part.Text.TrimStart().Length;
            p.SpeechWords=part.Timings().Select(w=>w with {StartChar=w.StartChar-leading,EndChar=w.EndChar-leading,Start=Math.Max(0,w.Start-start),End=Math.Min(end-start,w.End-start)}).ToList();
            p.Recording=Slice(voice,start,end);p.SyncSpeechWords=true;p.FitTextToVoice=false;p.FinalHold=0;output.Add(p);
        }
        return output;
    }
    public static RecordedVoice Slice(RecordedVoice voice,double start,double end)
    {
        long first=(long)Math.Round(start*DialogueAudio.Rate),last=(long)Math.Round(end*DialogueAudio.Rate);int count=(int)(last-first);if(count>120*DialogueAudio.Rate)throw new InvalidDataException("Divide la grabación en fragmentos de hasta dos minutos.");var bytes=new byte[count*2];
        for(int i=0;i<count;i++)BitConverter.TryWriteBytes(bytes.AsSpan(i*2,2),(short)Math.Clamp((int)Math.Round(voice.SampleAt((first+i)/(double)DialogueAudio.Rate)*32768),short.MinValue,short.MaxValue));
        return voice with {Pcm=bytes,TrimStart=0,TrimEnd=null,Offset=0};
    }
}
public static class SpeechAlignment
{
    public static List<SpeechWord> Even(string text,double start,double end)
    {
        var matches=Regex.Matches(text,@"\S+").Cast<Match>().ToList();int total=matches.Sum(m=>m.Length),at=0;var result=new List<SpeechWord>();
        foreach(var m in matches){double a=start+(end-start)*at/Math.Max(1,total);at+=m.Length;result.Add(new(m.Index,m.Index+m.Length,a,start+(end-start)*at/Math.Max(1,total)));}return result;
    }
    public static List<SpeechWord> Reflow(string text,List<SpeechWord> words)
    {
        if(words.Count==0)return new();var matches=Regex.Matches(text,@"\S+").Cast<Match>().ToList();
        if(matches.Count==words.Count)return matches.Select((m,i)=>words[i] with {StartChar=m.Index,EndChar=m.Index+m.Length}).ToList();
        return Even(text,words[0].Start,words[^1].End);
    }
    public static void Apply(DialogueTimeline timeline,DialogueProject project)
    {
        var words=project.SpeechWords;double previous=0;
        foreach(var page in timeline.Pages)
        foreach(var g in page.Glyphs)
        {
            var w=words.FirstOrDefault(w=>w.StartChar<=g.OriginalStart && g.OriginalStart<w.EndChar);
            if(w!=null){double span=Math.Max(.001,w.End-w.Start),length=w.EndChar-w.StartChar;g.At=w.Start+span*(g.OriginalStart-w.StartChar)/length;g.Duration=span*(g.OriginalEnd-g.OriginalStart)/length;previous=g.At+g.Duration;}
            else{g.At=previous;g.Duration=0;}g.SpeakingDuration=g.Duration;
        }
        for(int i=0;i<timeline.Pages.Count;i++){var page=timeline.Pages[i];page.Start=i==0?0:page.Glyphs.FirstOrDefault()?.At??timeline.Pages[i-1].End;page.End=i+1<timeline.Pages.Count?timeline.Pages[i+1].Glyphs[0].At:Math.Max(timeline.LastWritten,project.Recording?.Duration??0);}
        string key="";double animation=0,segment=0;string binding="";
        foreach(var g in timeline.Pages.SelectMany(p=>p.Glyphs)){string next=g.Binding.PoseId+":"+g.Binding.Mode+(g.Binding.Mode=="Gesto"?":"+g.Binding.Id:"");if(next!=key){key=next;animation=g.At;}if(g.Binding.Id!=binding){binding=g.Binding.Id;segment=g.At;}g.AnimationStart=animation;g.SegmentStart=segment;}
    }
}
