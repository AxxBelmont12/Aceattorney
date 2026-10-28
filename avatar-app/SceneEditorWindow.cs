using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AvatarStudio;
public sealed class SceneEditorWindow:Window
{
    readonly AssetStore assets;readonly DialogueProject project;
    SceneDesign scene;
    readonly PreviewPane preview=new();readonly ListBox layers=new(){Height=115};
    readonly TextBox text=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=65,MaxLength=1000};
    readonly Slider x=new(){Minimum=-.5,Maximum=1.5},y=new(){Minimum=-.5,Maximum=1.5},size=new(){Minimum=.03,Maximum=1.5},opacity=new(){Minimum=0,Maximum=1},rotation=new(){Minimum=-180,Maximum=180};
    readonly CheckBox front=new(){Content="Delante del personaje",Foreground=Brushes.White};
    readonly TextBlock values=new(){Foreground=Brushes.Gold,TextWrapping=TextWrapping.Wrap},status=new(){Foreground=Brushes.LightSteelBlue,TextWrapping=TextWrapping.Wrap};
    bool loading;string? selectedId;Point? grab;double gx,gy;
    public SceneDesign? Result{get;private set;}
    sealed class PreviewPane:FrameworkElement
    {
        public BitmapSource? Image;public Rect? Outline;
        public void Refresh(){InvalidateVisual();}
        public Point PointAt(Point p){double scale=Math.Min(ActualWidth/1280,ActualHeight/720);return new((p.X-(ActualWidth-1280*scale)/2)/scale,(p.Y-(ActualHeight-720*scale)/2)/scale);}
        protected override Size MeasureOverride(Size s)=>new(0,0);
        protected override void OnRender(DrawingContext dc)
        {if(Image==null)return;double scale=Math.Min(ActualWidth/1280,ActualHeight/720);dc.PushTransform(new TranslateTransform((ActualWidth-1280*scale)/2,(ActualHeight-720*scale)/2));dc.PushTransform(new ScaleTransform(scale,scale));dc.DrawImage(Image,new Rect(0,0,1280,720));if(Outline is {} rect)dc.DrawRectangle(null,new Pen(Brushes.Gold,2){DashStyle=DashStyles.Dash},rect);dc.Pop();dc.Pop();}
    }
    public SceneEditorWindow(AssetStore assets,DialogueProject p)
    {
        this.assets=assets;project=p.Copy();project.Width=1280;project.Height=720;scene=p.Scene.Copy();
        Title="Fondos, elementos y transiciones";Width=1180;Height=820;MinWidth=950;MinHeight=650;Background=MainWindow.Navy;Foreground=Brushes.White;FontFamily=new("Segoe UI");WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid{Margin=new Thickness(18),Background=MainWindow.Navy};root.ColumnDefinitions.Add(new(){Width=new GridLength(360)});root.ColumnDefinitions.Add(new(){Width=new GridLength(14)});root.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});Content=root;
        var tabs=new TabControl{Background=Brushes.Transparent};root.Children.Add(tabs);
        var bg=Panel(tabs,"Fondo");var choices=new List<SceneAssetInfo>{new("Transparente","Transparente",false),new("Color","Color liso",false),new("Imagen","Mi imagen",false)};choices.AddRange(SceneAssets.Catalog.Where(c=>!c.Foreground));
        bg.Children.Add(Label("Fondo del fragmento"));var background=new ComboBox{ItemsSource=choices,SelectedItem=choices.First(c=>c.Id==scene.Background),Foreground=MainWindow.Navy};bg.Children.Add(background);
        background.SelectionChanged+=(_,_)=>{if(background.SelectedItem is not SceneAssetInfo item)return;if(item.Id=="Imagen" && scene.Image==null){status.Text="Pulsa Importar fondo para elegir tu imagen.";return;}scene=scene with {Background=item.Id};Render();};
        var color=new TextBox{Text=scene.Color,Margin=new Thickness(0,8,0,4)};bg.Children.Add(Label("Color · por ejemplo #18243B"));bg.Children.Add(color);bg.Children.Add(Action("Aplicar color",()=>{try{_=ColorConverter.ConvertFromString(color.Text);scene=scene with {Background="Color",Color=color.Text};background.SelectedItem=choices.First(c=>c.Id=="Color");Render();}catch{status.Text="Escribe un color válido, como #18243B.";}}));
        bg.Children.Add(Action("Importar fondo PNG / JPG…",()=>{var image=PickImage();if(image==null)return;scene=scene with {Background="Imagen",Image=image};background.SelectedItem=choices.First(c=>c.Id=="Imagen");Render();}));
        bg.Children.Add(Label("Mesa o estrado delante del personaje"));var desks=new List<SceneAssetInfo>{new("","Sin estrado",true)};desks.AddRange(SceneAssets.Catalog.Where(c=>c.Foreground));var desk=new ComboBox{ItemsSource=desks,SelectedItem=desks.First(c=>c.Id==scene.Foreground),Foreground=MainWindow.Navy};bg.Children.Add(desk);desk.SelectionChanged+=(_,_)=>{if(desk.SelectedItem is SceneAssetInfo d){scene=scene with {Foreground=d.Id};Render();}};
        CheckBox avatar=new CheckBox{Content="Mostrar personaje",IsChecked=scene.ShowAvatar,Foreground=Brushes.White,Margin=new Thickness(0,15,0,8)},dialogue=new CheckBox{Content="Mostrar cuadro de diálogo",IsChecked=scene.ShowDialogue,Foreground=Brushes.White};bg.Children.Add(avatar);bg.Children.Add(dialogue);avatar.Click+=(_,_)=>{scene=scene with {ShowAvatar=avatar.IsChecked==true};Render();};dialogue.Click+=(_,_)=>{scene=scene with {ShowDialogue=dialogue.IsChecked==true};Render();};
        bg.Children.Add(Label("Las imágenes importadas se guardan dentro del proyecto. Los fondos se ajustan para cubrir la pantalla conservando su proporción."));
        var elements=Panel(tabs,"Elementos");elements.Children.Add(layers);var kind=new ComboBox{ItemsSource=new[]{"Cartela","Sello","Flecha","Marco","Imagen"},SelectedIndex=0,Foreground=MainWindow.Navy};elements.Children.Add(kind);
        elements.Children.Add(Action("＋ Añadir elemento",()=>{if(scene.Layers.Count>=20){status.Text="Máximo 20 elementos por fragmento.";return;}string k=(string)kind.SelectedItem;ImageAsset? image=k=="Imagen"?PickImage():null;if(k=="Imagen" && image==null)return;var item=new SceneLayer{Kind=k,Image=image,Text=k=="Sello"?"PRUEBA":"Tu texto"};scene=scene with {Layers=scene.Layers.Append(item).ToList()};LoadLayers(item.Id);Render();}));
        elements.Children.Add(Action("Duplicar elemento",()=>{if(SelectedLayer is not {} l || scene.Layers.Count>=20)return;var copy=l with {Id=Guid.NewGuid().ToString("N"),X=Math.Clamp(l.X+.04,-.5,1.5)};scene=scene with {Layers=scene.Layers.Append(copy).ToList()};LoadLayers(copy.Id);Render();}));
        elements.Children.Add(Action("Quitar elemento",()=>{scene=scene with {Layers=scene.Layers.Where(l=>l.Id!=selectedId).ToList()};LoadLayers(scene.Layers.FirstOrDefault()?.Id);Render();}));
        elements.Children.Add(Label("Texto del elemento"));elements.Children.Add(text);elements.Children.Add(front);elements.Children.Add(values);
        foreach(var pair in new[]{("Horizontal",x),("Vertical",y),("Tamaño",size),("Opacidad",opacity),("Giro",rotation)}){elements.Children.Add(Label(pair.Item1));elements.Children.Add(pair.Item2);pair.Item2.ValueChanged+=(_,_)=>EditLayer();}
        text.TextChanged+=(_,_)=>EditLayer();front.Click+=(_,_)=>EditLayer();layers.SelectionChanged+=(_,_)=>{if(loading)return;selectedId=(layers.SelectedItem as SceneLayer)?.Id;LoadLayer();Render();};
        elements.Children.Add(Label("Selecciona un elemento y arrástralo en la vista previa. El orden de la lista es el orden de dibujo."));elements.Children.Add(Action("Traer al frente de su grupo",()=>{if(SelectedLayer is not {} l)return;scene=scene with {Layers=scene.Layers.Where(s=>s.Id!=l.Id).Append(l).ToList()};LoadLayers(l.Id);Render();}));
        var transitions=Panel(tabs,"Transiciones");transitions.Children.Add(Label("Se aplican al personaje. El cuadro de diálogo permanece legible."));
        foreach(bool entry in new[]{true,false}){transitions.Children.Add(Label(entry?"Entrada":"Salida"));var combo=new ComboBox{ItemsSource=SceneDesign.Transitions,SelectedItem=entry?scene.Entry:scene.Exit,Foreground=MainWindow.Navy};transitions.Children.Add(combo);combo.SelectionChanged+=(_,_)=>{scene=entry?scene with {Entry=(string)combo.SelectedItem}:scene with {Exit=(string)combo.SelectedItem};Render(entry ? .12 :Math.Max(0,DialogueCompiler.Compile(project,assets.Library).Duration-.12));};}
        var duration=new Slider{Minimum=.05,Maximum=2,Value=scene.TransitionSeconds};var length=Label($"Duración: {duration.Value:0.00} s");transitions.Children.Add(length);transitions.Children.Add(duration);duration.ValueChanged+=(_,_)=>{scene=scene with {TransitionSeconds=duration.Value};length.Text=$"Duración: {duration.Value:0.00} s";Render(.1);};transitions.Children.Add(Action("Ver inicio de la transición",()=>Render(.1)));transitions.Children.Add(Action("Ver escena completa",()=>Render(1)));
        var stage=new Border{Background=DialogueWindow.Checker(),Child=preview,ClipToBounds=true};Grid.SetColumn(stage,2);root.Children.Add(stage);
        var bottom=new StackPanel{Margin=new Thickness(0,12,0,0)};bottom.Children.Add(status);bottom.Children.Add(Action("Aplicar al fragmento",()=>{try{scene.Validate();Result=scene.Copy();Close();}catch(Exception ex){status.Text=ex.Message;}}));Grid.SetRow(bottom,1);Grid.SetColumnSpan(bottom,3);root.Children.Add(bottom);
        preview.MouseLeftButtonDown+=(_,e)=>{if(SelectedLayer is not {} l)return;var pt=preview.PointAt(e.GetPosition(preview));if(!ScenePainter.LayerRect(l).Contains(pt))return;grab=pt;gx=l.X;gy=l.Y;preview.CaptureMouse();e.Handled=true;};
        preview.MouseMove+=(_,e)=>{if(grab is not {} g)return;var pt=preview.PointAt(e.GetPosition(preview));loading=true;x.Value=Math.Clamp(gx+(pt.X-g.X)/1280,-.5,1.5);y.Value=Math.Clamp(gy+(pt.Y-g.Y)/720,-.5,1.5);loading=false;EditLayer();};preview.MouseLeftButtonUp+=(_,_)=>{grab=null;preview.ReleaseMouseCapture();};preview.LostMouseCapture+=(_,_)=>grab=null;
        LoadLayers(scene.Layers.FirstOrDefault()?.Id);Render();
    }
    SceneLayer? SelectedLayer=>scene.Layers.FirstOrDefault(l=>l.Id==selectedId);
    void LoadLayers(string? id){loading=true;layers.ItemsSource=scene.Layers;layers.SelectedItem=scene.Layers.FirstOrDefault(l=>l.Id==id);selectedId=id;loading=false;LoadLayer();}
    void LoadLayer(){loading=true;if(SelectedLayer is {} l){text.Text=l.Text;x.Value=l.X;y.Value=l.Y;size.Value=l.Width;opacity.Value=l.Opacity;rotation.Value=l.Rotation;front.IsChecked=l.Front;}loading=false;}
    void EditLayer()
    {if(loading || SelectedLayer is not {} l)return;var next=l with {Text=text.Text,X=x.Value,Y=y.Value,Width=size.Value,Opacity=opacity.Value,Rotation=rotation.Value,Front=front.IsChecked==true};scene=scene with {Layers=scene.Layers.Select(s=>s.Id==l.Id?next:s).ToList()};values.Text=$"X {x.Value*100:0}% · Y {y.Value*100:0}% · tamaño {size.Value*100:0}% · giro {rotation.Value:0}°";Render();}
    void Render(double at=.7){try{project.Scene=scene;var timeline=DialogueCompiler.Compile(project,assets.Library);preview.Image=new DialogueRenderer(assets).Render(project,timeline,Math.Min(at,timeline.Duration));preview.Outline=SelectedLayer is {} l?ScenePainter.LayerRect(l):null;preview.Refresh();status.Text="Vista previa · los bordes dorados del elemento no se exportan.";}catch(Exception ex){status.Text=ex.Message;}}
    ImageAsset? PickImage(){var d=new OpenFileDialog{Filter="Imágenes|*.png;*.jpg;*.jpeg;*.bmp"};if(d.ShowDialog(this)!=true)return null;try{return ImageAsset.Import(d.FileName);}catch(Exception ex){status.Text=ex.Message;return null;}}
    static TextBlock Label(string t)=>new(){Text=t,Foreground=Brushes.LightSteelBlue,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,5)};
    static Button Action(string label,Action action){var b=new Button{Content=label,Padding=new Thickness(9),Margin=new Thickness(0,5,0,4),Background=new SolidColorBrush(Color.FromRgb(35,52,82)),Foreground=Brushes.White};b.Click+=(_,_)=>action();return b;}
    static StackPanel Panel(TabControl tabs,string title){var p=new StackPanel{Margin=new Thickness(8)};tabs.Items.Add(new TabItem{Header=title,Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Foreground=MainWindow.Navy});return p;}
}
