using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AvatarStudio;

public sealed partial class DialogueSequenceWindow
{
    sealed record Card(string Id,string Label,double Width,string Title,string Summary,string Timing,BitmapSource Thumbnail,double WritingWidth,double ReadingWidth)
    {public override string ToString()=>Label;}
    readonly Dictionary<string,BitmapSource> portraits=new();
    bool resizingClip;
    string? resizingId;
    double resizeStart,resizeDelta;

    Card MakeCard(DialogueShot c,int index,double at)
    {
        var timeline=DialogueCompiler.Compile(c.Project,assets.Library);double duration=(c.Out??timeline.Duration)-c.In;
        var pose=assets.Library.Poses.First(p=>p.Id==c.Project.DefaultPose);
        if(!portraits.TryGetValue(pose.Id,out var portrait))
        {
            var sprite=assets.Frame(pose.Frames[pose.Clips[pose.Talk].At(Math.Min(8,pose.Clips[pose.Talk].Total-1)).Frame]);
            var pixels=new byte[sprite.PixelWidth*sprite.PixelHeight*4];sprite.CopyPixels(pixels,sprite.PixelWidth*4,0);
            int x0=sprite.PixelWidth,y0=sprite.PixelHeight,x1=0,y1=0;
            for(int y=0;y<sprite.PixelHeight;y++)for(int x=0;x<sprite.PixelWidth;x++)if(pixels[(y*sprite.PixelWidth+x)*4+3]>30){x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);}
            BitmapSource crop=x1>=x0 && y1>=y0?new CroppedBitmap(sprite,new Int32Rect(x0,y0,x1-x0+1,y1-y0+1)):sprite;
            double fit=Math.Min(130d/crop.PixelWidth,75d/crop.PixelHeight);portrait=new TransformedBitmap(crop,new ScaleTransform(fit,fit));portrait.Freeze();portraits[pose.Id]=portrait;
        }
        double spoken=Math.Clamp(Math.Max(timeline.LastWritten,c.Project.Recording?.Duration??0)-c.In,0,duration);
        string title=$"{index+1:00} · {pose.Character}",text=c.Project.Text.Replace('\r',' ').Replace('\n',' ');
        string timing=$"{duration:0.0} s · lectura {duration-spoken:0.0} s";
        string label=$"{title} · {pose.Name}\n{c.Name} · {at:0.0}–{at+duration:0.0} s\n{timing}\n{text}";
        return new(c.Id,label,duration*zoom.Value,title,text,timing,portrait,spoken*zoom.Value,(duration-spoken)*zoom.Value);
    }
    DataTemplate TimelineCardTemplate()
    {
        var root=new FrameworkElementFactory(typeof(Grid));
        var stack=new FrameworkElementFactory(typeof(StackPanel));stack.SetValue(FrameworkElement.MarginProperty,new Thickness(4));root.AppendChild(stack);
        var image=new FrameworkElementFactory(typeof(Image));image.SetBinding(Image.SourceProperty,new Binding("Thumbnail"));image.SetValue(FrameworkElement.HeightProperty,55d);image.SetValue(Image.StretchProperty,Stretch.Uniform);stack.AppendChild(image);
        void Line(string binding,double size,int height,bool wrap)
        {
            var line=new FrameworkElementFactory(typeof(TextBlock));line.SetBinding(TextBlock.TextProperty,new Binding(binding));line.SetValue(TextBlock.FontSizeProperty,size);line.SetValue(FrameworkElement.HeightProperty,(double)height);line.SetValue(TextBlock.TextWrappingProperty,wrap?TextWrapping.Wrap:TextWrapping.NoWrap);line.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);stack.AppendChild(line);
        }
        Line("Title",11,16,false);Line("Summary",11,30,true);Line("Timing",10,16,false);
        var bar=new FrameworkElementFactory(typeof(StackPanel));bar.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);bar.SetValue(FrameworkElement.MarginProperty,new Thickness(-4,4,-4,0));stack.AppendChild(bar);
        foreach(var part in new[]{("WritingWidth",Brushes.CornflowerBlue),("ReadingWidth",Brushes.Gold)})
        {var section=new FrameworkElementFactory(typeof(Border));section.SetBinding(FrameworkElement.WidthProperty,new Binding(part.Item1));section.SetValue(FrameworkElement.HeightProperty,5d);section.SetValue(Border.BackgroundProperty,part.Item2);bar.AppendChild(section);}
        var thumb=new FrameworkElementFactory(typeof(Thumb));thumb.SetValue(FrameworkElement.WidthProperty,10d);thumb.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Right);thumb.SetValue(Control.BackgroundProperty,new SolidColorBrush(Color.FromArgb(90,225,195,90)));thumb.SetValue(FrameworkElement.CursorProperty,Cursors.SizeWE);thumb.SetValue(FrameworkElement.ToolTipProperty,"Arrastra el borde para ajustar la duración y el tiempo de lectura.");
        var hit=new FrameworkElementFactory(typeof(Grid));hit.SetValue(Panel.BackgroundProperty,Brushes.Transparent);
        var grip=new FrameworkElementFactory(typeof(Border));grip.SetValue(FrameworkElement.WidthProperty,3d);grip.SetValue(FrameworkElement.MarginProperty,new Thickness(0,6,0,6));grip.SetValue(Border.CornerRadiusProperty,new CornerRadius(2));grip.SetValue(Border.BackgroundProperty,Brushes.Gold);hit.AppendChild(grip);
        thumb.SetValue(Control.TemplateProperty,new ControlTemplate(typeof(Thumb)){VisualTree=hit});
        thumb.AddHandler(Thumb.DragStartedEvent,new DragStartedEventHandler((sender,e)=>
        {
            if(busy || sender is not FrameworkElement {DataContext:Card c} || !CommitQuick())return;
            Pause();resizingClip=true;resizingId=c.Id;resizeStart=sequence.Clips.First(x=>x.Id==c.Id).Duration(assets.Library);resizeDelta=0;e.Handled=true;
        }));
        thumb.AddHandler(Thumb.DragDeltaEvent,new DragDeltaEventHandler((sender,e)=>
        {
            if(!resizingClip)return;resizeDelta+=e.HorizontalChange/zoom.Value;var candidate=DialogueSequence.Clone(sequence.Clips.First(c=>c.Id==resizingId));candidate.Resize(resizeStart+resizeDelta,assets.Library);
            var container=track.ItemContainerGenerator.ContainerFromItem(track.Items.Cast<Card>().First(c=>c.Id==resizingId)) as ListBoxItem;
            double duration=candidate.Duration(assets.Library);if(container!=null)container.Width=duration*zoom.Value;
            status.Text=$"Duración: {duration:0.00} s · azul: escritura/voz · dorado: lectura";e.Handled=true;
        }));
        thumb.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((_,e)=>
        {
            if(!resizingClip)return;string? id=resizingId;resizingClip=false;resizingId=null;
            if(!e.Canceled && id!=null)ResizeClip(id,resizeStart+resizeDelta);else Refresh(id);e.Handled=true;
        }));root.AppendChild(thumb);
        return new DataTemplate{VisualTree=root};
    }
    void ResizeClip(string id,double duration)
    {
        try{var candidate=DialogueSequence.Clone(sequence);candidate.Clips.First(c=>c.Id==id).Resize(duration,assets.Library);candidate.Validate(assets.Library);Remember();sequence=candidate;compiled=null;Refresh(id);Changed();status.Text="Duración ajustada. Puedes deshacer el cambio; el borde dorado muestra el tiempo de lectura.";}
        catch(Exception ex){Refresh(id);status.Text=ex.Message;}
    }
}
