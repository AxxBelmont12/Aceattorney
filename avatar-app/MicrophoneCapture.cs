using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AvatarStudio;
public sealed record MicrophoneDevice(uint Id,string Name){public override string ToString()=>Name;}
public static class MicrophoneCapture
{
    [StructLayout(LayoutKind.Sequential,Pack=2)] struct Format{public ushort Tag,Channels;public uint Rate,BytesPerSecond;public ushort Align,Bits,Extra;}
    [StructLayout(LayoutKind.Sequential)] struct Header{public IntPtr Data;public uint Length,Recorded;public UIntPtr User;public uint Flags,Loops;public IntPtr Next;public UIntPtr Reserved;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Caps{public ushort Mid,Pid;public uint Version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name;public uint Formats;public ushort Channels,Reserved;}
    [DllImport("winmm.dll")] static extern uint waveInGetNumDevs();
    [DllImport("winmm.dll",CharSet=CharSet.Unicode,EntryPoint="waveInGetDevCapsW")] static extern uint GetCaps(UIntPtr device,out Caps caps,uint size);
    [DllImport("winmm.dll")] static extern uint waveInOpen(out IntPtr handle,uint device,ref Format format,IntPtr callback,IntPtr instance,uint flags);
    [DllImport("winmm.dll")] static extern uint waveInPrepareHeader(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveInUnprepareHeader(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveInAddBuffer(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveInStart(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint waveInStop(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint waveInReset(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint waveInClose(IntPtr handle);
    sealed class Buffer{public IntPtr Data,Header;public bool Prepared;}
    public static List<MicrophoneDevice> Devices()
    {
        var list=new List<MicrophoneDevice>{new(uint.MaxValue,"Micrófono predeterminado de Windows")};
        for(uint i=0;i<waveInGetNumDevs();i++)if(GetCaps((UIntPtr)i,out var caps,(uint)Marshal.SizeOf<Caps>())==0)list.Add(new(i,caps.Name));return list;
    }
    static void Require(uint error){if(error!=0)throw new IOException("No se pudo usar el micrófono (código "+error+"). Revisa el dispositivo y el acceso al micrófono en Windows.");}
    public static Task<RecordedVoice> Record(uint device,IProgress<double>? progress,CancellationToken stop)=>Task.Run(()=>
    {
        IntPtr handle=IntPtr.Zero;var buffers=new List<Buffer>();uint headerSize=(uint)Marshal.SizeOf<Header>();using var output=new MemoryStream();int next=0;const int capacity=8192,maximum=120*48000*2;var watch=Stopwatch.StartNew();double last=0;
        void Read(Buffer b)
        {var header=Marshal.PtrToStructure<Header>(b.Header);int size=(int)Math.Min(header.Recorded,capacity);size=Math.Min(size,maximum-(int)output.Length);if(size<=0)return;var data=new byte[size];Marshal.Copy(b.Data,data,0,size);output.Write(data);}
        try
        {
            stop.ThrowIfCancellationRequested();var format=new Format{Tag=1,Channels=1,Rate=48000,BytesPerSecond=96000,Align=2,Bits=16};Require(waveInOpen(out handle,device,ref format,IntPtr.Zero,IntPtr.Zero,0));
            for(int i=0;i<4;i++)
            {
                var b=new Buffer{Data=Marshal.AllocHGlobal(capacity),Header=Marshal.AllocHGlobal((int)headerSize)};buffers.Add(b);var header=new Header{Data=b.Data,Length=capacity};Marshal.StructureToPtr(header,b.Header,false);Require(waveInPrepareHeader(handle,b.Header,headerSize));b.Prepared=true;Require(waveInAddBuffer(handle,b.Header,headerSize));
            }
            Require(waveInStart(handle));
            while(!stop.IsCancellationRequested && output.Length<maximum && watch.Elapsed.TotalSeconds<120)
            {
                while((Marshal.PtrToStructure<Header>(buffers[next].Header).Flags&1)!=0 && output.Length<maximum){Read(buffers[next]);Require(waveInAddBuffer(handle,buffers[next].Header,headerSize));next=(next+1)%buffers.Count;}
                if(watch.Elapsed.TotalSeconds-last>.1){last=watch.Elapsed.TotalSeconds;progress?.Report(last);}Thread.Sleep(10);
            }
            waveInStop(handle);waveInReset(handle);for(int i=0;i<buffers.Count;i++)Read(buffers[(next+i)%buffers.Count]);
            var voice=new RecordedVoice{Name="Grabación "+DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss")+".wav",Pcm=output.ToArray()};if(voice.Pcm.Length<4800)throw new InvalidDataException("La grabación es demasiado corta. Graba al menos un segundo.");voice.Validate();return voice;
        }
        finally
        {
            if(handle!=IntPtr.Zero){waveInStop(handle);waveInReset(handle);}
            foreach(var b in buffers){if(b.Prepared && handle!=IntPtr.Zero)waveInUnprepareHeader(handle,b.Header,headerSize);Marshal.FreeHGlobal(b.Header);Marshal.FreeHGlobal(b.Data);}if(handle!=IntPtr.Zero)waveInClose(handle);
        }
    });
}
