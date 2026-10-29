using System.Diagnostics;
using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AvatarStudio;

public sealed class MainWindow : Window
{
    public static readonly Brush Navy = new SolidColorBrush(Color.FromRgb(13, 20, 39));
    static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(28, 40, 66));
    readonly AssetStore assets;
    readonly Preferences settings;
    readonly bool testing;
    readonly AppUpdates updates=new();
    readonly UpdateNotificationGate updateNotifications=new();
    UpdatesWindow? updatesWindow;
    readonly ComboBox characters = new(), modes = new(), backgrounds = new();
    readonly ListBox poses = new();
    readonly TextBlock status = new(), shortcutLabel = new(), talkLabel = new(), poseTitle = new();
    readonly Image preview = new() { Stretch = Stretch.Uniform };
    readonly TextBlock characterCount=new(){FontSize=15,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,4)};
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    Pose current = null!;
    bool talking, toggleTalking, choosing, selecting, talkShortcutActive;
    double started;
    Hotkeys? hotkeys;
    OutputWindow? output;
    int lastFrame = -1;
    Cell? lastCell;

    public MainWindow(AssetStore assets, Preferences settings, bool testing = false)
    {
        this.assets = assets; this.settings = settings; this.testing = testing;
        Title = "Axx Belmont · Avatar Studio "+AppUpdates.Version; Width = 1050; Height = 760; MinWidth = 900; MinHeight = 680;
        Background = Navy; Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new Grid { Margin = new Thickness(24) }; Content = new Border { Background = Navy, Child = root };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        heading.Children.Add(Text("AVATAR STUDIO", 30, Brushes.Gold));
        UpdateCharacterCount();heading.Children.Add(characterCount);
        var header=new DockPanel();
        var video=Button("CREAR VIDEO CON DIÁLOGO",(_,_)=>OpenDialogue());video.HorizontalAlignment=HorizontalAlignment.Right;video.VerticalAlignment=VerticalAlignment.Top;video.Margin=new Thickness(18,8,0,0);video.Foreground=Brushes.Gold;
        DockPanel.SetDock(video,Dock.Right);header.Children.Add(video);header.Children.Add(heading);root.Children.Add(header);
        var body = new Grid(); Grid.SetRow(body, 1); root.Children.Add(body);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        body.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 6, 0) });
        left.Children.Add(Text("PERSONAJE", 12, Brushes.Gold));
        characters.ItemsSource = assets.Library.Poses.Select(p => p.Character).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList(); characters.IsTextSearchEnabled = true; characters.Margin = new Thickness(0, 6, 0, 12);
        left.Children.Add(characters);
        left.Children.Add(Button("Importar mi avatar…",(_,_)=>ImportCustomAvatar()));
        poses.Height = 190; poses.Background = Panel; poses.Foreground = Brushes.White; poses.BorderThickness = new Thickness(0);
        left.Children.Add(poses);
        shortcutLabel.TextWrapping = TextWrapping.Wrap; shortcutLabel.Margin = new Thickness(0, 10, 0, 0); left.Children.Add(shortcutLabel);
        left.Children.Add(Button("Asignar atajo a esta pose", (_, _) => Assign(false)));
        left.Children.Add(Button("Quitar atajo de esta pose", (_, _) => { settings.Shortcuts.Remove(current.Id); Register(); Labels(); Save(); }));
        left.Children.Add(Text("CONTROL DE VOZ", 12, Brushes.Gold));
        modes.Items.Add("Mantener pulsado para hablar"); modes.Items.Add("Pulsar para activar / detener");
        modes.SelectedIndex = settings.Mode == "toggle" ? 1 : 0; modes.Margin = new Thickness(0, 6, 0, 6); left.Children.Add(modes);
        talkLabel.TextWrapping = TextWrapping.Wrap; left.Children.Add(talkLabel);
        left.Children.Add(Button("Cambiar atajo de hablar", (_, _) => Assign(true)));
        var right = new Grid(); Grid.SetColumn(right, 1); body.Children.Add(right);
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        poseTitle.FontSize = 19; poseTitle.Margin = new Thickness(0, 0, 0, 8); right.Children.Add(poseTitle);
        var stage = new Border { Background = Checker(), CornerRadius = new CornerRadius(8), Child = preview, ClipToBounds = true };
        Grid.SetRow(stage, 1); right.Children.Add(stage);
        var controls = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; Grid.SetRow(controls, 2); right.Children.Add(controls);
        var speak = Button("▶  Hablar / detener (vista previa)", (_, _) => { toggleTalking = !toggleTalking; Update(); }); controls.Children.Add(speak);
        controls.Children.Add(Button("↺  Repetir animación",(_,_)=>ReplayAnimation()));
        var speedLine = new StackPanel { Orientation = Orientation.Horizontal };
        speedLine.Children.Add(Text("Velocidad  ", 13));
        var speed = new Slider { Minimum = 0.5, Maximum = 2, Value = Math.Clamp(settings.Speed, .5, 2), Width = 180, TickFrequency = .25, IsSnapToTickEnabled = true };
        speedLine.Children.Add(speed); var speedText = Text($"  {speed.Value:0.00}×", 13); speedLine.Children.Add(speedText); controls.Children.Add(speedLine);
        var mirror = new CheckBox { Content = "Reflejar personaje", IsChecked = settings.Mirror, Margin = new Thickness(0, 8, 0, 4), Foreground = Brushes.White }; controls.Children.Add(mirror);
        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var outputLine = new StackPanel { Orientation = Orientation.Horizontal };
        backgrounds.ItemsSource = new[] { "Verde para OBS", "Transparente", "Magenta para OBS" };
        backgrounds.SelectedItem = settings.Background; if (backgrounds.SelectedIndex < 0) backgrounds.SelectedIndex = 0;
        backgrounds.Width = 190; backgrounds.VerticalAlignment = VerticalAlignment.Center; outputLine.Children.Add(backgrounds);
        var show = Button("ABRIR VENTANA DEL AVATAR →", (_, _) => OpenOutput()); show.Margin = new Thickness(12, 0, 0, 0); show.Background = Brushes.Gold; show.Foreground = Navy; outputLine.Children.Add(show); footer.Children.Add(outputLine);
        footer.Children.Add(Text("OBS: captura «Avatar Studio · Salida». Con fondo verde, añade el filtro de clave de color.", 12));
        var updateButton=Button("Versión "+AppUpdates.Version+" · Actualizaciones",(_,_)=>OpenUpdates());footer.Children.Add(updateButton);
        updates.Changed+=()=>{if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(()=>{updateButton.Content=updates.AvailableVersion is {} version?"Nueva versión "+version+" · Ver actualización":"Versión "+AppUpdates.Version+" · Actualizaciones";MaybeShowUpdate();});};
        Loaded+=async(_,_)=>{if(!testing && settings.CheckUpdatesOnStart)await updates.Check();};
        Activated+=(_,_)=>Dispatcher.BeginInvoke(MaybeShowUpdate);
        IsEnabledChanged+=(_,_)=>{if(IsEnabled)Dispatcher.BeginInvoke(MaybeShowUpdate);};
        status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 8, 0, 0); status.Foreground = Brushes.LightSteelBlue; footer.Children.Add(status);

        characters.SelectionChanged += (_, _) =>
        {
            if (selecting) return;
            poses.ItemsSource = assets.Library.Poses.Where(p => p.Character == (string)characters.SelectedItem).ToList();
            poses.SelectedIndex = 0;
        };
        poses.SelectionChanged += (_, _) => { if (!selecting && poses.SelectedItem is Pose p) Select(p); };
        modes.SelectionChanged += (_, _) => { settings.Mode = modes.SelectedIndex == 1 ? "toggle" : "hold"; toggleTalking = false; Update(); Save(); };
        speed.ValueChanged += (_, _) => { double now=clock.Elapsed.TotalSeconds;started=AnimationClock.AnchorAfterSpeedChange(now,started,settings.Speed,speed.Value);settings.Speed = speed.Value; speedText.Text = $"  {speed.Value:0.00}×";Tick(); Save(); };
        mirror.Click += (_, _) => { settings.Mirror = mirror.IsChecked == true; lastFrame = -1; Tick(); Save(); };
        backgrounds.SelectionChanged += (_, _) => { settings.Background = (string)backgrounds.SelectedItem; output?.SetBackground(settings.Background); Save(); };
        Select(assets.Library.Poses.FirstOrDefault(p => p.Id == settings.Pose) ?? assets.Library.Poses[0]);
        SourceInitialized += (_, _) => { if (!testing) { hotkeys = new Hotkeys(this); Register(); timer.Start(); } };
        timer.Tick += (_, _) =>
        {
            bool hold = talkShortcutActive && !choosing && settings.Mode == "hold" && Hotkeys.IsDown(settings.Talk);
            SetTalking(!choosing && (hold || toggleTalking)); Tick();
        };
        Closed += (_, _) => { timer.Stop(); hotkeys?.Dispose(); output?.Close(); Save(); };
    }
    static TextBlock Text(string text, double size, Brush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    void UpdateCharacterCount()=>characterCount.Text=$"Axx Belmont · {assets.Library.Poses.Select(p=>p.Character).Distinct().Count()} personajes y avatares";
    void ImportCustomAvatar()
    {
        choosing=true;toggleTalking=false;SetTalking(false);timer.Stop();hotkeys?.Clear();
        try
        {
            var dialog=new Microsoft.Win32.OpenFileDialog{Title="Importar paquete de avatar",Filter="Avatar Studio · avatar animado|*.avavatar.zip",CheckFileExists=true};if(dialog.ShowDialog(this)!=true)return;
            var pose=assets.ImportAvatarPack(dialog.FileName);selecting=true;characters.ItemsSource=assets.Library.Poses.Select(p=>p.Character).Distinct().OrderBy(n=>n,StringComparer.CurrentCultureIgnoreCase).ToList();selecting=false;UpdateCharacterCount();Select(pose);Save();status.Text="Avatar importado. Ya puedes hablar, asignar atajos y usarlo en el editor de video.";
        }
        catch(Exception ex){selecting=false;status.Text="No se pudo importar el avatar: "+ex.Message;}
        finally{choosing=false;Register();if(!testing)timer.Start();}
    }
    static Button Button(string label, RoutedEventHandler action)
    {
        var b = new Button { Content = label, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 5, 0, 5), Background = Panel, Foreground = Brushes.White, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
        b.Click += action; return b;
    }
    static Brush Checker()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(38, 49, 69)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
        var lighter = new SolidColorBrush(Color.FromRgb(46, 59, 80));
        group.Children.Add(new GeometryDrawing(lighter, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(lighter, null, new RectangleGeometry(new Rect(16, 16, 16, 16))));
        return new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 32, 32), ViewportUnits = BrushMappingMode.Absolute };
    }
    void Select(Pose p)
    {
        current = p; settings.Pose = p.Id; selecting = true;
        characters.SelectedItem = p.Character;
        poses.ItemsSource = assets.Library.Poses.Where(x => x.Character == p.Character).ToList(); poses.SelectedItem = p;
        poses.ScrollIntoView(p);
        selecting = false; assets.ClearCache(); lastFrame = -1; started = clock.Elapsed.TotalSeconds;
        poseTitle.Text = $"{p.Character} · {p.Name}"; Labels(); Tick(); Save();
    }
    void Labels()
    {
        shortcutLabel.Text = settings.Shortcuts.TryGetValue(current.Id, out var key) ? $"Atajo de pose: {key}" : "Esta pose aún no tiene atajo.";
        talkLabel.Text = $"Hablar: {settings.Talk}";
    }
    void OpenDialogue()
    {
        choosing=true;toggleTalking=false;SetTalking(false);timer.Stop();hotkeys?.Clear();
        try { new DialogueSequenceWindow(assets,new DialogueProject{Text="",DefaultPose=current.Id}){Owner=this}.ShowDialog(); }
        finally { choosing=false;assets.ClearCache();lastFrame=-1;started=clock.Elapsed.TotalSeconds;Tick();Register();timer.Start(); }
    }
    void MaybeShowUpdate()
    {
        if(testing || !settings.CheckUpdatesOnStart || updatesWindow!=null || updates.Busy || updates.Updating)return;
        if(updateNotifications.TryShow(updates.AvailableVersion,IsVisible && IsActive && IsEnabled))OpenUpdates();
    }
    void OpenUpdates()
    {
        if(updatesWindow!=null){updatesWindow.Activate();return;}
        choosing=true;toggleTalking=false;SetTalking(false);timer.Stop();hotkeys?.Clear();
        updateNotifications.MarkShown(updates.AvailableVersion);
        try{updatesWindow=new UpdatesWindow(updates,settings){Owner=this};updatesWindow.ShowDialog();}
        finally{updateNotifications.MarkShown(updates.AvailableVersion);updatesWindow=null;choosing=false;lastFrame=-1;started=clock.Elapsed.TotalSeconds;Tick();Register();if(!testing)timer.Start();}
    }
    void Assign(bool talk)
    {
        choosing = true; toggleTalking = false; SetTalking(false); hotkeys?.Clear();
        try
        {
            var dialog = new ShortcutDialog(this);
            if (dialog.ShowDialog() == true && dialog.Result is Shortcut key)
            {
                bool used = talk ? settings.Shortcuts.Values.Contains(key) : settings.Talk == key || settings.Shortcuts.Any(x => x.Key != current.Id && x.Value == key);
                if (used) MessageBox.Show(this, "Ese atajo ya está asignado a otra acción.", "Atajo ocupado");
                else if (talk) settings.Talk = key;
                else settings.Shortcuts[current.Id] = key;
            }
        }
        finally { choosing = false; Register(); Labels(); Save(); }
    }
    void Register()
    {
        if (hotkeys == null) return;
        hotkeys.Clear(); var failures = new List<string>();
        foreach (var pair in settings.Shortcuts)
        {
            var pose = assets.Library.Poses.FirstOrDefault(p => p.Id == pair.Key);
            if (pose != null && !hotkeys.Add(pair.Value, () => Select(pose))) failures.Add(pair.Value.ToString());
        }
        talkShortcutActive = hotkeys.Add(settings.Talk, () => { if (settings.Mode == "toggle" && !choosing) { toggleTalking = !toggleTalking; Update(); } });
        if (!talkShortcutActive) failures.Add(settings.Talk.ToString());
        status.Text = failures.Count == 0 ? "Atajos activos incluso con otra aplicación en primer plano. Esc en la ventana de salida la cierra." : "Atajos ocupados por otra aplicación: " + string.Join(", ", failures) + ". Elige otras combinaciones.";
    }
    void Update() { SetTalking(toggleTalking); Tick(); }
    void ReplayAnimation(){started=clock.Elapsed.TotalSeconds;lastFrame=-1;Tick();}
    void SetTalking(bool active) { if (talking == active) return; talking = active;if(AnimationClock.SpeechRestarts(current))started = clock.Elapsed.TotalSeconds; lastFrame = -1; }
    void Tick()
    {
        if (current == null) return;
        var clip = current.Clips[talking ? current.Talk : current.Idle];
        double elapsed = (clock.Elapsed.TotalSeconds - started) * assets.Library.Fps * settings.Speed;
        double time = AnimationClock.Frame(clip, elapsed);
        var key = clip.At(time); lastCell = key;
        if (lastFrame != key.Frame) { preview.Source = assets.Frame(current.Frames[key.Frame]); lastFrame = key.Frame; }
        ApplyTransform(preview, key, settings.Mirror);
        output?.Present(preview.Source, key, settings.Mirror);
    }
    public static void ApplyTransform(Image image, Cell cell, bool mirror)
    {
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(cell.Sx * (mirror ? -1 : 1), cell.Sy));
        group.Children.Add(new RotateTransform(cell.Rotation));
        // Source translations use the original 256×192 coordinate space.
        var bitmap = image.Source as System.Windows.Media.Imaging.BitmapSource;
        double scale = bitmap == null ? 0 : 3 * Math.Min(image.ActualWidth / bitmap.PixelWidth, image.ActualHeight / bitmap.PixelHeight);
        group.Children.Add(new TranslateTransform(cell.X * scale, cell.Y * scale));
        image.RenderTransformOrigin = new Point(.5, .5); image.RenderTransform = group;
    }
    void OpenOutput()
    {
        if (output == null)
        {
            output = new OutputWindow(); output.Closed += (_, _) => output = null;
            output.SetBackground(settings.Background); output.Show();
        }
        else output.Activate();
        Tick();
    }
    void Save()
    {
        if (testing) return;
        try { settings.Save(); }
        catch (Exception ex) { status.Text = "No se pudo guardar la configuración: " + ex.Message; }
    }
    public void TestPose(Pose p, bool speak) { Select(p); SetTalking(speak); Tick(); }
    public void VerifyGestureContinuity()
    {
        foreach(var pose in assets.Library.Poses.Where(p=>p.Idle==p.Talk))
        {
            TestPose(pose,false);started=clock.Elapsed.TotalSeconds-1;
            SetTalking(true);Tick();SetTalking(false);Tick();
            if(clock.Elapsed.TotalSeconds-started<.99)throw new InvalidDataException($"Speech restarted gesture {pose.Id}");
            ReplayAnimation();
            if(lastCell?.Frame!=pose.Clips[pose.Idle].Keys[0].Frame)throw new InvalidDataException($"Gesture replay did not start at its first frame: {pose.Id}");
        }
    }
}

public sealed class OutputWindow : Window
{
    readonly Image sprite = new() { Stretch = Stretch.Uniform };
    public OutputWindow()
    {
        Title = "Avatar Studio · Salida"; Width = 640; Height = 480;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip; Topmost = true; Content = sprite;
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        var menu = new ContextMenu();
        var top = new MenuItem { Header = "Siempre visible", IsCheckable = true, IsChecked = true }; top.Click += (_, _) => Topmost = top.IsChecked; menu.Items.Add(top);
        var close = new MenuItem { Header = "Cerrar ventana de salida" }; close.Click += (_, _) => Close(); menu.Items.Add(close); ContextMenu = menu;
    }
    public void SetBackground(string value) => Background = value == "Verde para OBS" ? Brushes.Lime : value == "Magenta para OBS" ? Brushes.Magenta : Brushes.Transparent;
    public void Present(ImageSource frame, Cell cell, bool mirror) { sprite.Source = frame; MainWindow.ApplyTransform(sprite, cell, mirror); }
}
