using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AvatarStudio;

public sealed class UpdatesWindow:Window
{
    readonly AppUpdates updates;
    readonly Preferences preferences;
    readonly bool testing;
    readonly TextBlock title=Text("Actualizaciones",27,Brushes.White),subtitle=Text("",14,Brushes.LightSteelBlue),status=Text("",14,Brushes.White),nextVersion=Text("—",20,Brushes.Gold);
    readonly StackPanel notes=new();
    readonly Button primary=new(){Content="Buscar actualizaciones"},secondary=new(){Content="Cerrar"};
    readonly ProgressBar progress=new(){Minimum=0,Maximum=100,Height=8,Foreground=Brushes.Gold,Background=new SolidColorBrush(Color.FromRgb(43,56,80)),BorderThickness=new Thickness(0),Margin=new Thickness(0,10,0,0)};
    readonly Border progressCard=new(){Padding=new Thickness(14),CornerRadius=new CornerRadius(9),Background=new SolidColorBrush(Color.FromRgb(28,40,66)),Margin=new Thickness(0,14,0,0)};
    readonly CheckBox auto=new(){Content="Avisarme al abrir Avatar Studio",Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,14,0,0)};
    CancellationTokenSource? stop;
    bool closeAfterCancel,closed;
    string? lastNotes;
    public UpdatesWindow(AppUpdates updates,Preferences preferences,bool testing=false)
    {
        this.updates=updates;this.preferences=preferences;this.testing=testing;
        ShowInTaskbar=false;ResizeMode=ResizeMode.NoResize;primary.IsDefault=true;secondary.IsCancel=true;
        Title="Avatar Studio · Actualización";Width=660;Height=Math.Min(640,SystemParameters.WorkArea.Height-30);MinWidth=560;MinHeight=490;Background=MainWindow.Navy;Foreground=Brushes.White;FontFamily=new("Segoe UI");FontSize=14;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid{Margin=new Thickness(26),Background=MainWindow.Navy};Content=root;
        foreach(var height in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})root.RowDefinitions.Add(new(){Height=height});
        var top=new StackPanel();root.Children.Add(top);
        var brand=Text("AXX BELMONT  /  AVATAR STUDIO",12,Brushes.Gold);brand.FontWeight=FontWeights.SemiBold;brand.Margin=new Thickness(0,0,0,14);top.Children.Add(brand);
        title.FontWeight=FontWeights.SemiBold;top.Children.Add(title);subtitle.Margin=new Thickness(0,7,0,17);top.Children.Add(subtitle);
        var versions=new Grid();versions.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});versions.ColumnDefinitions.Add(new(){Width=new GridLength(42)});versions.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});top.Children.Add(versions);
        var current=VersionCard("TU VERSIÓN",Text(AppUpdates.Version,20,Brushes.White));versions.Children.Add(current);
        var arrow=Text("→",24,Brushes.LightSteelBlue);arrow.HorizontalAlignment=HorizontalAlignment.Center;arrow.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(arrow,1);versions.Children.Add(arrow);
        var target=VersionCard("VERSIÓN DISPONIBLE",nextVersion);Grid.SetColumn(target,2);versions.Children.Add(target);
        var progressBody=new StackPanel();progressBody.Children.Add(status);progressBody.Children.Add(progress);progressCard.Child=progressBody;top.Children.Add(progressCard);
        var scroll=new ScrollViewer{Content=notes,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,18,0,12)};Grid.SetRow(scroll,1);root.Children.Add(scroll);
        var bottom=new StackPanel();Grid.SetRow(bottom,2);root.Children.Add(bottom);
        bottom.Children.Add(Text("Se descargará y verificará la actualización antes de reiniciar. Tus proyectos y ajustes se conservan.",12,Brushes.LightSteelBlue));
        var buttons=new Grid{Margin=new Thickness(0,16,0,0)};buttons.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});buttons.ColumnDefinitions.Add(new(){Width=new GridLength(14)});buttons.ColumnDefinitions.Add(new(){Width=new GridLength(150)});
        primary.Style=ButtonStyle(Brushes.Gold,MainWindow.Navy);secondary.Style=ButtonStyle(new SolidColorBrush(Color.FromRgb(28,40,66)),Brushes.White);buttons.Children.Add(primary);Grid.SetColumn(secondary,2);buttons.Children.Add(secondary);bottom.Children.Add(buttons);
        auto.IsChecked=preferences.CheckUpdatesOnStart;auto.Click+=(_,_)=>{preferences.CheckUpdatesOnStart=auto.IsChecked==true;if(!testing)preferences.Save();};bottom.Children.Add(auto);
        primary.Click+=async(_,_)=>await PrimaryAction();secondary.Click+=(_,_)=>Dismiss();
        updates.Changed+=OnChanged;
        Closing+=(_,e)=>{if(updates.Restarting){e.Cancel=true;return;}if(stop!=null){e.Cancel=true;closeAfterCancel=true;stop.Cancel();secondary.IsEnabled=false;status.Text="Cancelando la actualización…";}};
        Closed+=(_,_)=>{closed=true;updates.Changed-=OnChanged;};
        Loaded+=async(_,_)=>{if(!testing && !updates.Busy && updates.Available==null)await updates.Check();};Refresh();
    }
    async Task PrimaryAction()
    {
        if(stop!=null || updates.Busy || updates.Updating)return;
        if(updates.Available==null){await updates.Check();return;}
        stop=new();Refresh();
        try{await updates.UpdateAndRestart(stop.Token,()=>{if(!testing)preferences.Save();});}
        finally{stop.Dispose();stop=null;Refresh();if(closeAfterCancel)Close();}
    }
    void Dismiss()
    {
        if(updates.Restarting)return;
        if(stop!=null){closeAfterCancel=true;stop.Cancel();secondary.IsEnabled=false;status.Text="Cancelando la actualización…";return;}
        Close();
    }
    void OnChanged(){if(closed || Dispatcher.HasShutdownStarted)return;if(Dispatcher.CheckAccess())Refresh();else Dispatcher.BeginInvoke(Refresh);}
    void Refresh()
    {
        bool working=stop!=null || updates.Updating;
        title.Text=updates.Restarting?"Todo listo. Reiniciando…":working?"Actualizando Avatar Studio":updates.Available!=null?"Nueva versión disponible":"Actualizaciones";
        subtitle.Text=working?"Nos encargamos de la descarga y la instalación.":updates.Available!=null?"Puedes actualizar ahora o seguir con tu versión actual.":"Comprueba si hay novedades para tu aplicación.";
        nextVersion.Text=updates.AvailableVersion??"—";status.Text=updates.Status;
        progressCard.Visibility=updates.Busy || updates.Available==null || working || updates.Failed?Visibility.Visible:Visibility.Collapsed;status.Foreground=updates.Failed?Brushes.LightSalmon:Brushes.White;
        progress.Visibility=updates.Busy || working?Visibility.Visible:Visibility.Collapsed;progress.IsIndeterminate=updates.Busy && !working || updates.Restarting;progress.Value=updates.Progress;
        primary.Content=working?updates.Restarting?"Reiniciando…":$"Actualizando… {updates.Progress}%":updates.Available!=null?"Actualizar y reiniciar":"Buscar actualizaciones";
        primary.IsEnabled=!working && !updates.Busy;secondary.Content=working?"Cancelar":"Ahora no";if(updates.Available==null && !working)secondary.Content="Cerrar";
        secondary.IsEnabled=!updates.Restarting && !closeAfterCancel;auto.IsEnabled=!working;
        string markdown=updates.Available?.TargetFullRelease.NotesMarkdown??"";
        if(markdown!=lastNotes){lastNotes=markdown;RenderNotes(markdown);}
    }
    void RenderNotes(string markdown)
    {
        notes.Children.Clear();notes.Children.Add(Text("QUÉ CAMBIA",12,Brushes.Gold));
        if(string.IsNullOrWhiteSpace(markdown)){notes.Children.Add(Text("Aquí aparecerán las novedades cuando haya una versión disponible.",14,Brushes.LightSteelBlue));return;}
        foreach(string original in markdown.Split('\n').Take(80))
        {
            string line=original.Trim();if(line.Length==0)continue;
            bool heading=line.StartsWith('#');line=Regex.Replace(line,@"^#{1,6}\s*","");line=Regex.Replace(line,@"\[([^\]]+)\]\([^)]+\)","$1");line=line.Replace("**","").Replace("`","");if(line.StartsWith("- ") || line.StartsWith("* "))line="•  "+line[2..];
            var text=Text(line,heading?15:14,Brushes.White);text.Margin=new Thickness(0,heading?10:7,6,0);if(heading)text.FontWeight=FontWeights.SemiBold;notes.Children.Add(text);
        }
    }
    public void VerifyOffer()
    {
        Refresh();if(updates.Available==null || primary.Content.ToString()!="Actualizar y reiniciar" || secondary.Content.ToString()!="Ahora no" || !primary.IsEnabled)throw new System.IO.InvalidDataException("Update offer must have one update action and one dismiss action");
    }
    public void VerifyFailure(){Refresh();if(!updates.Failed || progressCard.Visibility!=Visibility.Visible || !primary.IsEnabled)throw new System.IO.InvalidDataException("Update failure must remain visible and allow retry");}
    static Border VersionCard(string label,TextBlock value)
    {
        var stack=new StackPanel();var caption=Text(label,11,Brushes.LightSteelBlue);caption.Margin=new Thickness(0,0,0,5);stack.Children.Add(caption);stack.Children.Add(value);return new Border{Child=stack,Padding=new Thickness(14,10,14,10),CornerRadius=new CornerRadius(9),Background=new SolidColorBrush(Color.FromRgb(28,40,66))};
    }
    static TextBlock Text(string text,double size,Brush color)=>new(){Text=text,FontSize=size,Foreground=color,TextWrapping=TextWrapping.Wrap};
    static Style ButtonStyle(Brush background,Brush foreground)
    {
        var style=new Style(typeof(Button));style.Setters.Add(new Setter(Control.BackgroundProperty,background));style.Setters.Add(new Setter(Control.ForegroundProperty,foreground));style.Setters.Add(new Setter(Control.FontWeightProperty,FontWeights.SemiBold));style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,46d));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(14,10,14,10)));style.Setters.Add(new Setter(Control.BorderBrushProperty,background));style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(2)));
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(9));
        foreach(var pair in new[]{(Border.BackgroundProperty,"Background"),(Border.BorderBrushProperty,"BorderBrush"),(Border.BorderThicknessProperty,"BorderThickness"),(Border.PaddingProperty,"Padding")})border.SetBinding(pair.Item1,new Binding(pair.Item2){RelativeSource=RelativeSource.TemplatedParent});
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);style.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(Button)){VisualTree=border}));
        var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,.5));style.Triggers.Add(disabled);
        var focused=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focused.Setters.Add(new Setter(Control.BorderBrushProperty,Brushes.White));style.Triggers.Add(focused);
        var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(UIElement.OpacityProperty,.88));style.Triggers.Add(hover);return style;
    }
}
