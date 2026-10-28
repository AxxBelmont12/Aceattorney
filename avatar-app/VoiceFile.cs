using System.IO;

namespace AvatarStudio;
public static class VoiceFile
{
    public static void SaveWav(RecordedVoice voice,string path,CancellationToken cancel=default)
    {
        voice.Validate();string temp=path+"."+Guid.NewGuid().ToString("N")+".part";
        try
        {
            int count=(int)Math.Round(voice.Duration*DialogueAudio.Rate);
            using(var stream=File.Create(temp))using(var writer=new BinaryWriter(stream))
            {
                writer.Write("RIFF"u8.ToArray());writer.Write(36+count*2);writer.Write("WAVEfmt "u8.ToArray());writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(DialogueAudio.Rate);writer.Write(DialogueAudio.Rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8.ToArray());writer.Write(count*2);
                for(int i=0;i<count;i++){if(i%4096==0)cancel.ThrowIfCancellationRequested();writer.Write((short)Math.Clamp((int)Math.Round(voice.SampleAt(i/(double)DialogueAudio.Rate)*32768),short.MinValue,short.MaxValue));}
            }
            cancel.ThrowIfCancellationRequested();File.Move(temp,path,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
