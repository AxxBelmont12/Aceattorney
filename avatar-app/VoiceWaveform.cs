using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AvatarStudio;
public sealed class VoiceWaveform:FrameworkElement
{
    RecordedVoice? voice;
    byte[]? pcm;
    float[] peaks=Array.Empty<float>();
    double start,end;
    int dragging;
    public Action<double,double>? TrimCommitted;
    public Action<double>? Seek;
    public VoiceWaveform(){Height=85;Cursor=Cursors.Cross;Focusable=true;ToolTip="Arrastra los límites dorados para recortar; pulsa la onda para escuchar desde ese punto.";}
    public void Set(RecordedVoice? value)
    {voice=value;if(!ReferenceEquals(pcm,value?.Pcm)){pcm=value?.Pcm;peaks=value?.Peaks()??Array.Empty<float>();}start=value?.TrimStart??0;end=value?.TrimEnd??value?.SourceDuration??0;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(16,26,43)),null,new Rect(0,0,ActualWidth,ActualHeight),5,5);
        if(voice==null || ActualWidth<=0)return;
        var pen=new Pen(Brushes.CornflowerBlue,1);double h=ActualHeight-20;
        for(int i=0;i<peaks.Length;i++){double x=i*ActualWidth/peaks.Length,v=Math.Max(.5,peaks[i]*h/2);dc.DrawLine(pen,new(x,ActualHeight/2-v),new(x,ActualHeight/2+v));}
        double a=start/voice.SourceDuration*ActualWidth,b=end/voice.SourceDuration*ActualWidth;
        var shade=new SolidColorBrush(Color.FromArgb(150,0,0,0));dc.DrawRectangle(shade,null,new Rect(0,0,Math.Max(0,a),ActualHeight));dc.DrawRectangle(shade,null,new Rect(b,0,Math.Max(0,ActualWidth-b),ActualHeight));
        foreach(double x in new[]{a,b})dc.DrawRectangle(Brushes.Gold,null,new Rect(Math.Clamp(x-2,0,Math.Max(0,ActualWidth-4)),0,4,ActualHeight));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if(voice==null || ActualWidth<=0)return;double x=e.GetPosition(this).X;
        double a=start/voice.SourceDuration*ActualWidth,b=end/voice.SourceDuration*ActualWidth;
        dragging=Math.Abs(x-a)<12?1:Math.Abs(x-b)<12?2:0;
        if(dragging!=0)CaptureMouse();else Seek?.Invoke(Math.Clamp(x/ActualWidth,0,1)*voice.SourceDuration);e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(dragging==0 || voice==null)return;double at=Math.Clamp(e.GetPosition(this).X/ActualWidth,0,1)*voice.SourceDuration;
        if(dragging==1)start=Math.Min(at,end-.05);else end=Math.Max(at,start+.05);InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){if(dragging==0)return;dragging=0;ReleaseMouseCapture();TrimCommitted?.Invoke(start,end);e.Handled=true;}
    protected override void OnLostMouseCapture(MouseEventArgs e){if(dragging==0)return;dragging=0;Set(voice);}
}
public sealed partial class DialogueSequenceWindow
{
    readonly VoiceWaveform waveform=new();
    readonly System.Windows.Controls.TextBox voiceIn=new(),voiceOut=new(),voiceOffset=new();
    void AddWaveform(System.Windows.Controls.StackPanel panel)
    {
        panel.Children.Add(waveform);panel.Children.Add(Text("Inicio y final del archivo · segundos",12));
        panel.Children.Add(voiceIn);panel.Children.Add(voiceOut);panel.Children.Add(Text("Desplazamiento en el clip · negativo adelanta / positivo retrasa",12));panel.Children.Add(voiceOffset);
        var apply=new System.Windows.Controls.Button{Content="Aplicar recorte y desplazamiento"};StyleButton(apply);apply.Click+=(_,_)=>ApplyVoiceTrim();panel.Children.Add(apply);
        waveform.TrimCommitted=(a,b)=>{voiceIn.Text=a.ToString("0.###");voiceOut.Text=b.ToString("0.###");ApplyVoiceTrim();};
        waveform.Seek=source=>{if(quickTemplate?.Recording is not {} v || Selected is not {} shot)return;Pause();position=ClipStart(shot.Id)+Math.Clamp(source-v.TrimStart+v.Offset-shot.In,0,shot.Duration(assets.Library)-.000001);Render();};
    }
    void RefreshWaveform()
    {var v=quickTemplate?.Recording;waveform.Set(v);voiceIn.Text=(v?.TrimStart??0).ToString("0.###");voiceOut.Text=(v?.TrimEnd??v?.SourceDuration??0).ToString("0.###");voiceOffset.Text=(v?.Offset??0).ToString("0.###");}
    void ApplyVoiceTrim()
    {
        try
        {
            if(quickTemplate?.Recording is not {} v)return;
            if(!Number(voiceIn.Text,out double a) || !Number(voiceOut.Text,out double b) || !Number(voiceOffset.Text,out double offset))throw new System.IO.InvalidDataException("Escribe los tres tiempos en segundos.");
            if(b>v.SourceDuration && b-v.SourceDuration<=.001)b=v.SourceDuration;
            var edited=v with {TrimStart=a,TrimEnd=b,Offset=offset};edited.Validate();quickTemplate.Recording=edited;QueueQuick();CommitQuick();RefreshWaveform();status.Text="Recorte y desplazamiento de voz guardados.";
        }
        catch(Exception ex){RefreshWaveform();status.Text=ex.Message;}
    }
}
