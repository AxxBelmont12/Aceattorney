using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AvatarStudio;

public sealed class DialogueWindow : Window
{
    readonly AssetStore assets;
    readonly bool testing;
    DialogueProject project;
    DialogueTimeline? timeline;
    readonly DialogueRenderer renderer;
    readonly TextBox script=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=175,MaxLength=10000,IsInactiveSelectionHighlightEnabled=true};
    readonly TextBox speaker=new(){MaxLength=50};
    readonly ComboBox character=new(),pose=new(),mode=new(),color=new(),voice=new(),resolution=new(),fps=new();
    readonly ListBox ranges=new(){MinHeight=110,Background=Brushes.Transparent,Foreground=Brushes.White,BorderThickness=new Thickness(0)};
    readonly Slider fragmentSpeed=new(){Minimum=5,Maximum=80,Value=30,TickFrequency=1,IsSnapToTickEnabled=true},fragmentPause=new(){Minimum=0,Maximum=10,Value=0,TickFrequency=.1,IsSnapToTickEnabled=true};
    readonly CheckBox customSpeed=new(){Content="Velocidad propia de este fragmento",Foreground=Brushes.White};
    readonly TextBlock fragmentInfo=new(),speedLabel=new(),holdLabel=new(),fragmentSpeedLabel=new(),fragmentPauseLabel=new();
    readonly TextBox fragmentText=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=70,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxLength=10000};
    readonly TabControl workTabs=new(),settingTabs=new();
    string? selectedRangeId;
    sealed class VideoPreview : FrameworkElement
    {
        ImageSource? source;
        public ImageSource? Source { get=>source;set{source=value;InvalidateVisual();} }
        protected override Size MeasureOverride(Size availableSize)=>new(0,0);
        protected override void OnRender(DrawingContext dc)
        {
            if(source==null || ActualWidth<=0 || ActualHeight<=0)return;
            double scale=Math.Min(ActualWidth/source.Width,ActualHeight/source.Height),w=source.Width*scale,h=source.Height*scale;
            dc.DrawImage(source,new Rect((ActualWidth-w)/2,(ActualHeight-h)/2,w,h));
        }
    }
    readonly VideoPreview preview=new();
    readonly TextBlock status=new(),selection=new(),clockLabel=new();
    readonly Slider seek=new(){Minimum=0},speed=new(){Minimum=5,Maximum=80,Value=30,TickFrequency=1,IsSnapToTickEnabled=true},hold=new(){Minimum=.2,Maximum=5,Value=1},volume=new(){Minimum=0,Maximum=1,Value=.45};
    readonly CheckBox mirror=new(){Content="Reflejar personaje",Foreground=Brushes.White};
    readonly Button play=new(){Content="▶ Reproducir"},export=new(){Content="EXPORTAR VIDEO TRANSPARENTE"},cancel=new(){Content="Cancelar exportación",Visibility=Visibility.Collapsed};
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(350)};
    readonly Stopwatch watch=new();
    readonly MediaPlayer media=new();
    readonly DockPanel header;
    readonly List<string> audioFiles=new();
    bool updating,updatingSeek,dirty=true,playing,pendingPlay,audioReady,busy,closeAfterExport;
    int previewRequest;
    CancellationTokenSource? previewCancel;
    double position,startedAt;
    CancellationTokenSource? exportCancel;
    readonly ScrollViewer editorPane;
    readonly StackPanel options;
    static string DraftPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AxxBelmont","AvatarStudio","dialogue-draft.json");

    sealed record RangeItem(DialogueRange Range,string Label) { public override string ToString()=>Label; }
    public DialogueWindow(AssetStore assets,string initialPose,bool testing=false,DialogueProject? initialProject=null)
    {
        this.assets=assets;this.testing=testing;renderer=new(assets);project=initialProject??new(){DefaultPose=initialPose};media.Volume=1;
        try { if(initialProject==null && !testing && File.Exists(DraftPath)) { var saved=JsonSerializer.Deserialize<DialogueProject>(File.ReadAllText(DraftPath),AssetStore.Json)!;saved.Validate(assets.Library);project=saved; } } catch { }
        Title="Axx Belmont · Estudio de diálogos";Width=Math.Min(1380,SystemParameters.WorkArea.Width-30);Height=Math.Min(920,SystemParameters.WorkArea.Height-30);MinWidth=1050;MinHeight=700;
        Background=MainWindow.Navy;Foreground=Brushes.White;FontFamily=new FontFamily("Segoe UI");FontSize=14;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid{Margin=new Thickness(22)};Content=new Border{Background=MainWindow.Navy,Child=root};
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        header=new DockPanel{Margin=new Thickness(0,0,0,16),LastChildFill=true};
        var files=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(files,Dock.Right);
        if(initialProject!=null)files.Children.Add(Action("Guardar clip y volver",(_,_)=>Close()));
        files.Children.Add(Action("Montaje de clips",(_,_)=>{Pause();new DialogueSequenceWindow(assets,Snapshot()){Owner=this}.ShowDialog();}));
        files.Children.Add(Action("Abrir",(_,_)=>OpenProject()));files.Children.Add(Action("Guardar proyecto",(_,_)=>SaveProject()));header.Children.Add(files);
        var heading=new StackPanel();heading.Children.Add(Label("ESTUDIO DE DIÁLOGOS",25,Brushes.Gold));heading.Children.Add(Label("Escribe → organiza los fragmentos → revisa y exporta",12,Brushes.LightSteelBlue));header.Children.Add(heading);root.Children.Add(header);
        var body=new Grid();Grid.SetRow(body,1);root.Children.Add(body);body.ColumnDefinitions.Add(new(){Width=new GridLength(355)});body.ColumnDefinitions.Add(new(){Width=new GridLength(12)});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var left=new StackPanel();editorPane=new ScrollViewer{Content=left,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};body.Children.Add(editorPane);
        StyleTabs(workTabs);left.Children.Add(workTabs);
        var itemStyle=new Style(typeof(ListBoxItem));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10)));itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,3,0,3)));itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));itemStyle.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(23,35,56))));
        var selected=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};selected.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(48,72,110))));selected.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));itemStyle.Triggers.Add(selected);ranges.ItemContainerStyle=itemStyle;
        var itemText=new FrameworkElementFactory(typeof(TextBlock));itemText.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Label"));itemText.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);itemText.SetValue(TextBlock.FontSizeProperty,13d);ranges.ItemTemplate=new DataTemplate{VisualTree=itemText};
        var writing=new StackPanel();AddTab(workTabs,"1  Guion",writing);
        writing.Children.Add(Label("Tu diálogo",18));writing.Children.Add(Label("Una línea en blanco separa las páginas. El texto largo se ajusta automáticamente.",12,Brushes.LightSteelBlue));
        script.Height=250;StyleInput(script);writing.Children.Add(script);selection.TextWrapping=TextWrapping.Wrap;selection.Foreground=Brushes.LightSteelBlue;selection.FontSize=12;selection.Margin=new Thickness(0,8,0,8);writing.Children.Add(selection);
        writing.Children.Add(Action("＋ Crear fragmento con la selección",(_,_)=>{AssignSelection();workTabs.SelectedIndex=1;}));
        writing.Children.Add(Action("Dividir por párrafos",(_,_)=>SplitParagraphs()));
        writing.Children.Add(Label("Los fragmentos cambian la pose o el ritmo en una parte del guion. El resto usa el personaje base.",12,Brushes.LightSteelBlue));
        var fragments=new StackPanel();AddTab(workTabs,"2  Fragmentos",fragments);
        fragments.Children.Add(Label("Secuencia del diálogo",18));fragments.Children.Add(Label("Selecciona una tarjeta para editarla y ver su momento en el video.",12,Brushes.LightSteelBlue));
        ranges.MaxHeight=270;fragments.Children.Add(ranges);fragmentInfo.Foreground=Brushes.Gold;fragmentInfo.TextWrapping=TextWrapping.Wrap;fragments.Children.Add(fragmentInfo);
        StyleInput(fragmentText);fragments.Children.Add(Label("Texto del fragmento",12));fragments.Children.Add(fragmentText);
        fragments.Children.Add(Action("Guardar texto del fragmento",(_,_)=>UpdateFragmentText()));
        fragments.Children.Add(Action("Eliminar fragmento",(_,_)=>{if(ranges.SelectedItem is RangeItem item){project.Ranges.Remove(item.Range);selectedRangeId=null;Changed();RefreshRanges();status.Text="Fragmento eliminado. Su texto permanece en el guion.";}}));
        var right=new Grid();Grid.SetColumn(right,2);body.Children.Add(right);
        right.RowDefinitions.Add(new(){Height=GridLength.Auto});right.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});right.RowDefinitions.Add(new(){Height=GridLength.Auto});right.RowDefinitions.Add(new(){Height=GridLength.Auto});
        right.Children.Add(Label("VISTA PREVIA · el fondo de cuadros es transparente",12,Brushes.LightSteelBlue));
        var stage=new Border{Background=Checker(),Child=preview,CornerRadius=new CornerRadius(8),ClipToBounds=true,MinHeight=0};Grid.SetRow(stage,1);right.Children.Add(stage);
        var playback=new StackPanel{Margin=new Thickness(0,7,0,8)};Grid.SetRow(playback,2);right.Children.Add(playback);playback.Children.Add(seek);
        var buttons=new DockPanel();StyleButton(play);play.Click+=async(_,_)=>await TogglePlayAsync();DockPanel.SetDock(play,Dock.Left);buttons.Children.Add(play);
        var restart=Action("↺ Inicio",(_,_)=>{Pause();position=0;RenderPreview();});DockPanel.SetDock(restart,Dock.Left);buttons.Children.Add(restart);
        clockLabel.HorizontalAlignment=HorizontalAlignment.Right;clockLabel.VerticalAlignment=VerticalAlignment.Center;clockLabel.Foreground=Brushes.LightSteelBlue;buttons.Children.Add(clockLabel);playback.Children.Add(buttons);
        options=new StackPanel();Grid.SetRow(options,3);right.Children.Add(options);StyleTabs(settingTabs);options.Children.Add(settingTabs);
        foreach(var label in new[]{speedLabel,holdLabel,fragmentSpeedLabel,fragmentPauseLabel}){label.Foreground=Brushes.White;label.FontSize=12;label.Margin=new Thickness(0,5,0,3);}
        var actor=new StackPanel();AddTab(settingTabs,"Personaje y pose",actor);
        actor.Children.Add(Label("Selecciona un fragmento para editarlo, o define el personaje base.",12,Brushes.LightSteelBlue));
        character.ItemsSource=assets.Library.Poses.Select(p=>p.Character).Distinct().OrderBy(n=>n).ToList();
        mode.ItemsSource=new[]{"Hablar","Reposo","Gesto"};color.ItemsSource=new[]{"Blanco","Naranja","Verde","Azul"};
        actor.Children.Add(Pair("Personaje",character,"Animación",pose));actor.Children.Add(Pair("Comportamiento",mode,"Color de texto",color));actor.Children.Add(Pair("Nombre visible (opcional)",speaker,"Orientación",mirror));
        var applyRow=new WrapPanel();applyRow.Children.Add(Action("Aplicar al fragmento / selección",(_,_)=>AssignSelection()));applyRow.Children.Add(Action("Definir personaje base",(_,_)=>SetBase()));actor.Children.Add(applyRow);
        var rhythm=new StackPanel();AddTab(settingTabs,"Ritmo y sonido",rhythm);
        var global=new StackPanel();global.Children.Add(Label("Todo el diálogo",15,Brushes.Gold));global.Children.Add(speedLabel);global.Children.Add(speed);global.Children.Add(holdLabel);global.Children.Add(hold);
        var local=new StackPanel();local.Children.Add(Label("Fragmento seleccionado",15,Brushes.Gold));local.Children.Add(customSpeed);local.Children.Add(fragmentSpeedLabel);local.Children.Add(fragmentSpeed);local.Children.Add(fragmentPauseLabel);local.Children.Add(fragmentPause);local.Children.Add(Action("Guardar ritmo del fragmento",(_,_)=>SaveFragmentTiming()));
        rhythm.Children.Add(TwoColumns(global,local));voice.ItemsSource=new[]{"Automático","Original 1","Original 2","Sin sonido"};rhythm.Children.Add(Pair("Sonido de escritura",voice,"Volumen",volume));
        var output=new StackPanel();AddTab(settingTabs,"Exportar video",output);
        resolution.ItemsSource=new[]{"1280 × 720","1920 × 1080"};fps.ItemsSource=new[]{"30 fps","60 fps"};output.Children.Add(Pair("Resolución",resolution,"Fluidez",fps));
        output.Children.Add(Label("MOV con transparencia: opción principal para probar en DaVinci. WebM: alternativa ligera. El formato se elige al guardar.",12,Brushes.LightSteelBlue));
        output.Children.Add(Label("Se exportan el personaje, el cuadro y el sonido. El fondo de cuadros no aparece en el video.",12,Brushes.LightSteelBlue));output.Children.Add(Action("Créditos",(_,_)=>ShowCredits()));
        StyleButton(export);export.Background=Brushes.Gold;export.Foreground=MainWindow.Navy;export.FontWeight=FontWeights.SemiBold;export.Content="EXPORTAR VIDEO…";export.Click+=async(_,_)=>await ExportVideoAsync();options.Children.Add(export);
        StyleButton(cancel);cancel.Click+=(_,_)=>exportCancel?.Cancel();options.Children.Add(cancel);
        status.TextWrapping=TextWrapping.Wrap;status.Foreground=Brushes.LightSteelBlue;status.Margin=new Thickness(0,12,0,0);Grid.SetRow(status,2);root.Children.Add(status);
        customSpeed.Click+=(_,_)=>fragmentSpeed.IsEnabled=customSpeed.IsChecked==true;
        fragmentSpeed.ValueChanged+=(_,_)=>UpdateTimingLabels();fragmentPause.ValueChanged+=(_,_)=>UpdateTimingLabels();

        character.SelectionChanged+=(_,_)=>{if(updating)return;pose.ItemsSource=assets.Library.Poses.Where(p=>p.Character==(string?)character.SelectedItem).ToList();pose.SelectedIndex=0;};
        script.TextChanged+=(_,_)=>
        {
            if(updating)return;DialogueEdits.AdjustRanges(project,project.Text,script.Text);Changed();
        };
        script.SelectionChanged+=(_,_)=>selection.Text=script.SelectionLength>0?$"Selección: {script.SelectedText.Replace('\n',' ').Replace('\r',' ')}":"Selecciona las palabras a las que quieras asignar una animación.";
        ranges.SelectionChanged+=(_,_)=>
        {
            if(updating || ranges.SelectedItem is not RangeItem item)return;
            selectedRangeId=item.Range.Id;script.Select(item.Range.Start,item.Range.End-item.Range.Start);fragmentText.Text=project.Text[item.Range.Start..item.Range.End];
            customSpeed.IsChecked=item.Range.CharactersPerSecond!=null;fragmentSpeed.Value=item.Range.CharactersPerSecond??project.CharactersPerSecond;fragmentSpeed.IsEnabled=customSpeed.IsChecked==true;fragmentPause.Value=item.Range.PauseAfter;UpdateTimingLabels();ChoosePose(item.Range.PoseId);mode.SelectedItem=item.Range.Mode;color.SelectedItem=item.Range.Color;speaker.Text=item.Range.DisplayName;
            Pause();position=EnsureTimeline().TimeFor(item.Range.Start);RenderPreview();
        };
        speed.ValueChanged+=(_,_)=>{if(updating)return;project.SyncSpeechWords=false;project.FitTextToVoice=false;project.CharactersPerSecond=Math.Round(speed.Value);UpdateTimingLabels();Changed();};
        hold.ValueChanged+=(_,_)=>{if(updating)return;project.SyncSpeechWords=false;project.FitTextToVoice=false;project.PageHold=Math.Round(hold.Value,2);UpdateTimingLabels();Changed();};
        volume.ValueChanged+=(_,_)=>{if(updating)return;project.Volume=volume.Value;Changed();};
        voice.SelectionChanged+=(_,_)=>{if(updating)return;project.Voice=(string)voice.SelectedItem;Changed();};
        mirror.Click+=(_,_)=>{project.Mirror=mirror.IsChecked==true;Changed();};
        resolution.SelectionChanged+=(_,_)=>{if(updating)return;project.Width=resolution.SelectedIndex==1?1920:1280;project.Height=resolution.SelectedIndex==1?1080:720;Changed();};
        fps.SelectionChanged+=(_,_)=>{if(updating)return;project.Fps=fps.SelectedIndex==1?60:30;Changed();};
        seek.ValueChanged+=(_,_)=>{if(updatingSeek || updating || busy)return;Pause();position=seek.Value;RenderPreview();};
        timer.Tick+=(_,_)=>{if(!playing)return;position=startedAt+watch.Elapsed.TotalSeconds;RenderPreview();if(position>=EnsureTimeline().Duration)Pause();};
        debounce.Tick+=(_,_)=>{debounce.Stop();try{RefreshRanges();RenderPreview();SaveDraft();}catch(Exception ex){status.Text=ex.Message;}};
        media.MediaOpened+=(_,_)=>{audioReady=true;if(pendingPlay)StartPlayback();};
        media.MediaFailed+=(_,e)=>{audioReady=false;if(pendingPlay)StartPlayback(false);status.Text="No se pudo reproducir el audio de la vista previa. La exportación conserva su pista de sonido. "+e.ErrorException.Message;};
        Closing+=(_,e)=>{if(busy){e.Cancel=true;closeAfterExport=true;exportCancel?.Cancel();}};
        Closed+=(_,_)=>{Pause();timer.Stop();debounce.Stop();media.Close();SaveDraft();foreach(var file in audioFiles)try{if(File.Exists(file))File.Delete(file);}catch(IOException){} };
        ApplyProject();
    }
    static TextBlock Label(string text,double size,Brush? brush=null)=>new(){Text=text,FontSize=size,Foreground=brush??Brushes.White,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,5)};
    static void StyleInput(Control input){input.Background=new SolidColorBrush(Color.FromRgb(23,35,56));input.Foreground=Brushes.White;input.BorderBrush=new SolidColorBrush(Color.FromRgb(65,83,110));input.Padding=new Thickness(10);}
    static void StyleTabs(TabControl tabs){tabs.Background=Brushes.Transparent;tabs.BorderThickness=new Thickness(0);tabs.Foreground=Brushes.White;tabs.Margin=new Thickness(0,0,0,5);}
    static void AddTab(TabControl tabs,string title,StackPanel content){tabs.Items.Add(new TabItem{Header=title,Foreground=MainWindow.Navy,Padding=new Thickness(12,7,12,7),Content=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=title.StartsWith("1 ") || title.StartsWith("2 ")?560:240,Padding=new Thickness(0,8,4,0)}});}
    static Grid TwoColumns(UIElement a,UIElement b){var g=new Grid();g.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});g.ColumnDefinitions.Add(new(){Width=new GridLength(14)});g.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});g.Children.Add(a);Grid.SetColumn(b,2);g.Children.Add(b);return g;}
    static Grid Pair(string left,UIElement a,string right,UIElement b){var x=new StackPanel();x.Children.Add(Label(left,12));x.Children.Add(a);var y=new StackPanel();y.Children.Add(Label(right,12));y.Children.Add(b);return TwoColumns(x,y);}
    static void StyleButton(Button b){b.Padding=new Thickness(10,7,10,7);b.Margin=new Thickness(0,5,6,5);b.Background=new SolidColorBrush(Color.FromRgb(31,47,77));b.Foreground=Brushes.White;b.BorderThickness=new Thickness(0);}
    static Button Action(string label,RoutedEventHandler action){var b=new Button{Content=label};StyleButton(b);b.Click+=action;return b;}
    internal static Brush Checker()
    {
        var d=new DrawingGroup();d.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(39,51,72)),null,new RectangleGeometry(new Rect(0,0,32,32))));
        var lighter=new SolidColorBrush(Color.FromRgb(48,62,84));d.Children.Add(new GeometryDrawing(lighter,null,new RectangleGeometry(new Rect(0,0,16,16))));d.Children.Add(new GeometryDrawing(lighter,null,new RectangleGeometry(new Rect(16,16,16,16))));
        return new DrawingBrush(d){TileMode=TileMode.Tile,Viewport=new Rect(0,0,32,32),ViewportUnits=BrushMappingMode.Absolute};
    }
    void ChoosePose(string id)
    {
        var p=assets.Library.Poses.First(p=>p.Id==id);bool was=updating;updating=true;character.SelectedItem=p.Character;pose.ItemsSource=assets.Library.Poses.Where(x=>x.Character==p.Character).ToList();pose.SelectedItem=p;updating=was;
    }
    void ApplyProject()
    {
        DialogueEdits.NormalizeNewlines(project);
        updating=true;script.Text=project.Text;ChoosePose(project.DefaultPose);mode.SelectedItem=project.DefaultMode;color.SelectedItem=project.DefaultColor;speaker.Text=project.DisplayName;
        speed.Value=project.CharactersPerSecond;hold.Value=project.PageHold;volume.Value=project.Volume;voice.SelectedItem=project.Voice;mirror.IsChecked=project.Mirror;resolution.SelectedIndex=project.Width==1920?1:0;fps.SelectedIndex=project.Fps==60?1:0;updating=false;
        selectedRangeId=null;UpdateTimingLabels();RefreshRanges();position=0;Changed();RenderPreview();status.Text="Empieza en Guion. Crea fragmentos y selecciona cada tarjeta para ajustar personaje, animación y ritmo.";
    }
    void UpdateTimingLabels()
    {
        speedLabel.Text=$"Escritura: {speed.Value:0} caracteres/s";holdLabel.Text=$"Lectura al final de cada página: {hold.Value:0.0} s";
        fragmentSpeedLabel.Text=$"Escritura: {fragmentSpeed.Value:0} caracteres/s";fragmentPauseLabel.Text=$"Pausa al terminar el fragmento: {fragmentPause.Value:0.0} s";
    }
    void RefreshRanges()
    {
        bool was=updating;updating=true;
        var t=DialogueCompiler.Compile(project,assets.Library);int index=0;
        var items=project.Ranges.OrderBy(r=>r.Start).Select(r=>
        {
            var p=assets.Library.Poses.First(x=>x.Id==r.PoseId);string phrase=project.Text[r.Start..r.End].Replace('\r',' ').Replace('\n',' ');
            if(phrase.Length>39)phrase=phrase[..39]+"…";
            var glyphs=t.Pages.SelectMany(pg=>pg.Glyphs).Where(g=>g.Binding.Id==r.Id).ToList();
            double start=glyphs.FirstOrDefault()?.At??0,end=glyphs.LastOrDefault() is {} last?last.At+last.Duration:start;
            return new RangeItem(r,$"{++index:00}   {start:0.0}–{end:0.0} s   ·   {end-start:0.0} s\n{phrase}\n{p.Character} · {p.Name}");
        }).ToList();
        ranges.ItemsSource=items;ranges.SelectedItem=items.FirstOrDefault(i=>i.Range.Id==selectedRangeId);
        fragmentInfo.Text=items.Count==0?"Aún no hay fragmentos. Selecciona palabras en Guion o divide por párrafos.":$"{items.Count} fragmento(s) · {t.Duration:0.0} s en total";
        updating=was;
    }
    void SplitParagraphs()
    {
        if(project.Ranges.Count>0){status.Text="Para dividir por párrafos, elimina primero los fragmentos existentes. Así se conservan tus ajustes.";return;}
        var matches=System.Text.RegularExpressions.Regex.Matches(project.Text,@"[^\r\n]+(?:\r?\n(?!\r?\n)[^\r\n]+)*");
        foreach(System.Text.RegularExpressions.Match match in matches)
        {
            int start=match.Index,end=start+match.Length;while(start<end && char.IsWhiteSpace(project.Text[start]))start++;while(end>start && char.IsWhiteSpace(project.Text[end-1]))end--;
            if(end>start)project.Ranges.Add(new(){Start=start,End=end,PoseId=project.DefaultPose,Mode=project.DefaultMode,Color=project.DefaultColor,DisplayName=project.DisplayName});
        }
        if(project.Ranges.Count>500){project.Ranges.Clear();status.Text="Usa como máximo 500 fragmentos por proyecto.";return;}
        Changed();RefreshRanges();workTabs.SelectedIndex=1;if(ranges.Items.Count>0)ranges.SelectedIndex=0;
        status.Text="Párrafos convertidos en fragmentos. Selecciona uno para ajustar su pose y su ritmo.";
    }
    void UpdateFragmentText()
    {
        var r=project.Ranges.FirstOrDefault(r=>r.Id==selectedRangeId);if(r==null){status.Text="Selecciona primero un fragmento de la lista.";return;}
        string replacement=fragmentText.Text;
        string next=project.Text[..r.Start]+replacement+project.Text[r.End..];
        if(next.Length>10000){status.Text="El guion admite hasta 10 000 caracteres.";return;}
        int start=r.Start;script.Text=next;
        if(replacement.Length>0)script.Select(start,replacement.Length);
        status.Text="Texto actualizado; los fragmentos siguientes conservan sus palabras.";
    }
    void SaveFragmentTiming()
    {
        var r=project.Ranges.FirstOrDefault(r=>r.Id==selectedRangeId);if(r==null){status.Text="Selecciona primero un fragmento en la pestaña Fragmentos.";return;}
        project.SyncSpeechWords=false;project.FitTextToVoice=false;r.CharactersPerSecond=customSpeed.IsChecked==true?fragmentSpeed.Value:null;r.PauseAfter=fragmentPause.Value;
        Changed();RefreshRanges();RenderPreview();status.Text="Ritmo guardado. Los tiempos de la secuencia se recalcularon.";
    }
    void AssignSelection()
    {
        if(script.SelectionLength==0 || pose.SelectedItem is not Pose p){status.Text="Selecciona primero un tramo del diálogo y una pose.";return;}
        var (start,end)=DialogueEdits.Snap(project.Text,script.SelectionStart,script.SelectionLength);
        if(project.Ranges.Any(r=>r.Start<end && start<r.End && !(r.Start==start && r.End==end))){status.Text="La selección se cruza con otro tramo. Elimínalo o ajusta la selección.";return;}
        var range=project.Ranges.FirstOrDefault(r=>r.Start==start && r.End==end) ?? new DialogueRange{Start=start,End=end};
        if(!project.Ranges.Contains(range)){if(project.Ranges.Count>=500){status.Text="Máximo 500 fragmentos por proyecto.";return;}project.Ranges.Add(range);}
        range.PoseId=p.Id;range.Mode=(string?)mode.SelectedItem??"Hablar";range.Color=(string?)color.SelectedItem??"Blanco";range.DisplayName=speaker.Text.Trim();
        selectedRangeId=range.Id;Changed();RefreshRanges();var chosen=ranges.SelectedItem;ranges.SelectedItem=null;ranges.SelectedItem=chosen;position=EnsureTimeline().TimeFor(start);RenderPreview();status.Text="Tramo asignado. La animación se activa al aparecer esas palabras.";
    }
    void SetBase()
    {
        if(pose.SelectedItem is not Pose p)return;project.DefaultPose=p.Id;project.DisplayName=speaker.Text.Trim();project.DefaultMode=(string?)mode.SelectedItem??"Hablar";project.DefaultColor=(string?)color.SelectedItem??"Blanco";
        Changed();RefreshRanges();status.Text="Pose base actualizada. Los tramos que ya asignaste se conservan.";
    }
    void Changed(){Pause();dirty=true;timeline=null;audioReady=false;media.Close();debounce.Stop();debounce.Start();}
    DialogueTimeline EnsureTimeline()=>timeline??=DialogueCompiler.Compile(project,assets.Library);
    void RenderPreview()
    {
        var t=EnsureTimeline();position=Math.Clamp(position,0,t.Duration);preview.Source=renderer.Render(project,t,position);
        updatingSeek=true;seek.Maximum=t.Duration;seek.Value=position;updatingSeek=false;
        clockLabel.Text=$"  {position:0.0} / {t.Duration:0.0} s · {t.Pages.Count} página(s)";
    }
    async Task TogglePlayAsync()
    {
        if(playing || pendingPlay){Pause();return;}
        try
        {
            debounce.Stop();var t=EnsureTimeline();if(position>=t.Duration)position=0;pendingPlay=true;int request=++previewRequest;play.Content="Ⅱ Pausar";
            if(project.Voice=="Sin sonido" || project.Volume==0){StartPlayback(false);return;}
            if(dirty || !audioReady)
            {
                media.Close();audioReady=false;string file=Path.Combine(Path.GetTempPath(),"axx-avatar-preview-"+Guid.NewGuid().ToString("N")+".wav");audioFiles.Add(file);
                previewCancel?.Cancel();previewCancel=new();var token=previewCancel.Token;
                var snapshot=Snapshot();await Task.Run(()=>
                {
                    try{DialogueAudio.Write(file,snapshot,t,assets.Library,Math.Ceiling(t.Duration*project.Fps)/project.Fps,token);}
                    catch{if(File.Exists(file))File.Delete(file);throw;}
                },token);
                if(!pendingPlay || request!=previewRequest || token.IsCancellationRequested)return;dirty=false;media.Open(new Uri(file));
            }
            else StartPlayback();
        }
        catch(OperationCanceledException){}
        catch(Exception ex){Pause();status.Text=ex.Message;}
    }
    void StartPlayback(bool sound=true){if(!pendingPlay)return;pendingPlay=false;playing=true;startedAt=position;watch.Restart();if(sound && audioReady){media.Position=TimeSpan.FromSeconds(position);media.Play();}timer.Start();play.Content="Ⅱ Pausar";}
    void Pause(){previewRequest++;previewCancel?.Cancel();pendingPlay=false;if(playing)position=startedAt+watch.Elapsed.TotalSeconds;playing=false;watch.Stop();timer.Stop();media.Pause();play.Content="▶ Reproducir";}
    DialogueProject Snapshot()=>project.Copy();
    void SaveDraft(){if(testing)return;try{Directory.CreateDirectory(Path.GetDirectoryName(DraftPath)!);File.WriteAllText(DraftPath,JsonSerializer.Serialize(project,AssetStore.Json));}catch(IOException){} }
    void SaveProject()
    {
        try {project.Validate(assets.Library);var dialog=new SaveFileDialog{Filter="Proyecto de diálogo|*.avdialog.json",FileName="dialogo.avdialog.json"};if(dialog.ShowDialog(this)==true){File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(project,AssetStore.Json));status.Text="Proyecto guardado.";}}
        catch(Exception ex){status.Text=ex.Message;}
    }
    void OpenProject()
    {
        var dialog=new OpenFileDialog{Filter="Proyecto de diálogo|*.json"};if(dialog.ShowDialog(this)!=true)return;
        try
        {
            if(new FileInfo(dialog.FileName).Length>90_000_000)throw new InvalidDataException("El proyecto es demasiado grande.");
            var loaded=JsonSerializer.Deserialize<DialogueProject>(File.ReadAllText(dialog.FileName),AssetStore.Json)??throw new InvalidDataException("Proyecto vacío.");loaded.Validate(assets.Library);
            Pause();SaveDraft();if(!testing && File.Exists(DraftPath))File.Copy(DraftPath,DraftPath+".previous",true);project=loaded;ApplyProject();
        }
        catch(Exception ex){status.Text=ex.Message;}
    }
    async Task ExportVideoAsync()
    {
        if(busy)return;
        var dialog=new SaveFileDialog{Filter="MOV transparente · ProRes 4444|*.mov|WebM transparente · VP9|*.webm",FileName="dialogo.mov",DefaultExt=".mov",AddExtension=true};
        if(dialog.ShowDialog(this)!=true)return;
        Pause();debounce.Stop();busy=true;header.IsEnabled=false;editorPane.IsEnabled=false;options.IsEnabled=false;play.IsEnabled=false;seek.IsEnabled=false;cancel.Visibility=Visibility.Visible;cancel.IsEnabled=true;
        // Cancellation remains outside the disabled export options.
        options.IsEnabled=true;foreach(UIElement child in options.Children)child.IsEnabled=ReferenceEquals(child,cancel);
        exportCancel=new();
        try
        {
            var snapshot=Snapshot();var t=DialogueCompiler.Compile(snapshot,assets.Library);status.Text="Preparando el componente de video y el sonido…";
            var progress=new Progress<(int Current,int Total)>(p=>status.Text=$"Exportando: {p.Current} / {p.Total} fotogramas ({p.Current*100/p.Total} %). Puedes cancelar.");
            await DialogueExport.ExportAsync(snapshot,t,assets,dialog.FileName,Dispatcher,progress,exportCancel.Token);
            status.Text="Video exportado con canal alfa y sonido: "+dialog.FileName;
        }
        catch(OperationCanceledException){status.Text="Exportación cancelada. El proyecto se conserva.";}
        catch(Exception ex){status.Text="No se pudo exportar: "+ex.Message;}
        finally
        {
            busy=false;exportCancel.Dispose();exportCancel=null;header.IsEnabled=true;editorPane.IsEnabled=true;foreach(UIElement child in options.Children)child.IsEnabled=true;play.IsEnabled=true;seek.IsEnabled=true;cancel.Visibility=Visibility.Collapsed;
            if(closeAfterExport)Close();
        }
    }
    public void TestProject(DialogueProject value,double at){project=value;ApplyProject();workTabs.SelectedIndex=1;if(ranges.Items.Count>0)ranges.SelectedIndex=0;position=at;RenderPreview();}
    public DialogueProject CurrentProject()=>Snapshot();
    public void TestTab(int index){settingTabs.SelectedIndex=index;}
    public void VerifyFragmentWorkflow()
    {
        project=new(){Text="Primera frase.\r\n\r\nSegunda frase.",DefaultPose="pl0000"};ApplyProject();SplitParagraphs();
        if(project.Ranges.Count!=2 || ranges.SelectedItem is not RangeItem)throw new InvalidDataException("Paragraph fragment creation / selection");
        customSpeed.IsChecked=true;fragmentSpeed.Value=12;fragmentPause.Value=1.5;SaveFragmentTiming();
        if(project.Ranges[0].CharactersPerSecond!=12 || project.Ranges[0].PauseAfter!=1.5)throw new InvalidDataException("Fragment timing editor");
        fragmentText.Text="Primera frase ampliada.";UpdateFragmentText();
        if(project.Text[project.Ranges[1].Start..project.Ranges[1].End]!="Segunda frase.")throw new InvalidDataException("Editing a fragment shifts following fragments");
    }
    void ShowCredits()
    {
        string license=System.Text.Encoding.UTF8.GetString(DialogueResources.Read("DialogueEncoderLicense"));
        var text=new TextBox{Text="Axx Belmont · Avatar Studio\nPersonajes, animaciones y sonidos originales: Capcom.\nFuente del juego: Linotype Syntax Letter Pro.\n\nFFmpeg se ejecuta como un programa separado.\nCompilación y fuentes: https://www.gyan.dev/ffmpeg/builds/\nCódigo fuente de FFmpeg: https://ffmpeg.org/releases/ffmpeg-8.1.2.tar.xz\n\n"+license,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(16)};
        new Window{Owner=this,Title="Créditos",Width=740,Height=550,Content=text,WindowStartupLocation=WindowStartupLocation.CenterOwner}.ShowDialog();
    }
}


