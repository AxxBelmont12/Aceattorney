using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace AvatarStudio;
public sealed record TranscriptionChoice(RecordedVoice Voice,TranscriptDocument Document,bool Fragments,bool Replace);
public sealed class TranscriptionWindow:Window
{
    enum Operation{None,Record,Import,Download,Recognize}
    Operation operation;
    CancellationTokenSource? stop;
    bool closeRequested,modelReady;
    RecordedVoice? audio;
    readonly RecordedVoice? current;
    readonly List<Button> controls=new();
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue},source=new(){Text="Importa una grabación o usa el micrófono.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.White},model=new(){Text="Comprobando modelo local…",Foreground=Brushes.Gold,TextWrapping=TextWrapping.Wrap};
    readonly ComboBox devices=new(){MinWidth=270,Foreground=MainWindow.Navy},language=new(){ItemsSource=new[]{"Español","Detectar idioma","English"},SelectedIndex=0,Foreground=MainWindow.Navy};
    readonly TextBox names=new(){MaxLength=600};
    readonly DataGrid grid=new(){AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=true,CanUserSortColumns=false,SelectionMode=DataGridSelectionMode.Extended,SelectionUnit=DataGridSelectionUnit.FullRow,Background=MainWindow.Navy,Foreground=MainWindow.Navy,MinRowHeight=40,HeadersVisibility=DataGridHeadersVisibility.Column,Margin=new Thickness(0,8,0,8)};
    readonly ObservableCollection<TranscriptPart> rows=new();
    readonly RadioButton onlyText=new(){Content="Transcribir como un diálogo",GroupName="Result",Foreground=Brushes.White},fragments=new(){Content="Crear fragmentos sincronizados",GroupName="Result",Foreground=Brushes.White};
    readonly ComboBox destination=new(){ItemsSource=new[]{"Añadir al montaje","Reemplazar el clip seleccionado"},SelectedIndex=0,Foreground=MainWindow.Navy,MinWidth=250};
    readonly Button record=new(),transcribe=new(),apply=new(),cancel=new(){Content="Cancelar operación",Visibility=Visibility.Collapsed};
    public TranscriptionChoice? Result{get;private set;}
    double duration;
    public TranscriptionWindow(RecordedVoice? current,bool createFragments,bool canReplace,string vocabulary)
    {
        this.current=current;Title="Audio a texto · transcripción local";Width=Math.Min(1120,SystemParameters.WorkArea.Width-30);Height=Math.Min(880,SystemParameters.WorkArea.Height-30);MinWidth=950;MinHeight=700;Background=MainWindow.Navy;Foreground=Brushes.White;FontFamily=new("Segoe UI");FontSize=14;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid{Margin=new Thickness(20),Background=MainWindow.Navy};Content=root;foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})root.RowDefinitions.Add(new(){Height=height});
        var top=new StackPanel();root.Children.Add(top);top.Children.Add(Label("CONVIERTE TU VOZ EN DIÁLOGOS",23,Brushes.Gold));top.Children.Add(source);
        var audioButtons=new WrapPanel();top.Children.Add(audioButtons);devices.ItemsSource=MicrophoneCapture.Devices();devices.SelectedIndex=0;audioButtons.Children.Add(devices);
        StyleButton(record);record.Content="● Grabar micrófono";record.Click+=async(_,_)=>{if(operation==Operation.Record){stop?.Cancel();return;}await Run(Operation.Record,async token=>{var mic=(MicrophoneDevice)devices.SelectedItem;var voice=await MicrophoneCapture.Record(mic.Id,new Progress<double>(s=>status.Text=$"Grabando {s:0.0} / 120 s · pulsa Detener para terminar."),token);SetAudio(voice);});};audioButtons.Children.Add(record);
        audioButtons.Children.Add(Action("Importar audio…",()=>Run(Operation.Import,async token=>{var file=new OpenFileDialog{Filter="Grabaciones|*.wav;*.mp3;*.m4a;*.ogg;*.flac;*.opus;*.aac;*.wma|Todos|*.*"};if(file.ShowDialog(this)!=true)return;SetAudio(await RecordedVoice.ImportAsync(file.FileName,token));})));
        if(current!=null)audioButtons.Children.Add(Action("Usar audio del clip",()=>{SetAudio(current);return Task.CompletedTask;}));
        audioButtons.Children.Add(Action("Guardar WAV…",()=>Run(Operation.Import,async token=>{if(audio==null){status.Text="Graba o importa primero un audio.";return;}var save=new SaveFileDialog{Filter="Audio WAV|*.wav",FileName="mi-grabacion.wav"};if(save.ShowDialog(this)!=true)return;await Task.Run(()=>VoiceFile.SaveWav(audio,save.FileName,token),token);status.Text="Grabación guardada: "+save.FileName;})));
        var settings=new Grid{Margin=new Thickness(0,10,0,5)};settings.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});settings.ColumnDefinitions.Add(new(){Width=new GridLength(18)});settings.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});Grid.SetRow(settings,1);root.Children.Add(settings);
        var left=new StackPanel();settings.Children.Add(left);left.Children.Add(model);var modelButtons=new WrapPanel();left.Children.Add(modelButtons);
        modelButtons.Children.Add(Action("Descargar modelo local · 148 MB",()=>Run(Operation.Download,async token=>{await LocalTranscription.DownloadModel(new Progress<string>(s=>status.Text=s),token);modelReady=await LocalTranscription.ModelReady(token);UpdateModel();})));
        modelButtons.Children.Add(Action("Usar modelo descargado…",()=>Run(Operation.Download,async token=>{var file=new OpenFileDialog{Filter="Modelo multilingüe base|ggml-base.bin"};if(file.ShowDialog(this)!=true)return;await LocalTranscription.InstallModel(file.FileName,token);modelReady=true;UpdateModel();})));
        var right=new StackPanel();Grid.SetColumn(right,2);settings.Children.Add(right);right.Children.Add(Label("Idioma",12));right.Children.Add(language);right.Children.Add(Label("Nombres o vocabulario opcional",12));names.Text=vocabulary;right.Children.Add(names);
        StyleButton(transcribe);transcribe.Content="TRANSCRIBIR AUDIO";transcribe.Background=Brushes.Gold;transcribe.Foreground=MainWindow.Navy;transcribe.Click+=async(_,_)=>await Run(Operation.Recognize,async token=>
        {
            if(audio==null)return;var doc=await LocalTranscription.Transcribe(audio,language.SelectedIndex==0?"es":language.SelectedIndex==2?"en":"auto",names.Text,new Progress<string>(s=>status.Text=s),token);
            duration=doc.Duration;rows.Clear();foreach(var part in doc.Parts)rows.Add(part);status.Text=$"{rows.Count} fragmentos reconocidos. Revisa nombres y tiempos antes de aplicar.";
        });left.Children.Add(transcribe);
        grid.ItemsSource=rows;
        foreach(var field in new[]{("Inicio (s)","Start"),("Final (s)","End")})grid.Columns.Add(new DataGridTextColumn{Header=field.Item1,Width=100,Binding=new Binding(field.Item2){StringFormat="0.000",ConverterCulture=CultureInfo.CurrentCulture,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true}});
        var textStyle=new Style(typeof(TextBlock));textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(5)));
        grid.Columns.Add(new DataGridTextColumn{Header="Texto · puedes corregirlo",MinWidth=300,Width=new DataGridLength(1,DataGridLengthUnitType.Star),Binding=new Binding("Text"){UpdateSourceTrigger=UpdateSourceTrigger.LostFocus},ElementStyle=textStyle});grid.SizeChanged+=(_,_)=>{if(grid.ActualWidth>0)grid.Columns[2].Width=new DataGridLength(Math.Max(300,grid.ActualWidth-225));};Grid.SetRow(grid,2);root.Children.Add(grid);
        var footer=new StackPanel();Grid.SetRow(footer,3);root.Children.Add(footer);var edits=new WrapPanel();edits.Children.Add(Action("Unir filas seleccionadas",()=>{Merge();return Task.CompletedTask;}));edits.Children.Add(Action("Dividir fila por frases",()=>{SplitSentence();return Task.CompletedTask;}));edits.Children.Add(Action("Copiar texto",()=>{CommitGrid();Clipboard.SetText(string.Join(" ",rows.Select(r=>r.Text.Trim())));return Task.CompletedTask;}));footer.Children.Add(edits);
        footer.Children.Add(Label("Los tiempos son aproximados. Si cambias la cantidad de palabras de una fila, su ritmo se redistribuye entre Inicio y Final. Cada fragmento puede tener después su personaje o plano del juicio.",12));
        var options=new WrapPanel();onlyText.Margin=fragments.Margin=new Thickness(0,6,20,6);onlyText.IsChecked=!createFragments;fragments.IsChecked=createFragments;options.Children.Add(onlyText);options.Children.Add(fragments);options.Children.Add(destination);destination.IsEnabled=canReplace;footer.Children.Add(options);
        footer.Children.Add(Label("Se conservan personaje, posición y fondo del clip de referencia. Las animaciones por palabras se asignan después sobre el texto nuevo. Puedes deshacer la aplicación.",12));
        var final=new WrapPanel();StyleButton(apply);apply.Content="APLICAR RESULTADO";apply.Background=Brushes.Gold;apply.Foreground=MainWindow.Navy;apply.Click+=(_,_)=>Apply();final.Children.Add(apply);StyleButton(cancel);cancel.Click+=(_,_)=>stop?.Cancel();final.Children.Add(cancel);footer.Children.Add(final);footer.Children.Add(status);
        Loaded+=async(_,_)=>{try{modelReady=await LocalTranscription.ModelReady();UpdateModel();UpdateControls();}catch(Exception ex){status.Text=ex.Message;}};
        Closing+=(_,e)=>{if(operation==Operation.None)return;e.Cancel=true;closeRequested=true;stop?.Cancel();};
        if(current!=null)SetAudio(current);UpdateControls();
    }
    void SetAudio(RecordedVoice voice){audio=voice;duration=voice.Duration;rows.Clear();source.Text=$"{voice.Name} · {voice.Duration:0.00} s audibles · se usan el recorte y desplazamiento actuales";UpdateControls();}
    void UpdateModel()=>model.Text=modelReady?"Modelo multilingüe base listo · funciona sin Internet":"Falta el modelo base. Se descarga una vez; tus audios se procesan localmente.";
    void UpdateControls()
    {
        bool idle=operation==Operation.None;foreach(var button in controls)button.IsEnabled=idle;record.IsEnabled=idle || operation==Operation.Record;record.Content=operation==Operation.Record?"■ Detener grabación":"● Grabar micrófono";
        transcribe.IsEnabled=idle && audio!=null && modelReady;apply.IsEnabled=idle && rows.Count>0;devices.IsEnabled=language.IsEnabled=names.IsEnabled=grid.IsEnabled=idle;cancel.Visibility=idle?Visibility.Collapsed:Visibility.Visible;cancel.Content=operation==Operation.Record?"Detener grabación":"Cancelar operación";
    }
    async Task Run(Operation kind,Func<CancellationToken,Task> action)
    {
        if(operation!=Operation.None)return;operation=kind;stop=new();UpdateControls();
        try{await action(stop.Token);}catch(OperationCanceledException){status.Text="Operación cancelada. Puedes volver a intentarlo.";}catch(Exception ex){status.Text=ex.Message;}
        finally{stop.Dispose();stop=null;operation=Operation.None;UpdateControls();if(closeRequested)Close();}
    }
    bool CommitGrid()=>grid.CommitEdit(DataGridEditingUnit.Cell,true) && grid.CommitEdit(DataGridEditingUnit.Row,true);
    void Apply()
    {
        try{if(!CommitGrid()){status.Text="Corrige el valor que está marcado en rojo.";return;}if(audio==null)return;var doc=new TranscriptDocument{Duration=duration,Parts=rows.ToList()};doc.Validate();Result=new(audio,doc,fragments.IsChecked==true,destination.IsEnabled && destination.SelectedIndex==1);Close();}catch(Exception ex){status.Text=ex.Message;}
    }
    void Merge()
    {
        if(!CommitGrid())return;var selected=grid.SelectedItems.Cast<TranscriptPart>().OrderBy(p=>rows.IndexOf(p)).ToList();if(selected.Count<2){status.Text="Selecciona dos o más filas consecutivas.";return;}int index=rows.IndexOf(selected[0]);if(selected.Where((p,i)=>rows.IndexOf(p)!=index+i).Any()){status.Text="Selecciona filas consecutivas para unirlas.";return;}
        var merged=new TranscriptPart{Text=string.Join(" ",selected.Select(p=>p.Text.Trim())),Start=selected[0].Start,End=selected[^1].End};merged.OriginalText=merged.Text;
        foreach(var p in selected)foreach(var w in p.Timings())merged.Words.Add(new(p.Text[w.StartChar..w.EndChar],w.Start,w.End));foreach(var p in selected)rows.Remove(p);rows.Insert(index,merged);grid.SelectedItem=merged;
    }
    void SplitSentence()
    {
        if(!CommitGrid() || grid.SelectedItem is not TranscriptPart part)return;var phrases=System.Text.RegularExpressions.Regex.Split(part.Text.Trim(),@"(?<=[.!?…])\s+").Where(t=>t.Length>0).ToList();if(phrases.Count<2){status.Text="Esta fila no contiene varias frases. Puedes corregir primero su puntuación.";return;}int index=rows.IndexOf(part),total=phrases.Sum(p=>p.Length),at=0;rows.Remove(part);
        var known=part.Timings();int offset=0;
        foreach(var phrase in phrases)
        {
            int from=part.Text.IndexOf(phrase,offset,StringComparison.Ordinal);offset=from+phrase.Length;var words=known.Where(w=>w.StartChar>=from && w.EndChar<=offset).ToList();
            double a=words.Count>0?words[0].Start:part.Start+(part.End-part.Start)*at/total;at+=phrase.Length;double b=words.Count>0?words[^1].End:part.Start+(part.End-part.Start)*at/total;
            rows.Insert(index++,new(){Text=phrase,OriginalText=phrase,Start=a,End=b,Words=words.Select(w=>new RecognizedWord(part.Text[w.StartChar..w.EndChar],w.Start,w.End)).ToList()});
        }
        status.Text="Fila dividida. Revisa los tiempos antes de aplicar.";
    }
    Button Action(string title,Func<Task> action){var b=new Button{Content=title};StyleButton(b);b.Click+=async(_,_)=>{try{await action();}catch(Exception ex){status.Text=ex.Message;}UpdateControls();};controls.Add(b);return b;}
    public void VerifyReviewLayout(){if(grid.Columns[2].ActualWidth<300)throw new System.IO.InvalidDataException("Transcript text column is too narrow");}
    public void TestReview(TranscriptDocument document){rows.Clear();foreach(var part in document.Parts)rows.Add(part);duration=document.Duration;modelReady=true;UpdateModel();UpdateControls();}
    static void StyleButton(Button b){b.Margin=new Thickness(0,4,8,4);b.Padding=new Thickness(9,7,9,7);b.Background=new SolidColorBrush(Color.FromRgb(35,52,82));b.Foreground=Brushes.White;b.BorderThickness=new Thickness(0);}
    static TextBlock Label(string text,double size,Brush? brush=null)=>new(){Text=text,FontSize=size,Foreground=brush??Brushes.LightSteelBlue,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,4)};
}
