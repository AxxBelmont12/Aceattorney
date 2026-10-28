using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;

namespace AvatarStudio;

public static class DialogueEncoder
{
    static readonly SemaphoreSlim Gate=new(1,1);
    public static async Task<string> PathAsync(CancellationToken cancel=default)
    {
        await Gate.WaitAsync(cancel);
        try
        {
            using var metadata=JsonDocument.Parse(DialogueResources.Read("DialogueEncoderInfo"));
            string hash=metadata.RootElement.GetProperty("sha256").GetString()!;
            string directory=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AxxBelmont","AvatarStudio","video-tools",hash[..16]);
            Directory.CreateDirectory(directory);string path=System.IO.Path.Combine(directory,"ffmpeg.exe");
            if(File.Exists(path))
            {
                await using var existing=File.OpenRead(path);
                if(Convert.ToHexString(await SHA256.HashDataAsync(existing,cancel)).Equals(hash,StringComparison.OrdinalIgnoreCase))return path;
            }
            string temp=path+"."+Guid.NewGuid().ToString("N")+".part";
            try
            {
                using var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("DialogueEncoder")!;
                using var gzip=new GZipStream(source,CompressionMode.Decompress);
                await using(var output=File.Create(temp))await gzip.CopyToAsync(output,cancel);
                await using(var check=File.OpenRead(temp))
                    if(!Convert.ToHexString(await SHA256.HashDataAsync(check,cancel)).Equals(hash,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("No se pudo verificar el componente de video.");
                File.Move(temp,path,true);return path;
            }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
        finally { Gate.Release(); }
    }
}
public static class DialogueExport
{
    public static async Task ExportAsync(DialogueProject project,DialogueTimeline timeline,AssetStore assets,
        string destination,Dispatcher dispatcher,IProgress<(int Current,int Total)>? progress,CancellationToken cancel)
    {
        project.Validate(assets.Library);
        if(string.IsNullOrWhiteSpace(project.Text))throw new InvalidDataException("Escribe un diálogo antes de exportar.");
        bool webm=System.IO.Path.GetExtension(destination).Equals(".webm",StringComparison.OrdinalIgnoreCase);
        bool mp4=System.IO.Path.GetExtension(destination).Equals(".mp4",StringComparison.OrdinalIgnoreCase);
        if(!webm && !mp4 && !System.IO.Path.GetExtension(destination).Equals(".mov",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Usa MOV, WebM o MP4.");
        string encoder=await DialogueEncoder.PathAsync(cancel);
        int frames=checked((int)Math.Ceiling(timeline.Duration*project.Fps));double duration=frames/(double)project.Fps;
        string directory=System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(destination))!;
        Directory.CreateDirectory(directory);
        string temporary=System.IO.Path.Combine(directory,".avatar-render-"+Guid.NewGuid().ToString("N")+(webm?".webm":mp4?".mp4":".mov"));
        string audio=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"axx-avatar-audio-"+Guid.NewGuid().ToString("N")+".wav");
        try
        {
            await Task.Run(()=>DialogueAudio.Write(audio,project,timeline,assets.Library,duration,cancel),cancel);
            await Task.Run(async ()=>
            {
                var start=new ProcessStartInfo(encoder){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardError=true};
                foreach(string value in new[]{"-hide_banner","-loglevel","error","-y","-f","rawvideo","-pixel_format","bgra","-video_size",$"{project.Width}x{project.Height}","-framerate",project.Fps.ToString(CultureInfo.InvariantCulture),"-i","pipe:0","-i",audio,"-map","0:v:0","-map","1:a:0","-threads","2"})start.ArgumentList.Add(value);
                string[] codec=mp4?new[]{"-c:v","libx264","-preset","fast","-crf","18","-pix_fmt","yuv420p","-c:a","aac","-b:a","192k","-movflags","+faststart"}:webm?
                    new[]{"-c:v","libvpx-vp9","-pix_fmt","yuva420p","-b:v","0","-crf","18","-deadline","good","-cpu-used","4","-auto-alt-ref","0","-metadata:s:v:0","alpha_mode=1","-c:a","libopus","-b:a","128k"}:
                    new[]{"-c:v","prores_ks","-profile:v","4","-pix_fmt","yuva444p10le","-alpha_bits","16","-c:a","pcm_s16le"};
                foreach(var value in codec)start.ArgumentList.Add(value);
                foreach(var value in new[]{"-frames:v",frames.ToString(CultureInfo.InvariantCulture),"-t",duration.ToString("0.000000",CultureInfo.InvariantCulture),temporary})start.ArgumentList.Add(value);
                using var process=Process.Start(start) ?? throw new IOException("No se pudo iniciar la exportación.");
                var errors=process.StandardError.ReadToEndAsync();
                using var registration=cancel.Register(()=>{try { if(!process.HasExited)process.Kill(true); } catch(InvalidOperationException){} });
                var renderer=new DialogueRenderer(assets);
                try
                {
                    for(int i=0;i<frames;i++)
                    {
                        cancel.ThrowIfCancellationRequested();double time=i/(double)project.Fps;
                        byte[] pixels=await dispatcher.InvokeAsync(()=>DialogueRenderer.StraightBgra(renderer.Render(project,timeline,time,flattenBlack:mp4)),DispatcherPriority.Background,cancel);
                        await process.StandardInput.BaseStream.WriteAsync(pixels,cancel);
                        if(i%5==0 || i==frames-1)progress?.Report((i+1,frames));
                    }
                    process.StandardInput.Close();await process.WaitForExitAsync(cancel);
                    string error=await errors;
                    if(process.ExitCode!=0)throw new IOException("El componente de video informó un error: "+error[..Math.Min(2000,error.Length)]);
                }
                catch
                {
                    try { if(!process.HasExited)process.Kill(true); } catch(InvalidOperationException){}
                    await process.WaitForExitAsync();await errors;cancel.ThrowIfCancellationRequested();throw;
                }
            },cancel);
            cancel.ThrowIfCancellationRequested();
            if(!File.Exists(temporary) || new FileInfo(temporary).Length==0)throw new IOException("El video exportado está vacío.");
            File.Move(temporary,destination,true);
        }
        finally
        {
            if(File.Exists(audio))File.Delete(audio);
            if(File.Exists(temporary))File.Delete(temporary);
        }
    }
}
