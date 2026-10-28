using System.IO;

namespace AvatarStudio;

public sealed record DialogueBlip(double At,int Voice,double Volume);
public static class DialogueAudio
{
    public const int Rate=48000;
    static readonly Lazy<float[]> First=new(()=>Decode(DialogueResources.Read("DialogueBlip003")));
    static readonly Lazy<float[]> Second=new(()=>Decode(DialogueResources.Read("DialogueBlip004")));
    static readonly HashSet<string> Higher=new(){"Trucy Wright","Ema Skye","Olga Orly","Alita Tiala","Plum Kitaki","Vera Misham","Lamiroir"};
    public static List<DialogueBlip> Events(DialogueProject project,DialogueTimeline timeline,Library library)
    {
        var result=new List<DialogueBlip>();
        double last=-1;
        foreach(var glyph in timeline.Pages.SelectMany(p=>p.Glyphs))
        {
            string setting=glyph.VoiceOverride??project.Voice;double volume=glyph.VolumeOverride??project.Volume;
            if(timeline.Recordings.Any(v=>glyph.At>=v.Start && glyph.At<v.End) || setting=="Sin sonido" || volume<=0 || !glyph.MakesSound || glyph.At<0 || glyph.At-last<.052)continue;
            int voice=setting=="Original 2"?1:setting=="Original 1"?0:
                Higher.Contains(library.Poses.First(p=>p.Id==glyph.Binding.PoseId).Character)?1:0;
            result.Add(new(glyph.At,voice,volume));last=glyph.At;
        }
        return result;
    }
    static float[] Decode(byte[] wav)
    {
        using var stream=new MemoryStream(wav);using var reader=new BinaryReader(stream);
        if(new string(reader.ReadChars(4))!="RIFF")throw new InvalidDataException("Bip sin cabecera WAV.");
        reader.ReadInt32();if(new string(reader.ReadChars(4))!="WAVE")throw new InvalidDataException("Bip WAV no válido.");
        short channels=0,bits=0,format=0;int rate=0;byte[]? data=null;
        while(stream.Position+8<=stream.Length)
        {
            string name=new(reader.ReadChars(4));int size=reader.ReadInt32();long next=stream.Position+size+(size&1);
            if(size<0 || next>stream.Length)throw new InvalidDataException("Bloque WAV fuera del archivo.");
            if(name=="fmt ") { format=reader.ReadInt16();channels=reader.ReadInt16();rate=reader.ReadInt32();reader.ReadInt32();reader.ReadInt16();bits=reader.ReadInt16(); }
            else if(name=="data")data=reader.ReadBytes(size);
            stream.Position=next;
        }
        if(format!=1 || bits!=16 || rate!=Rate || channels<1 || data==null)throw new InvalidDataException("Formato del bip no compatible.");
        int count=data.Length/(channels*2);var output=new float[count];
        for(int i=0;i<count;i++)for(int c=0;c<channels;c++)output[i]+=BitConverter.ToInt16(data,(i*channels+c)*2)/(32768f*channels);
        return output;
    }
    public static void Write(string path,DialogueProject project,DialogueTimeline timeline,Library library,double duration,CancellationToken cancel=default)
    {
        int samples=checked((int)Math.Ceiling(duration*Rate));var events=Events(project,timeline,library);
        using var stream=File.Create(path);using var writer=new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());writer.Write(36+samples*2);writer.Write("WAVEfmt "u8.ToArray());writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(Rate);writer.Write(Rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8.ToArray());writer.Write(samples*2);
        var active=new List<(int Start,float[] Sound,double Volume)>();int index=0;
        for(int offset=0;offset<samples;offset+=4096)
        {
            cancel.ThrowIfCancellationRequested();int size=Math.Min(4096,samples-offset);var block=new float[size];
            while(index<events.Count && events[index].At*Rate<offset+size)
            {
                var e=events[index++];active.Add(((int)Math.Round(e.At*Rate),e.Voice==0?First.Value:Second.Value,e.Volume));
            }
            foreach(var item in active)
            {
                int start=Math.Max(0,item.Start-offset),end=Math.Min(size,item.Start+item.Sound.Length-offset);
                for(int i=start;i<end;i++)block[i]+=item.Sound[offset+i-item.Start]*(float)item.Volume;
            }
            foreach(var voice in timeline.Recordings)
            {
                int a=Math.Max(0,(int)Math.Ceiling(voice.Start*Rate)-offset),b=Math.Min(size,(int)Math.Ceiling(voice.End*Rate)-offset);
                for(int i=a;i<b;i++)
                {
                    double source=voice.SourceOffset+(offset+i)/(double)Rate-voice.Start;
                    block[i]+=voice.Voice.SampleAt(source)*(float)voice.Voice.Volume;
                }
            }
            active.RemoveAll(a=>a.Start+a.Sound.Length<=offset+size);
            for(int i=0;i<size;i++)writer.Write((short)Math.Clamp((int)Math.Round(block[i]*32767),short.MinValue,short.MaxValue));
        }
    }
}
