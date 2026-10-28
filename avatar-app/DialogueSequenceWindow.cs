using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AvatarStudio;
public sealed partial class DialogueSequenceWindow : Window
{
    readonly AssetStore assets;
    readonly DialogueProject incoming;
    DialogueSequence sequence=new();
    readonly ListBox track=new(){AllowDrop=true,Background=Brushes.Transparent,Foreground=Brushes.White,BorderThickness=new Thickness(0),Padding=new Thickness(0),Height=150,HorizontalAlignment=HorizontalAlignment.Left};
    readonly TextBox name=new(),start=new(),end=new();
    readonly TextBlock details=new(),clockLabel=new(),status=new();
    readonly ComboBox resolution=new(),fps=new();
    readonly Slider seek=new(),zoom=new(){Minimum=20,Maximum=160,Value=65,Width=150};
    readonly MontageRuler ruler=new();
    readonly ScrollViewer timelineScroll=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
    readonly StackPanel inspector=new();
    readonly WrapPanel toolbar=new();
    readonly Button play=new(){Content="▶ Reproducir montaje"},cancel=new(){Content="Cancelar exportación",Visibility=Visibility.Collapsed};
    readonly SequencePreview preview=new();
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    readonly Stopwatch watch=new();
    readonly MediaPlayer media=new();
    DialogueTimeline? audioTimeline;string? audioPath;
    readonly List<string> temporary=new();
    readonly List<DialogueSequence> undo=new(),redo=new();
    readonly bool testing;
    (DialogueProject Project,DialogueTimeline Timeline)? compiled;
    CancellationTokenSource? playbackCancel,exportCancel;
    double position,startedAt;
    bool playing,updating,busy,closed,closeAfterExport;
    string? dragId;
    Point dragStart;
    static string Draft=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AxxBelmont","AvatarStudio","montage-draft.json");
    sealed class SequencePreview : FrameworkElement
    {
        BitmapSource? image;
        public BitmapSource? Image{get=>image;set{image=value;InvalidateVisual();}}
        protected override Size MeasureOverride(Size s)=>new(0,0);
        protected override void OnRender(DrawingContext dc){if(image==null)return;double scale=Math.Min(ActualWidth/image.PixelWidth,ActualHeight/image.PixelHeight);double w=image.PixelWidth*scale,h=image.PixelHeight*scale;if(w>0 && h>0)dc.DrawImage(image,new Rect((ActualWidth-w)/2,(ActualHeight-h)/2,w,h));}
    }
    DialogueShot? Selected=>sequence.Clips.FirstOrDefault(c=>c.Id==(track.SelectedItem as Card)?.Id);
    public DialogueSequenceWindow(AssetStore assets,DialogueProject incoming,bool testing=false)
    {
        this.assets=assets;this.incoming=DialogueSequence.Clone(incoming);this.testing=testing;
        try{if(!testing && File.Exists(Draft)){var draft=JsonSerializer.Deserialize<DialogueSequence>(File.ReadAllText(Draft),AssetStore.Json)!;draft.Validate(assets.Library);sequence=draft;}}catch{}
        foreach(var clip in sequence.Clips){string actor=assets.Library.Poses.First(p=>p.Id==clip.Project.DefaultPose).Character;if(!sequence.Actors.ContainsKey(actor))sequence.Actors[actor]=ActorPreset.From(clip.Project);}
        Title="Axx Belmont · Editor de video "+AppUpdates.Version;Width=Math.Min(1320,SystemParameters.WorkArea.Width-30);Height=Math.Min(900,SystemParameters.WorkArea.Height-30);MinWidth=1000;MinHeight=700;Background=MainWindow.Navy;Foreground=Brushes.White;FontFamily=new("Segoe UI");FontSize=14;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid{Margin=new Thickness(20),Background=MainWindow.Navy};Content=root;
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        BuildStudioToolbar();root.Children.Add(toolbar);
        var body=new Grid{Margin=new Thickness(0,12,0,8)};body.ColumnDefinitions.Add(new(){Width=new GridLength(350)});body.ColumnDefinitions.Add(new(){Width=new GridLength(16)});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});Grid.SetRow(body,1);root.Children.Add(body);
        body.Children.Add(new ScrollViewer{Content=inspector,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        BuildQuickEditor();
        var advanced=new StackPanel();
        advanced.Children.Add(Text("Nombre del clip",12));advanced.Children.Add(name);advanced.Children.Add(Text("Recortar un clip ya creado · segundos",12,Brushes.Gold));advanced.Children.Add(Text("Inicio",12));advanced.Children.Add(start);advanced.Children.Add(Text("Final (vacío = completo)",12));advanced.Children.Add(end);advanced.Children.Add(Button("Aplicar recorte",(_,_)=>ApplyTrim()));
        advanced.Children.Add(Button("Animaciones por palabras y sonido…",(_,_)=>Edit()));
        inspector.Children.Add(new Expander{Header="Ajustes avanzados del clip",Foreground=Brushes.White,Content=advanced,Margin=new Thickness(0,12,0,8)});
        var move=new WrapPanel();move.Children.Add(Button("← Antes",(_,_)=>Move(-1)));move.Children.Add(Button("Después →",(_,_)=>Move(1)));move.Children.Add(Button("Duplicar",(_,_)=>Duplicate()));var remove=new Button{Content="Eliminar"};StyleButton(remove);remove.Click+=(_,_)=>{quickTimer.Stop();quickDirty=false;Remove();};move.Children.Add(remove);inspector.Children.Add(move);
        resolution.ItemsSource=new[]{"1280 × 720","1920 × 1080"};fps.ItemsSource=new[]{"30 fps","60 fps"};
        var outputOptions=new StackPanel();outputOptions.Children.Add(resolution);outputOptions.Children.Add(fps);inspector.Children.Add(new Expander{Header="Formato de exportación",Foreground=Brushes.White,Content=outputOptions});
        var right=new Grid();Grid.SetColumn(right,2);body.Children.Add(right);right.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});right.RowDefinitions.Add(new(){Height=GridLength.Auto});right.RowDefinitions.Add(new(){Height=GridLength.Auto});
        right.Children.Add(new Border{Background=DialogueWindow.Checker() ,Child=preview,ClipToBounds=true});
        var transport=new StackPanel{Margin=new Thickness(0,8,0,0)};Grid.SetRow(transport,1);right.Children.Add(transport);transport.Children.Add(seek);
        var row=new WrapPanel();StyleButton(play);play.Click+=async(_,_)=>{if(CommitQuick())await TogglePlay();};row.Children.Add(play);row.Children.Add(Button("▶ Solo este clip",async(_,_)=>await TogglePlay(true)));row.Children.Add(repeatPlayback);row.Children.Add(Button("↺ Inicio",(_,_)=>{Pause();position=0;Render();}));clockLabel.Foreground=Brushes.LightSteelBlue;clockLabel.Margin=new Thickness(8,12,0,0);row.Children.Add(clockLabel);transport.Children.Add(row);
        var exportRow=new StackPanel();Grid.SetRow(exportRow,2);right.Children.Add(exportRow);var export=Button("EXPORTAR MONTAJE TRANSPARENTE…",async(_,_)=>await Export());export.Background=Brushes.Gold;export.Foreground=MainWindow.Navy;exportRow.Children.Add(export);exportRow.Children.Add(Button("Exportar por lotes: fragmentos / escenas…",(_,_)=>ShowBatchExport()));StyleButton(cancel);cancel.Click+=(_,_)=>exportCancel?.Cancel();exportRow.Children.Add(cancel);
        var strip=new StackPanel();Grid.SetRow(strip,2);root.Children.Add(strip);var timelineTools=new WrapPanel();timelineTools.Children.Add(Text("LÍNEA DE TIEMPO",13,Brushes.Gold));timelineTools.Children.Add(Text("Zoom",12));timelineTools.Children.Add(zoom);timelineTools.Children.Add(Button("✂ Dividir en el cursor",(_,_)=>SplitAtCursor()));timelineTools.Children.Add(Button("Ajustar vista",(_,_)=>{if(sequence.Clips.Count>0)zoom.Value=Math.Clamp(Math.Max(500,ActualWidth-50)/Compile().Timeline.Duration,20,160);}));strip.Children.Add(timelineTools);
        var panel=new FrameworkElementFactory(typeof(StackPanel));panel.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);track.ItemsPanel=new ItemsPanelTemplate(panel);ScrollViewer.SetHorizontalScrollBarVisibility(track,ScrollBarVisibility.Disabled);ScrollViewer.SetVerticalScrollBarVisibility(track,ScrollBarVisibility.Disabled);
        track.ItemTemplate=TimelineCardTemplate();var timelineContent=new StackPanel{HorizontalAlignment=HorizontalAlignment.Left};timelineContent.Children.Add(ruler);timelineContent.Children.Add(track);timelineScroll.Content=timelineContent;strip.Children.Add(timelineScroll);
        var style=new Style(typeof(ListBoxItem));style.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(27,43,68))));style.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));style.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,4,0,4)));var selectedStyle=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};selectedStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));selectedStyle.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(48,72,110))));selectedStyle.Setters.Add(new Setter(Control.BorderBrushProperty,Brushes.Gold));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty,new System.Windows.Data.Binding("Width")));style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        var cardBorder=new FrameworkElementFactory(typeof(Border));cardBorder.SetValue(Border.BorderThicknessProperty,new Thickness(1));cardBorder.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));cardBorder.SetValue(UIElement.ClipToBoundsProperty,true);
        cardBorder.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)});
        cardBorder.SetBinding(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)});
        var cardContent=new FrameworkElementFactory(typeof(ContentPresenter));cardBorder.AppendChild(cardContent);style.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(ListBoxItem)){VisualTree=cardBorder}));style.Triggers.Add(selectedStyle);style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new System.Windows.Data.Binding("Label")));track.ItemContainerStyle=style;
        zoom.ValueChanged+=(_,_)=>{if(updating)return;double at=position;string? id=Selected?.Id;Refresh(id);position=at;Render();};ruler.Seek+=at=>{if(busy)return;Pause();quickPreviewAtEnd=false;position=at;Render();};
        status.Foreground=Brushes.LightSteelBlue;status.TextWrapping=TextWrapping.Wrap;status.Margin=new Thickness(0,8,0,0);Grid.SetRow(status,3);root.Children.Add(status);
        track.SelectionChanged+=(_,_)=>Select();track.PreviewMouseLeftButtonDown+=(_,e)=>{dragStart=e.GetPosition(track);dragId=null;};
        track.PreviewMouseMove+=(_,e)=>{if(busy || resizingClip || e.LeftButton!=MouseButtonState.Pressed || track.SelectedItem is not Card c)return;var now=e.GetPosition(track);if(Math.Abs(now.X-dragStart.X)>SystemParameters.MinimumHorizontalDragDistance || Math.Abs(now.Y-dragStart.Y)>SystemParameters.MinimumVerticalDragDistance){dragId=c.Id;DragDrop.DoDragDrop(track,new DataObject("AxxAvatarShot",c.Id),DragDropEffects.Move);dragId=null;}};
        track.Drop+=(_,e)=>{if(busy || dragId==null || !e.Data.GetDataPresent("AxxAvatarShot"))return;var item=ItemsControl.ContainerFromElement(track,e.OriginalSource as DependencyObject) as ListBoxItem;int index=item==null?sequence.Clips.Count-1:track.ItemContainerGenerator.IndexFromContainer(item);if(index>=0 && index<sequence.Clips.Count && sequence.Clips.FindIndex(c=>c.Id==dragId)!=index){Remember();sequence.Move(dragId,index);compiled=null;Refresh(dragId);Changed();}e.Handled=true;};
        resolution.SelectionChanged+=(_,_)=>{if(updating)return;Remember();sequence.Width=resolution.SelectedIndex==1?1920:1280;sequence.Height=resolution.SelectedIndex==1?1080:720;Changed();};fps.SelectionChanged+=(_,_)=>{if(updating)return;Remember();sequence.Fps=fps.SelectedIndex==1?60:30;Changed();};
        seek.ValueChanged+=(_,_)=>{if(updating || busy)return;Pause();quickPreviewAtEnd=false;position=seek.Value;Render();};
        timer.Tick+=(_,_)=>PlaybackTick();
        media.MediaOpened+=(_,_)=>{if(closed || playbackCancel?.IsCancellationRequested!=false)return;media.Position=TimeSpan.FromSeconds(position);media.Play();StartPlayback();};
        media.MediaFailed+=(_,_)=>{if(!closed && playbackCancel?.IsCancellationRequested==false){status.Text="La vista previa no pudo abrir el audio. El montaje conserva su sonido al exportar.";StartPlayback();}};
        Closing+=(_,e)=>{if(!CommitQuick()){e.Cancel=true;return;}if(busy){e.Cancel=true;closeAfterExport=true;exportCancel?.Cancel();voiceImportCancel?.Cancel();}};
        Closed+=(_,_)=>{quickTimer.Stop();closed=true;Pause();media.Close();Persist();draftTask.GetAwaiter().GetResult();foreach(string p in temporary)try{File.Delete(p);}catch(IOException){} };
        PreviewKeyDown+=(_,e)=>{if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.Enter){if(CommitQuick())NewClip();e.Handled=true;return;}if(Keyboard.FocusedElement is TextBox)return;if(!CommitQuick()){e.Handled=true;return;}if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.Z){History(false);e.Handled=true;}else if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.Y){History(true);e.Handled=true;}else if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.B){SplitAtCursor();e.Handled=true;}else if(Keyboard.Modifiers==ModifierKeys.None && e.Key==Key.Space && !busy){_ = TogglePlay();e.Handled=true;}};
        status.Text="Azul: escritura o voz · Dorado: lectura. Arrastra el borde derecho de una tarjeta para ajustar su duración.";
        if(sequence.Clips.Count==0){if(string.IsNullOrWhiteSpace(incoming.Text))NewClip();else Add(incoming);}else{Refresh(sequence.Clips[0].Id);Changed();}
    }
    static TextBlock Text(string text,double size=14,Brush? color=null)=>new(){Text=text,FontSize=size,Foreground=color??Brushes.White,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,12,5)};
    static void StyleButton(Button b){b.Padding=new Thickness(10,8,10,8);b.Margin=new Thickness(0,4,6,4);b.Background=new SolidColorBrush(Color.FromRgb(31,47,77));b.Foreground=Brushes.White;b.BorderThickness=new Thickness(0);}
    Button Button(string label,RoutedEventHandler action){var b=new Button{Content=label};StyleButton(b);b.Click+= (sender,e)=>{if(CommitQuick())action(sender,e);};return b;}
    (DialogueProject Project,DialogueTimeline Timeline) Compile()=>compiled??=sequence.Compile(assets.Library);
    void Remember(){undo.Add(DialogueSequence.Clone(sequence));if(undo.Count>30)undo.RemoveAt(0);redo.Clear();}
    void History(bool forward)
    {
        if(busy)return;var source=forward?redo:undo;var target=forward?undo:redo;if(source.Count==0){status.Text="No hay más cambios para "+(forward?"rehacer.":"deshacer.");return;}
        Pause();string? id=Selected?.Id;target.Add(DialogueSequence.Clone(sequence));sequence=source[^1];source.RemoveAt(source.Count-1);compiled=null;Refresh(sequence.Clips.Any(c=>c.Id==id)?id:sequence.Clips.FirstOrDefault()?.Id);Changed();status.Text=forward?"Cambio rehecho.":"Cambio deshecho.";
    }
    void NewClip()
    {
        Pause();var template=Selected?.Project??incoming;
        updating=true;track.SelectedItem=null;updating=false;
        var blank=DialogueSequence.Clone(template);blank.Text="";blank.Ranges.Clear();blank.Recording=null;blank.FitTextToVoice=false;blank.FinalHold=0;blank.SpeechWords.Clear();blank.SyncSpeechWords=false;
        if(Selected==null && sequence.Actors.TryGetValue(assets.Library.Poses.First(p=>p.Id==blank.DefaultPose).Character,out var remembered))remembered.Apply(blank);
        LoadQuick(null,blank);details.Text="Nuevo fragmento · escribe para añadirlo a la secuencia.";quickText.Focus();
    }
    void Add(DialogueProject p){try{p.Validate(assets.Library);if(sequence.Clips.Count>=100)throw new InvalidDataException("Máximo 100 clips.");if(string.IsNullOrWhiteSpace(p.Text))throw new InvalidDataException("El diálogo está vacío.");var shot=new DialogueShot{Project=DialogueSequence.Clone(p),Name=$"Clip {sequence.Clips.Count+1:00}",SceneName=Selected?.SceneName??"Escena 1"};Remember();sequence.Clips.Add(shot);sequence.Actors[assets.Library.Poses.First(x=>x.Id==p.DefaultPose).Character]=ActorPreset.From(p);compiled=null;Refresh(shot.Id);Changed();status.Text="Clip añadido. Edita sus animaciones o arrástralo para cambiar el orden.";}catch(Exception ex){status.Text=ex.Message;}}
    void Refresh(string? id=null){updating=true;double cursor=0;track.ItemsSource=sequence.Clips.Select((c,i)=>{var card=MakeCard(c,i,cursor);cursor+=c.Duration(assets.Library);return card;}).ToList();track.SelectedItem=track.Items.Cast<Card>().FirstOrDefault(c=>c.Id==id);resolution.SelectedIndex=sequence.Width==1920?1:0;fps.SelectedIndex=sequence.Fps==60?1:0;track.Width=Math.Max(1,cursor*zoom.Value);ruler.Update(cursor,zoom.Value,position);updating=false;Select();}
    void Select(){if(updating || quickApplying)return;string? wanted=Selected?.Id;if(!CommitQuick()){updating=true;track.SelectedItem=track.Items.Cast<Card>().FirstOrDefault(c=>c.Id==quickId);updating=false;return;}updating=true;track.SelectedItem=track.Items.Cast<Card>().FirstOrDefault(c=>c.Id==wanted);updating=false;Pause();var shot=Selected;if(shot==null){var blank=DialogueSequence.Clone(incoming);blank.Text="";blank.Ranges.Clear();blank.Recording=null;blank.FitTextToVoice=false;blank.FinalHold=0;blank.SpeechWords.Clear();blank.SyncSpeechWords=false;LoadQuick(null,blank);details.Text="Escribe para crear el primer fragmento.";return;}double full=DialogueCompiler.Compile(shot.Project,assets.Library).Duration;LoadQuick(shot.Id,shot.Project);name.Text=shot.Name;start.Text=shot.In.ToString("0.###",CultureInfo.CurrentCulture);end.Text=shot.Out?.ToString("0.###",CultureInfo.CurrentCulture)??"";details.Text=$"Diálogo original: {full:0.0} s\nRecorte: {(shot.Out??full)-shot.In:0.0} s\n{shot.Project.Ranges.Count} cambio(s) de animación o ritmo";position=sequence.Clips.TakeWhile(c=>c.Id!=shot.Id).Sum(c=>(c.Out??DialogueCompiler.Compile(c.Project,assets.Library).Duration)-c.In);Render();}
    void Changed(){Pause();compiled=null;Persist();try{Render();}catch(Exception ex){status.Text=ex.Message;}}
    void Render(){if(sequence.Clips.Count==0){preview.Image=null;seek.Value=0;clockLabel.Text="Sin clips";return;}var c=Compile();position=Math.Clamp(position,0,c.Timeline.Duration);preview.Image=new DialogueRenderer(assets).Render(c.Project,c.Timeline,position);updating=true;seek.Maximum=c.Timeline.Duration;seek.Value=position;updating=false;ruler.Update(c.Timeline.Duration,zoom.Value,position);clockLabel.Text=$"{position:0.00} / {c.Timeline.Duration:0.0} s · {sequence.Clips.Count} clips";}
    void Pause(){playbackCancel?.Cancel();if(playing)position=startedAt+watch.Elapsed.TotalSeconds;playing=false;watch.Stop();timer.Stop();media.Pause();play.Content="▶ Reproducir montaje";}
    void StartPlayback(){playing=true;startedAt=position;watch.Restart();timer.Start();play.Content="Ⅱ Pausar";}
    async Task TogglePlay(bool selectedOnly=false)
    {
        if(busy)return;
        if(playing || play.Content?.ToString()=="Preparando sonido…"){Pause();return;}
        try
        {
            var c=Compile();playingSelection=selectedOnly;
            if(selectedOnly){var bounds=SelectedBounds();playbackFrom=bounds.Start;playbackTo=bounds.End;position=playbackFrom;quickPreviewAtEnd=false;}
            if(quickPreviewAtEnd){position=sequence.Clips.TakeWhile(x=>x.Id!=quickId).Sum(x=>(x.Out??DialogueCompiler.Compile(x.Project,assets.Library).Duration)-x.In);quickPreviewAtEnd=false;}if(position>=c.Timeline.Duration)position=0;playbackCancel?.Cancel();playbackCancel=new();var token=playbackCancel.Token;
            play.Content="Preparando sonido…";
            if(audioTimeline!=c.Timeline || audioPath==null || !File.Exists(audioPath))
            {
                string wav=Path.Combine(Path.GetTempPath(),"axx-montage-"+Guid.NewGuid().ToString("N")+".wav");temporary.Add(wav);
                await Task.Run(()=>DialogueAudio.Write(wav,c.Project,c.Timeline,assets.Library,c.Timeline.Duration,token),token);
                if(closed || token.IsCancellationRequested)return;audioPath=wav;audioTimeline=c.Timeline;
            }
            if(closed || token.IsCancellationRequested)return;media.Close();media.Open(new Uri(audioPath));
        }
        catch(OperationCanceledException){}catch(Exception ex){Pause();status.Text=ex.Message;}
    }
    void SplitAtCursor()
    {
        if(busy || sequence.Clips.Count==0)return;
        try
        {
            Pause();double cursor=0;DialogueShot? shot=null;double offset=0;
            foreach(var c in sequence.Clips){double length=(c.Out??DialogueCompiler.Compile(c.Project,assets.Library).Duration)-c.In;if(position>=cursor && position<cursor+length){shot=c;offset=position-cursor;break;}cursor+=length;}
            if(shot==null)throw new InvalidDataException("Coloca el cursor dentro de un clip para dividirlo.");
            var candidate=DialogueSequence.Clone(sequence);var right=candidate.Split(shot.Id,offset,assets.Library);candidate.Validate(assets.Library);Remember();sequence=candidate;compiled=null;Refresh(right.Id);Changed();status.Text="Clip dividido. Puedes mover o editar cada parte por separado; la duración total se conserva.";
        }
        catch(Exception ex){status.Text=ex.Message;}
    }
    void Move(int direction){if(Selected is not {} shot)return;int i=sequence.Clips.IndexOf(shot);if(i+direction>=0 && i+direction<sequence.Clips.Count){Remember();sequence.Move(shot.Id,i+direction);compiled=null;Refresh(shot.Id);Changed();}}
    void Duplicate(){if(Selected is not {} shot || sequence.Clips.Count>=100)return;var copy=DialogueSequence.Clone(shot);copy.Id=Guid.NewGuid().ToString("N");copy.Name+=" · copia";Remember();sequence.Clips.Insert(sequence.Clips.IndexOf(shot)+1,copy);compiled=null;Refresh(copy.Id);Changed();}
    void Remove(){if(Selected is not {} shot)return;Remember();sequence.Clips.Remove(shot);compiled=null;Refresh(sequence.Clips.FirstOrDefault()?.Id);Changed();}
    void ApplyTrim(){if(Selected is not {} shot)return;try{if(!Number(start.Text,out double a) || !string.IsNullOrWhiteSpace(end.Text) && !Number(end.Text,out _))throw new InvalidDataException("Escribe los tiempos en segundos, por ejemplo 1,5.");double? b=string.IsNullOrWhiteSpace(end.Text)?null:Number(end.Text,out double value)?value:null;var copy=DialogueSequence.Clone(sequence);var candidate=copy.Clips.First(c=>c.Id==shot.Id);candidate.In=a;candidate.Out=b;candidate.Name=string.IsNullOrWhiteSpace(name.Text)?shot.Name:name.Text.Trim();copy.Validate(assets.Library);Remember();sequence=copy;compiled=null;Refresh(shot.Id);Changed();status.Text="Nombre y recorte guardados.";}catch(Exception ex){status.Text=ex.Message;}}
    static bool Number(string text,out double value)=>double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out value)||double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||(!text.Contains('.') && text.Count(c=>c==',')==1 && double.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out value));
    void Edit(){if(Selected is not {} shot)return;Pause();var editor=new DialogueWindow(assets,shot.Project.DefaultPose,testing:true,initialProject:DialogueSequence.Clone(shot.Project)){Owner=this};editor.ShowDialog();var edited=editor.CurrentProject();try{edited.Validate(assets.Library);if(string.IsNullOrWhiteSpace(edited.Text))throw new InvalidDataException("El clip debe contener texto.");Remember();shot.Project=edited;sequence.Actors[assets.Library.Poses.First(p=>p.Id==edited.DefaultPose).Character]=ActorPreset.From(edited);double duration=DialogueCompiler.Compile(edited,assets.Library).Duration;if(shot.In>=duration-.1)shot.In=0;if(shot.Out>duration || shot.Out<=shot.In)shot.Out=null;compiled=null;Refresh(shot.Id);Changed();}catch(Exception ex){status.Text=ex.Message;}}
    void ImportClip(){var dialog=new OpenFileDialog{Filter="Diálogo de Avatar Studio|*.avdialog.json|JSON|*.json"};if(dialog.ShowDialog(this)!=true)return;try{if(new FileInfo(dialog.FileName).Length>90_000_000)throw new InvalidDataException("Archivo demasiado grande.");Add(JsonSerializer.Deserialize<DialogueProject>(File.ReadAllText(dialog.FileName),AssetStore.Json)!);}catch(Exception ex){status.Text=ex.Message;}}
    void Save(){var dialog=new SaveFileDialog{Filter="Montaje de Avatar Studio|*.avmontage.json",FileName="montaje.avmontage.json"};if(dialog.ShowDialog(this)!=true)return;try{sequence.Validate(assets.Library);string json=JsonSerializer.Serialize(sequence,AssetStore.Json);File.WriteAllText(dialog.FileName,json);ProjectStorage.WriteVersion(sequence,json);status.Text="Montaje guardado con todos sus diálogos y su orden.";}catch(Exception ex){status.Text=ex.Message;}}
    void Open(){var dialog=new OpenFileDialog{Filter="Montaje de Avatar Studio|*.avmontage.json|JSON|*.json"};if(dialog.ShowDialog(this)!=true)return;try{if(new FileInfo(dialog.FileName).Length>200_000_000)throw new InvalidDataException("Archivo demasiado grande.");var loaded=JsonSerializer.Deserialize<DialogueSequence>(File.ReadAllText(dialog.FileName),AssetStore.Json)!;loaded.Validate(assets.Library);Pause();Remember();sequence=loaded;compiled=null;Refresh(sequence.Clips.FirstOrDefault()?.Id);Changed();}catch(Exception ex){status.Text=ex.Message;}}
    async Task Export(){if(busy)return;try{var c=Compile();var dialog=new SaveFileDialog{Filter="MOV transparente|*.mov|WebM transparente|*.webm|MP4 · fondo negro en zonas transparentes|*.mp4",FileName="montaje.mov",AddExtension=true,DefaultExt=".mov"};if(dialog.ShowDialog(this)!=true)return;Pause();busy=true;toolbar.IsEnabled=inspector.IsEnabled=track.IsEnabled=play.IsEnabled=seek.IsEnabled=false;cancel.Visibility=Visibility.Visible;exportCancel=new();var progress=new Progress<(int Current,int Total)>(p=>status.Text=$"Exportando montaje: {p.Current*100/p.Total} %");await DialogueExport.ExportAsync(c.Project,c.Timeline,assets,dialog.FileName,Dispatcher,progress,exportCancel.Token);status.Text="Montaje exportado: "+dialog.FileName;}catch(OperationCanceledException){status.Text="Exportación cancelada. El montaje está guardado.";}catch(Exception ex){status.Text=ex.Message;}finally{busy=false;toolbar.IsEnabled=inspector.IsEnabled=track.IsEnabled=play.IsEnabled=seek.IsEnabled=true;cancel.Visibility=Visibility.Collapsed;exportCancel?.Dispose();exportCancel=null;if(closeAfterExport)Close();}}
    public void VerifyHistory()
    {
        int count=sequence.Clips.Count;Duplicate();if(sequence.Clips.Count!=count+1)throw new InvalidDataException("Duplicate clip");
        Remove();History(false);if(sequence.Clips.Count!=count+1)throw new InvalidDataException("Undo clip removal");
        History(false);if(sequence.Clips.Count!=count)throw new InvalidDataException("Undo clip duplication");
        History(true);if(sequence.Clips.Count!=count+1)throw new InvalidDataException("Redo clip duplication");
        double duration=Compile().Timeline.Duration;position=.5;SplitAtCursor();if(sequence.Clips.Count!=count+2)throw new InvalidDataException("Split UI action");
        History(false);if(sequence.Clips.Count!=count+1 || Math.Abs(Compile().Timeline.Duration-duration)>1e-8)throw new InvalidDataException("Undo split");
        History(true);if(sequence.Clips.Count!=count+2)throw new InvalidDataException("Redo split");
    }
    public void TestSequence(DialogueSequence value,double at){sequence=value;compiled=null;Refresh(sequence.Clips.FirstOrDefault()?.Id);position=at;Render();}
}
