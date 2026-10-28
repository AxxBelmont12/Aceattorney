using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AvatarStudio;

public static class DialogueResources
{
    public static readonly FontFamily Font=Fonts.GetFontFamilies(new Uri("pack://application:,,,/assets/dialogue/")).First(f=>f.FamilyNames.Values.Any(n=>n.Contains("Syntax")));
    public static readonly Typeface Typeface=Font.GetTypefaces().First(t=>t.Style==FontStyles.Normal && t.Weight.ToOpenTypeWeight()==450);
    public static byte[] Read(string name)
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new FileNotFoundException("Falta un recurso de diálogo: "+name);
        using var output=new MemoryStream();stream.CopyTo(output);return output.ToArray();
    }
    public static Brush Color(string name)=>name switch
    {
        "Naranja"=>new SolidColorBrush(System.Windows.Media.Color.FromRgb(255,135,32)),
        "Verde"=>Brushes.LawnGreen,"Azul"=>Brushes.LightSkyBlue,_=>Brushes.White
    };
}
public sealed class DialogueRenderer
{
    readonly AssetStore assets;
    public DialogueRenderer(AssetStore assets)=>this.assets=assets;
    public RenderTargetBitmap Render(DialogueProject project,DialogueTimeline timeline,double seconds,bool avatarOnly=false,bool flattenBlack=false)
    {
        var state=timeline.At(seconds,project);var pose=assets.Library.Poses.First(p=>p.Id==state.Binding.PoseId);
        bool single=pose.Idle==pose.Talk;
        bool active=state.Binding.Mode!="Reposo" && (single || state.Speaking || timeline.RecordedSpeech(seconds)==null && state.Binding.Mode=="Gesto" && state.Visible>0 && !state.PageFinished);
        var clip=pose.Clips[active?pose.Talk:pose.Idle];
        double frame=state.Binding.Mode=="Reposo" && single?0:state.AnimationSeconds*assets.Library.Fps;
        frame=AnimationClock.Frame(clip,frame);
        var cell=clip.At(frame);var sprite=assets.Frame(pose.Frames[cell.Frame]);
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            double scale=project.Width/1280d;dc.PushTransform(new ScaleTransform(scale,scale));
            var sceneSpan=timeline.SceneAt(seconds,project);var scene=sceneSpan.Design;var motion=ScenePainter.Transition(sceneSpan,seconds);
            if(!avatarOnly){if(flattenBlack)dc.DrawRectangle(Brushes.Black,null,new Rect(0,0,1280,720));ScenePainter.Background(dc,scene);ScenePainter.Layers(dc,scene,false);}
            var stage=new Rect(128,8,1024,640);
            double fit=Math.Min(stage.Width/sprite.PixelWidth,stage.Height/sprite.PixelHeight)*(state.Binding.AvatarScale??project.AvatarScale);
            double w=sprite.PixelWidth*fit,h=sprite.PixelHeight*fit;
            var rect=new Rect(stage.X+(stage.Width-w)/2+(state.Binding.AvatarX??project.AvatarX)*1280,stage.Y+(stage.Height-h)/2+(state.Binding.AvatarY??project.AvatarY)*720,w,h);
            var group=new TransformGroup();
            group.Children.Add(new ScaleTransform(cell.Sx*((state.Binding.Mirror??project.Mirror)?-1:1),cell.Sy,rect.X+w/2,rect.Y+h/2));
            group.Children.Add(new RotateTransform(cell.Rotation,rect.X+w/2,rect.Y+h/2));
            group.Children.Add(new TranslateTransform(cell.X*3*fit,cell.Y*3*fit));
            group.Children.Add(new TranslateTransform(motion.X,0));
            dc.PushOpacity(motion.Opacity);dc.PushTransform(group);if(scene.ShowAvatar)dc.DrawImage(sprite,rect);dc.Pop();dc.Pop();
            if(!avatarOnly)ScenePainter.Layers(dc,scene,true);
            if(!avatarOnly && scene.ShowDialogue)
            {
            var panel=new Rect(40,520,1200,180);
            dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(210,8,10,17)),new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(185,225,225,236)),1.5),panel);
            var nameplate=new StreamGeometry();using(var g=nameplate.Open())
            {
                g.BeginFigure(new Point(40,474),true,true);g.LineTo(new Point(354,474),true,false);g.LineTo(new Point(384,520),true,false);g.LineTo(new Point(40,520),true,false);
            }
            dc.DrawGeometry(new SolidColorBrush(System.Windows.Media.Color.FromArgb(242,56,40,114)),null,nameplate);
            string speaker=string.IsNullOrWhiteSpace(state.Binding.Speaker)?pose.Character:state.Binding.Speaker;
            var name=new FormattedText(speaker,System.Globalization.CultureInfo.GetCultureInfo("es-MX"),FlowDirection.LeftToRight,DialogueResources.Typeface,28,Brushes.White,1)
                {MaxTextWidth=280,MaxLineCount=1,Trimming=TextTrimming.CharacterEllipsis};
            dc.DrawText(name,new Point(60,479));
            if(state.Page.Text.Length>0)
            {
                var text=DialogueCompiler.Format(state.Page.Text);
                // The entire page is laid out before hiding its suffix. Words
                // never jump between lines as additional letters appear.
                foreach(var glyph in state.Page.Glyphs)
                {
                    int length=Math.Min(glyph.RenderEnd,state.Page.Text.Length)-glyph.RenderStart;
                    if(length>0)text.SetForegroundBrush(DialogueResources.Color(glyph.Binding.Color),glyph.RenderStart,length);
                }
                if(state.Visible<state.Page.Text.Length)text.SetForegroundBrush(Brushes.Transparent,state.Visible,state.Page.Text.Length-state.Visible);
                dc.DrawText(text,new Point(70,536));
            }
            if(state.PageFinished && ((int)(seconds*2)%2==0))
            {
                var arrow=new StreamGeometry();using(var g=arrow.Open())
                {g.BeginFigure(new Point(1204,595),true,true);g.LineTo(new Point(1204,623),true,false);g.LineTo(new Point(1220,609),true,false);}
                dc.DrawGeometry(Brushes.White,null,arrow);
            }
            }
            dc.Pop();
        }
        var target=new RenderTargetBitmap(project.Width,project.Height,96,96,PixelFormats.Pbgra32);target.Render(visual);target.Freeze();return target;
    }
    public static byte[] StraightBgra(BitmapSource frame)
    {
        var bytes=new byte[frame.PixelWidth*frame.PixelHeight*4];frame.CopyPixels(bytes,frame.PixelWidth*4,0);
        // WPF draws premultiplied alpha; FFmpeg's bgra input uses straight alpha.
        for(int i=0;i<bytes.Length;i+=4)
        {
            int a=bytes[i+3];
            if(a==0)bytes[i]=bytes[i+1]=bytes[i+2]=0;
            else if(a<255)for(int c=0;c<3;c++)bytes[i+c]=(byte)Math.Min(255,(bytes[i+c]*255+a/2)/a);
        }
        return bytes;
    }
}
