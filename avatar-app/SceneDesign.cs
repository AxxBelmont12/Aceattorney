using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AvatarStudio;

public sealed record ImageAsset
{
    public string Name {get;init;}="Imagen";
    public byte[] Png {get;init;}=Array.Empty<byte>();
    BitmapSource? bitmap;
    [JsonIgnore] public BitmapSource Bitmap
    {
        get{if(bitmap!=null)return bitmap;using var stream=new MemoryStream(Png);var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return bitmap=image;}
    }
    public void Validate(){if(Png.Length<24 || Png.Length>12_000_000 || Name.Length>250 || !Png.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new InvalidDataException("Imagen PNG no válida o demasiado grande.");int w=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(Png.AsSpan(16,4)),h=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(Png.AsSpan(20,4));if(w<=0 || h<=0 || w>4096 || h>4096 || (long)w*h>8_000_000)throw new InvalidDataException("Dimensiones de imagen fuera de los límites.");}
    public static ImageAsset Import(string path)
    {
        if(new FileInfo(path).Length>30_000_000)throw new InvalidDataException("Usa una imagen de menos de 30 MB.");
        using var stream=File.OpenRead(path);var metadata=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.None).Frames[0];int width=Math.Min(1920,metadata.PixelWidth);if(metadata.PixelWidth<=0 || metadata.PixelHeight/(double)metadata.PixelWidth*width>4096)throw new InvalidDataException("La proporción de la imagen es demasiado alta.");stream.Position=0;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=width;image.StreamSource=stream;image.EndInit();image.Freeze();
        if(image.PixelHeight>4096)throw new InvalidDataException("La imagen es demasiado alta. Usa una proporción más cercana a la pantalla.");
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var output=new MemoryStream();encoder.Save(output);
        var result=new ImageAsset{Name=Path.GetFileName(path),Png=output.ToArray()};result.Validate();return result;
    }
}
public sealed record SceneLayer
{
    public string Id {get;init;}=Guid.NewGuid().ToString("N");
    public string Kind {get;init;}="Cartela";
    public string Text {get;init;}="Tu texto";
    public ImageAsset? Image {get;init;}
    public double X {get;init;}=.75;
    public double Y {get;init;}=.25;
    public double Width {get;init;}=.25;
    public double Opacity {get;init;}=1;
    public double Rotation {get;init;}
    public bool Front {get;init;}=true;
    public override string ToString()=>Kind+" · "+(Image?.Name??Text);
    public void Validate()
    {
        if(!new[]{"Cartela","Sello","Flecha","Marco","Imagen"}.Contains(Kind) || Text.Length>1000 || !double.IsFinite(X+Y+Width+Opacity+Rotation) || X<-.5 || X>1.5 || Y<-.5 || Y>1.5 || Width<.03 || Width>1.5 || Opacity<0 || Opacity>1 || Math.Abs(Rotation)>180)throw new InvalidDataException("Revisa la posición y tamaño del elemento.");
        Image?.Validate();if(Kind=="Imagen" && Image==null)throw new InvalidDataException("Falta la imagen del elemento.");
    }
}
public sealed record SceneDesign
{
    public string Background {get;init;}="Transparente";
    public string Color {get;init;}="#18243B";
    public ImageAsset? Image {get;init;}
    public string Foreground {get;init;}="";
    public bool ShowAvatar {get;init;}=true;
    public bool ShowDialogue {get;init;}=true;
    public string Entry {get;init;}="Corte";
    public string Exit {get;init;}="Corte";
    public double TransitionSeconds {get;init;}=.3;
    public List<SceneLayer> Layers {get;init;}=new();
    public SceneDesign Copy()=>this with {Layers=Layers.ToList()};
    public void Validate()
    {
        if(!new[]{"Transparente","Color","Imagen"}.Contains(Background) && !SceneAssets.Catalog.Any(a=>a.Id==Background && !a.Foreground))throw new InvalidDataException("Fondo desconocido.");
        if(Foreground!="" && !SceneAssets.Catalog.Any(a=>a.Id==Foreground && a.Foreground))throw new InvalidDataException("Estrado desconocido.");
        try{_=ColorConverter.ConvertFromString(Color);}catch{throw new InvalidDataException("Escribe un color como #183052.");}
        if(!Transitions.Contains(Entry) || !Transitions.Contains(Exit) || !double.IsFinite(TransitionSeconds) || TransitionSeconds<.05 || TransitionSeconds>2 || Layers.Count>20)throw new InvalidDataException("Transición o cantidad de elementos no válida.");
        Image?.Validate();if(Background=="Imagen" && Image==null)throw new InvalidDataException("Importa una imagen para ese fondo.");foreach(var layer in Layers)layer.Validate();
    }
    public static readonly string[] Transitions={"Corte","Aparición suave","Desde la izquierda","Desde la derecha"};
}
public sealed record SceneSpan(double Start,double End,double SourceOffset,double SourceDuration,SceneDesign Design);
public sealed record SceneAssetInfo(string Id,string Name,bool Foreground){public override string ToString()=>Name;}
public static class SceneAssets
{
    static readonly Lazy<ZipArchive> Archive=new(()=>new ZipArchive(typeof(SceneAssets).Assembly.GetManifestResourceStream("Scenes")!,ZipArchiveMode.Read));
    static readonly Dictionary<string,BitmapSource> Cache=new();
    static List<SceneAssetInfo>? catalog;
    public static List<SceneAssetInfo> Catalog{get{if(catalog!=null)return catalog;using var stream=Archive.Value.GetEntry("manifest.json")!.Open();return catalog=JsonSerializer.Deserialize<List<SceneAssetInfo>>(stream,AssetStore.Json)!;}}
    public static BitmapSource Image(string id)
    {
        if(Cache.TryGetValue(id,out var found))return found;
        using var source=Archive.Value.GetEntry(id+".png")!.Open();using var stream=new MemoryStream();source.CopyTo(stream);stream.Position=0;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();Cache[id]=image;return image;
    }
}
public static class ScenePainter
{
    public static void Background(DrawingContext dc,SceneDesign scene)
    {
        if(scene.Background=="Color")dc.DrawRectangle(new SolidColorBrush((Color)ColorConverter.ConvertFromString(scene.Color)),null,new Rect(0,0,1280,720));
        else if(scene.Background=="Imagen" && scene.Image!=null)Cover(dc,scene.Image.Bitmap,new Rect(0,0,1280,720));
        else if(scene.Background!="Transparente")dc.DrawImage(SceneAssets.Image(scene.Background),new Rect(0,0,1280,720));
    }
    static void Cover(DrawingContext dc,BitmapSource image,Rect area)
    {double fit=Math.Max(area.Width/image.PixelWidth,area.Height/image.PixelHeight);dc.PushClip(new RectangleGeometry(area));dc.DrawImage(image,new Rect(area.X+(area.Width-image.PixelWidth*fit)/2,area.Y+(area.Height-image.PixelHeight*fit)/2,image.PixelWidth*fit,image.PixelHeight*fit));dc.Pop();}
    public static Rect LayerRect(SceneLayer layer)
    {double width=layer.Width*1280;double ratio=layer.Kind=="Imagen" && layer.Image!=null?layer.Image.Bitmap.PixelHeight/(double)layer.Image.Bitmap.PixelWidth:layer.Kind=="Sello"?.55:.3;return new(layer.X*1280-width/2,layer.Y*720-width*ratio/2,width,width*ratio);}
    public static void Layers(DrawingContext dc,SceneDesign scene,bool front)
    {
        if(front && scene.Foreground!="")dc.DrawImage(SceneAssets.Image(scene.Foreground),new Rect(0,0,1280,720));
        foreach(var layer in scene.Layers.Where(l=>l.Front==front))
        {
            var rect=LayerRect(layer);dc.PushOpacity(layer.Opacity);dc.PushTransform(new RotateTransform(layer.Rotation,layer.X*1280,layer.Y*720));
            var gold=new SolidColorBrush(Color.FromRgb(242,192,77));
            if(layer.Kind=="Imagen")dc.DrawImage(layer.Image!.Bitmap,rect);
            else if(layer.Kind=="Marco")dc.DrawRoundedRectangle(null,new Pen(gold,5),rect,12,12);
            else if(layer.Kind=="Flecha")
            {
                var arrow=new StreamGeometry();using(var g=arrow.Open()){g.BeginFigure(new(rect.Left,rect.Top+rect.Height*.35),true,true);foreach(var p in new[]{new Point(rect.Right-rect.Height*.7,rect.Top+rect.Height*.35),new Point(rect.Right-rect.Height*.7,rect.Top),new Point(rect.Right,rect.Top+rect.Height/2),new Point(rect.Right-rect.Height*.7,rect.Bottom),new Point(rect.Right-rect.Height*.7,rect.Top+rect.Height*.65),new Point(rect.Left,rect.Top+rect.Height*.65)})g.LineTo(p,true,false);}dc.DrawGeometry(gold,null,arrow);
            }
            else
            {
                var color=layer.Kind=="Sello"?Brushes.IndianRed:gold;
                if(layer.Kind=="Sello")dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(170,30,8,14)),new Pen(color,5),new(rect.X+rect.Width/2,rect.Y+rect.Height/2),rect.Width/2,rect.Height/2);
                else dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(225,15,26,48)),new Pen(color,2),rect,8,8);
                var text=new FormattedText(layer.Text,System.Globalization.CultureInfo.GetCultureInfo("es-MX"),FlowDirection.LeftToRight,new Typeface("Segoe UI Semibold"),Math.Clamp(rect.Height*.3,14,42),color,1){MaxTextWidth=Math.Max(10,rect.Width-20),MaxTextHeight=Math.Max(10,rect.Height-8),TextAlignment=TextAlignment.Center,Trimming=TextTrimming.CharacterEllipsis};
                dc.DrawText(text,new Point(rect.X+10,rect.Y+Math.Max(4,(rect.Height-text.Height)/2)));
            }
            dc.Pop();dc.Pop();
        }
    }
    public static (double Opacity,double X) Transition(SceneSpan span,double at)
    {
        var s=span.Design;double local=span.SourceOffset+at-span.Start;double enter=Math.Clamp(local/s.TransitionSeconds,0,1),exit=Math.Clamp((span.SourceDuration-local)/s.TransitionSeconds,0,1);
        double opacity=(s.Entry=="Aparición suave"?enter:1)*(s.Exit=="Aparición suave"?exit:1);
        double x=(s.Entry=="Desde la izquierda"?-1280*(1-enter):s.Entry=="Desde la derecha"?1280*(1-enter):0)+(s.Exit=="Desde la izquierda"?-1280*(1-exit):s.Exit=="Desde la derecha"?1280*(1-exit):0);
        return(opacity,x);
    }
}
