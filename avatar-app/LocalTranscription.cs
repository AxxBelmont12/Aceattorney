using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AvatarStudio;
public static class LocalTranscription
{
    public const string ModelHash="60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe";
    public const long ModelSize=147951465;
    public const string ModelUrl="https://huggingface.co/ggerganov/whisper.cpp/resolve/80da2d8bfee42b0e836fc3a9890373e5defc00a6/ggml-base.bin";
    public static string ModelPath=>Path.Combine(ProjectStorage.Root,"speech-models","ggml-base.bin");
    static readonly SemaphoreSlim Gate=new(1,1);
    static string? checkedModel;
    public static async Task<bool> ModelReady(CancellationToken cancel=default)
    {
        if(!File.Exists(ModelPath) || new FileInfo(ModelPath).Length!=ModelSize)return false;
        string key=new FileInfo(ModelPath).LastWriteTimeUtc.Ticks.ToString();if(checkedModel==key)return true;
        await using var stream=File.OpenRead(ModelPath);bool valid=Convert.ToHexString(await SHA256.HashDataAsync(stream,cancel)).Equals(ModelHash,StringComparison.OrdinalIgnoreCase);if(valid)checkedModel=key;return valid;
    }
    public static async Task DownloadModel(IProgress<string>? progress,CancellationToken cancel)
    {
        await Gate.WaitAsync(cancel);string? part=null;
        try
        {
            if(await ModelReady(cancel))return;Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);part=ModelPath+"."+Guid.NewGuid().ToString("N")+".part";
            using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(20)};using var response=await client.GetAsync(ModelUrl,HttpCompletionOption.ResponseHeadersRead,cancel);response.EnsureSuccessStatusCode();
            await using(var input=await response.Content.ReadAsStreamAsync(cancel))await using(var output=File.Create(part))
            {var block=new byte[131072];long total=0;int count;while((count=await input.ReadAsync(block,cancel))>0){total+=count;if(total>ModelSize)throw new InvalidDataException("La descarga del modelo no tiene el tamaño esperado.");await output.WriteAsync(block.AsMemory(0,count),cancel);progress?.Report($"Descargando modelo: {total*100/ModelSize}% · {total/1_000_000} / 148 MB");}}
            progress?.Report("Verificando el modelo…");await VerifyModelFile(part,cancel);File.Move(part,ModelPath,true);checkedModel=null;
        }
        finally{if(part!=null && File.Exists(part))File.Delete(part);Gate.Release();}
    }
    public static async Task InstallModel(string path,CancellationToken cancel=default)
    {
        await VerifyModelFile(path,cancel);if(Path.GetFullPath(path).Equals(Path.GetFullPath(ModelPath),StringComparison.OrdinalIgnoreCase))return;
        await Gate.WaitAsync(cancel);string part=ModelPath+"."+Guid.NewGuid().ToString("N")+".part";
        try{Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);await using(var input=File.OpenRead(path))await using(var output=File.Create(part))await input.CopyToAsync(output,cancel);cancel.ThrowIfCancellationRequested();File.Move(part,ModelPath,true);checkedModel=null;}
        finally{if(File.Exists(part))File.Delete(part);Gate.Release();}
    }
    static async Task VerifyModelFile(string path,CancellationToken cancel)
    {if(new FileInfo(path).Length!=ModelSize)throw new InvalidDataException("Elige el modelo multilingüe ggml-base.bin de 148 MB.");await using var stream=File.OpenRead(path);if(!Convert.ToHexString(await SHA256.HashDataAsync(stream,cancel)).Equals(ModelHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("El archivo no coincide con el modelo esperado. Vuelve a descargarlo.");}
    static async Task<string> Runtime(CancellationToken cancel)
    {
        await Gate.WaitAsync(cancel);
        try
        {
            using var info=JsonDocument.Parse(DialogueResources.Read("SpeechRuntimeInfo"));var root=info.RootElement;string folder=Path.Combine(ProjectStorage.Root,"speech-tools","whisper-"+root.GetProperty("version").GetString());Directory.CreateDirectory(folder);
            using var data=new MemoryStream(DialogueResources.Read("SpeechRuntime"));if(!Convert.ToHexString(SHA256.HashData(data)).Equals(root.GetProperty("archiveSha256").GetString(),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("El motor de transcripción no superó su verificación.");data.Position=0;
            using var archive=new ZipArchive(data,ZipArchiveMode.Read);
            foreach(var item in root.GetProperty("files").EnumerateObject())
            {
                cancel.ThrowIfCancellationRequested();if(item.Name!=Path.GetFileName(item.Name))throw new InvalidDataException("Ruta de motor no válida.");string path=Path.Combine(folder,item.Name);
                if(File.Exists(path)){await using var existing=File.OpenRead(path);if(Convert.ToHexString(await SHA256.HashDataAsync(existing,cancel)).Equals(item.Value.GetString(),StringComparison.OrdinalIgnoreCase))continue;}
                using var input=archive.GetEntry(item.Name)!.Open();string temp=path+".part";try{await using(var output=File.Create(temp))await input.CopyToAsync(output,cancel);cancel.ThrowIfCancellationRequested();File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
            }
            bool avx=System.Runtime.Intrinsics.X86.Avx2.IsSupported && System.Runtime.Intrinsics.X86.Fma.IsSupported && (System.Runtime.Intrinsics.X86.X86Base.CpuId(1,0).Ecx & (1<<29))!=0;
            return Path.Combine(folder,avx?"whisper-cli-avx2.exe":"whisper-cli.exe");
        }
        finally{Gate.Release();}
    }
    public static async Task<TranscriptDocument> Transcribe(RecordedVoice voice,string language,string names,IProgress<string>? progress,CancellationToken cancel)
    {
        voice.Validate();if(voice.Duration<.3)throw new InvalidDataException("Usa una grabación de al menos 0,3 segundos.");if(!new[]{"es","en","auto"}.Contains(language))throw new InvalidDataException("Idioma no válido.");
        if(!await ModelReady(cancel))throw new InvalidDataException("Descarga primero el modelo local o selecciona tu archivo ggml-base.bin.");
        string engine=await Runtime(cancel),temp=Path.Combine(Path.GetTempPath(),"axx-asr-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            string wav=Path.Combine(temp,"audio.wav"),result=Path.Combine(temp,"result");await Task.Run(()=>WriteInput(wav,voice,cancel),cancel);progress?.Report("Reconociendo voz en tu computadora…");
            var start=new ProcessStartInfo(engine){WorkingDirectory=Path.GetDirectoryName(engine)!,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"-m",ModelPath,"-f",wav,"-l",language,"-ng","-t",Math.Clamp(Environment.ProcessorCount/2,1,6).ToString(CultureInfo.InvariantCulture),"-bo","1","-bs","1","-ojf","-of",result,"-ml","1","-sow","-dtw","base","-pp","-sns"})start.ArgumentList.Add(arg);
            if(!string.IsNullOrWhiteSpace(names)){start.ArgumentList.Add("--prompt");start.ArgumentList.Add(names[..Math.Min(600,names.Length)]);}
            using var process=Process.Start(start)??throw new IOException("No se pudo abrir el motor local.");
            try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception){}
            using var registration=cancel.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
            var stdout=process.StandardOutput.ReadToEndAsync();var error=new StringBuilder();
            var stderr=Task.Run(async()=>{string? line;while((line=await process.StandardError.ReadLineAsync())!=null){if(error.Length<16000)error.AppendLine(line);var match=Regex.Match(line,@"progress\s*=\s*(\d+)%");if(match.Success)progress?.Report("Transcribiendo: "+match.Groups[1].Value+"%");}});
            try{await process.WaitForExitAsync(cancel);await stdout;await stderr;}
            catch{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}await process.WaitForExitAsync();await stdout;await stderr;throw;}
            cancel.ThrowIfCancellationRequested();if(process.ExitCode!=0 || !File.Exists(result+".json"))throw new IOException("El motor no pudo transcribir el audio. "+error.ToString()[..Math.Min(800,error.Length)]);
            return TranscriptDocument.Parse(await File.ReadAllTextAsync(result+".json",cancel),voice.Duration);
        }
        finally{foreach(var path in Directory.EnumerateFiles(temp))File.Delete(path);Directory.Delete(temp);}
    }
    static void WriteInput(string path,RecordedVoice voice,CancellationToken cancel)
    {
        const int rate=16000;int count=(int)Math.Ceiling(voice.Duration*rate);using var stream=File.Create(path);using var writer=new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());writer.Write(36+count*2);writer.Write("WAVEfmt "u8.ToArray());writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8.ToArray());writer.Write(count*2);
        double maximum=0;
        for(int i=0;i<count;i++){if(i%4096==0)cancel.ThrowIfCancellationRequested();double at=i/(double)rate;double sample=(voice.SampleAt(at)+voice.SampleAt(at+1d/48000)+voice.SampleAt(at+2d/48000))/3;maximum=Math.Max(maximum,Math.Abs(sample));writer.Write((short)Math.Clamp((int)Math.Round(sample*32768),short.MinValue,short.MaxValue));}
        if(maximum<.002)throw new InvalidDataException("El audio está vacío o demasiado bajo para transcribirlo. Revisa la grabación.");
    }
}
