using System.Diagnostics;
using System.IO;
using System.Text.Json.Serialization;

namespace AvatarStudio;

public sealed record RecordedVoice
{
    public string Name { get; init; }="Mi voz";
    public byte[] Pcm { get; init; }=Array.Empty<byte>();
    public double Volume { get; init; }=1;
    public double Threshold { get; init; }=.015;
    public double TrimStart {get;init;}
    public double? TrimEnd {get;init;}
    public double Offset {get;init;}
    [JsonIgnore] public double SourceDuration=>Pcm.Length/(2d*DialogueAudio.Rate);
    [JsonIgnore] public double TrimLength=>(TrimEnd??SourceDuration)-TrimStart;
    public bool LipSync { get; init; }=true;
    [JsonIgnore] public double Duration=>Offset+TrimLength;
    float[]? envelope;byte[]? envelopePcm;
    public void Validate()
    {
        if(!double.IsFinite(TrimStart+Offset+(TrimEnd??0)) || TrimStart<0 || (TrimEnd??SourceDuration)>SourceDuration || TrimLength<.05-1e-8 || Offset< -30 || Offset>30 || Duration<.05-1e-8)throw new InvalidDataException("Revisa el recorte y desplazamiento de la grabación.");
        if(Pcm.Length<4800 || Pcm.Length>120*DialogueAudio.Rate*2 || Pcm.Length%2!=0 || Name.Length>250 || !double.IsFinite(Volume) || Volume<0 || Volume>1.5 || !double.IsFinite(Threshold) || Threshold<.001 || Threshold>.15)
            throw new InvalidDataException("La grabación debe durar entre 0,05 y 120 segundos y tener volumen y sensibilidad válidos.");
    }
    public bool Speaking(double at)
    {
        at=at-Offset+TrimStart;
        if(at<TrimStart || at>=(TrimEnd??SourceDuration) || Volume==0)return false;
        if(envelope==null || !ReferenceEquals(envelopePcm,Pcm))
        {
            const int block=480;var values=new float[(Pcm.Length/2+block-1)/block];
            for(int n=0;n<values.Length;n++)
            {
                int end=Math.Min(Pcm.Length/2,(n+1)*block);double sum=0;
                for(int i=n*block;i<end;i++){double v=BitConverter.ToInt16(Pcm,i*2)/32768d;sum+=v*v;}
                values[n]=(float)Math.Sqrt(sum/(end-n*block));
            }
            envelope=values;envelopePcm=Pcm;
        }
        int index=(int)(at*100);
        // A short release avoids a mouth flicker at every zero crossing or syllable.
        for(int i=Math.Max(0,index-5);i<=Math.Min(envelope.Length-1,index+1);i++)if(envelope[i]>=Threshold)return true;
        return false;
    }
    public float SampleAt(double at)
    {
        double source=at-Offset+TrimStart;
        if(source<TrimStart || source>=(TrimEnd??SourceDuration))return 0;
        int index=(int)Math.Round(source*DialogueAudio.Rate);return index<0 || index>=Pcm.Length/2?0:BitConverter.ToInt16(Pcm,index*2)/32768f;
    }
    public float[] Peaks(int count=500)
    {
        var peaks=new float[count];int samples=Pcm.Length/2;
        for(int i=0;i<count;i++){int a=(int)((long)i*samples/count),b=(int)((long)(i+1)*samples/count);for(int n=a;n<b;n++)peaks[i]=Math.Max(peaks[i],Math.Abs(BitConverter.ToInt16(Pcm,n*2)/32768f));}return peaks;
    }
    public static async Task<RecordedVoice> ImportAsync(string path,CancellationToken cancel=default)
    {
        if(new FileInfo(path).Length>250_000_000)throw new InvalidDataException("El archivo supera 250 MB. Importa una grabación más corta.");
        var start=new ProcessStartInfo(await DialogueEncoder.PathAsync(cancel)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{"-hide_banner","-loglevel","error","-i",path,"-vn","-t","121","-ac","1","-ar","48000","-f","s16le","pipe:1"})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("No se pudo abrir el componente de audio.");
        using var registration=cancel.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
        var errors=process.StandardError.ReadToEndAsync();using var output=new MemoryStream();
        try{await process.StandardOutput.BaseStream.CopyToAsync(output,cancel);await process.WaitForExitAsync(cancel);}
        catch{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}await process.WaitForExitAsync();throw;}
        string error=await errors;if(process.ExitCode!=0)throw new InvalidDataException("No se pudo leer el audio. "+error[..Math.Min(error.Length,500)]);
        var voice=new RecordedVoice{Name=Path.GetFileName(path),Pcm=output.ToArray()};voice.Validate();return voice;
    }
}
public sealed record VoiceSpan(double Start,double End,double SourceOffset,RecordedVoice Voice);

public sealed record ActorPreset(string PoseId,double X,double Y,double Scale,bool Mirror,double Speed,double Hold,string Mode,string DisplayName)
{
    public static ActorPreset From(DialogueProject p)=>new(p.DefaultPose,p.AvatarX,p.AvatarY,p.AvatarScale,p.Mirror,p.CharactersPerSecond,p.PageHold,p.DefaultMode,p.DisplayName);
    public void Apply(DialogueProject p){p.DefaultPose=PoseId;p.AvatarX=X;p.AvatarY=Y;p.AvatarScale=Scale;p.Mirror=Mirror;p.CharactersPerSecond=Speed;p.PageHold=Hold;p.DefaultMode=Mode;p.DisplayName=DisplayName;}
    public void Validate(Library library){var p=new DialogueProject();Apply(p);p.Validate(library);}
}
