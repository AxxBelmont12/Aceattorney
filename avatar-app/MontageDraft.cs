using System.IO;
using System.Text.Json;

namespace AvatarStudio;

public sealed partial class DialogueSequenceWindow
{
    static readonly SemaphoreSlim draftGate=new(1,1);
    CancellationTokenSource? draftCancel;
    Task draftTask=Task.CompletedTask;
    void Persist()
    {
        if(testing)return;
        // Recordings are immutable and shared by snapshots: typing does not copy PCM.
        var snapshot=DialogueSequence.Clone(sequence);draftCancel?.Cancel();draftCancel=new();var token=draftCancel.Token;
        draftTask=Task.Run(async()=>
        {
            string? temp=null;bool acquired=false;
            try
            {
                await draftGate.WaitAsync(token);acquired=true;token.ThrowIfCancellationRequested();
                string json=JsonSerializer.Serialize(snapshot,AssetStore.Json);token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(Draft)!);temp=Draft+"."+Guid.NewGuid().ToString("N")+".tmp";
                await File.WriteAllTextAsync(temp,json,token);token.ThrowIfCancellationRequested();File.Move(temp,Draft,true);
                if(snapshot.Clips.Count>0 && DateTime.UtcNow-lastVersion>TimeSpan.FromMinutes(2)){ProjectStorage.WriteVersion(snapshot,json);lastVersion=DateTime.UtcNow;}
            }
            catch(OperationCanceledException){}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            {if(!closed)_=Dispatcher.InvokeAsync(()=>status.Text="No se pudo guardar el borrador: "+ex.Message+" Usa Guardar montaje para elegir otra carpeta.");}
            finally{if(temp!=null && File.Exists(temp))try{File.Delete(temp);}catch(IOException){}if(acquired)draftGate.Release();}
        });
    }
}
