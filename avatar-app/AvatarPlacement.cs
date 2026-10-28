using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AvatarStudio;

public sealed partial class DialogueSequenceWindow
{
    readonly Slider avatarX=new(){Minimum=-.75,Maximum=.75,SmallChange=.005,LargeChange=.05};
    readonly Slider avatarY=new(){Minimum=-.5,Maximum=.5,SmallChange=.005,LargeChange=.05};
    readonly Slider avatarSize=new(){Minimum=.25,Maximum=2.5,Value=1,SmallChange=.01,LargeChange=.1};
    readonly TextBlock placementLabel=new();
    readonly CheckBox placementMirror=new(){Content="Mirar al otro lado",Foreground=Brushes.White,Margin=new Thickness(0,5,0,5)};
    bool movingAvatar;
    Point avatarGrab;
    double grabX,grabY;

    void BuildPlacement()
    {
        inspector.Children.Add(Text("POSICIÓN DEL PERSONAJE · este fragmento",12,Brushes.Gold));
        var presets=new WrapPanel();
        foreach(var item in new[]{("← Izquierda",-.25),("Centro",0d),("Derecha →",.25)})
            presets.Children.Add(Button(item.Item1,(_,_)=>SetPlacement(item.Item2,avatarY.Value,avatarSize.Value)));
        inspector.Children.Add(presets);inspector.Children.Add(placementMirror);
        placementMirror.Click+=(_,_)=>{QueueQuick();RenderPlacementPreview();CommitQuick();};
        inspector.Children.Add(Text("Arrastra al personaje en la vista previa. El cuadro de texto permanece fijo.",12,Brushes.LightSteelBlue));
        var fine=new StackPanel();placementLabel.Foreground=Brushes.White;placementLabel.TextWrapping=TextWrapping.Wrap;fine.Children.Add(placementLabel);
        fine.Children.Add(Text("Horizontal",12));fine.Children.Add(avatarX);fine.Children.Add(Text("Vertical",12));fine.Children.Add(avatarY);fine.Children.Add(Text("Tamaño",12));fine.Children.Add(avatarSize);
        fine.Children.Add(Button("Restablecer posición y tamaño",(_,_)=>SetPlacement(0,0,1)));
        inspector.Children.Add(new Expander{Header="Ajustar posición y tamaño",Foreground=Brushes.White,Content=fine});
        foreach(var slider in new[]{avatarX,avatarY,avatarSize})slider.ValueChanged+=(_,_)=>
        {PlacementLabel();if(quickLoading)return;QueueQuick();RenderPlacementPreview();};
        preview.Cursor=Cursors.Hand;
        preview.MouseLeftButtonDown+=(_,e)=>
        {
            if(busy || !CommitQuick() || preview.Image==null)return;
            var point=PreviewPoint(e.GetPosition(preview));if(point==null || point.Value.Y>=474)return;
            int px=(int)(point.Value.X/1280*preview.Image.PixelWidth),py=(int)(point.Value.Y/720*preview.Image.PixelHeight);
            var pixel=new byte[4];var hit=Compile();new DialogueRenderer(assets).Render(hit.Project,hit.Timeline,position,avatarOnly:true).CopyPixels(new Int32Rect(px,py,1,1),pixel,4,0);if(pixel[3]<20)return;
            Pause();double cursor=0;DialogueShot? visible=null;
            foreach(var shot in sequence.Clips){double length=(shot.Out??DialogueCompiler.Compile(shot.Project,assets.Library).Duration)-shot.In;if(position<cursor+length || shot==sequence.Clips[^1]){visible=shot;break;}cursor+=length;}
            if(visible==null)return;
            double at=position;track.SelectedItem=track.Items.Cast<Card>().First(c=>c.Id==visible.Id);position=at;Render();
            movingAvatar=true;avatarGrab=point.Value;grabX=avatarX.Value;grabY=avatarY.Value;quickTimer.Stop();preview.CaptureMouse();e.Handled=true;
        };
        preview.MouseMove+=(_,e)=>
        {
            if(!movingAvatar)return;var point=PreviewPoint(e.GetPosition(preview),true);if(point==null)return;
            quickLoading=true;avatarX.Value=Math.Clamp(grabX+(point.Value.X-avatarGrab.X)/1280,-.75,.75);avatarY.Value=Math.Clamp(grabY+(point.Value.Y-avatarGrab.Y)/720,-.5,.5);quickLoading=false;
            quickDirty=true;quickSaved.Text="Déjalo en el lugar deseado para guardar.";PlacementLabel();RenderPlacementPreview();
        };
        preview.MouseLeftButtonUp+=(_,e)=>{if(!movingAvatar)return;FinishAvatarDrag();e.Handled=true;};
        preview.LostMouseCapture+=(_,_)=>{if(movingAvatar)FinishAvatarDrag();};
    }
    void FinishAvatarDrag(){movingAvatar=false;preview.ReleaseMouseCapture();CommitQuick();}
    void LoadPlacement(DialogueProject p){placementMirror.IsChecked=p.Mirror;avatarX.Value=p.AvatarX;avatarY.Value=p.AvatarY;avatarSize.Value=p.AvatarScale;PlacementLabel();}
    void PlacementLabel()=>placementLabel.Text=$"X: {avatarX.Value*100:+0;-0;0}% · Y: {avatarY.Value*100:+0;-0;0}% · Tamaño: {avatarSize.Value*100:0}%";
    void SetPlacement(double x,double y,double size)
    {
        quickLoading=true;avatarX.Value=x;avatarY.Value=y;avatarSize.Value=size;quickLoading=false;
        PlacementLabel();QueueQuick();RenderPlacementPreview();CommitQuick();
    }
    Point? PreviewPoint(Point p,bool outside=false)
    {
        if(preview.Image==null || preview.ActualWidth<=0 || preview.ActualHeight<=0)return null;
        double fit=Math.Min(preview.ActualWidth/1280,preview.ActualHeight/720),w=1280*fit,h=720*fit;
        double x=(p.X-(preview.ActualWidth-w)/2)/fit,y=(p.Y-(preview.ActualHeight-h)/2)/fit;
        return !outside && (x<0 || x>=1280 || y<0 || y>=720)?null:new Point(x,y);
    }
    void RenderPlacementPreview()
    {
        if(quickTemplate==null || string.IsNullOrWhiteSpace(quickText.Text))return;
        try
        {
            Pause();var p=DialogueSequence.Clone(quickTemplate);DialogueEdits.AdjustRanges(p,p.Text,quickText.Text);
            if(!WordMode)p.DefaultPose=(quickPose.SelectedItem as Pose)?.Id??p.DefaultPose;
            p.AvatarX=avatarX.Value;p.AvatarY=avatarY.Value;p.AvatarScale=avatarSize.Value;p.Mirror=placementMirror.IsChecked==true;
            var timeline=DialogueCompiler.Compile(p,assets.Library);
            double start=sequence.Clips.TakeWhile(c=>c.Id!=quickId).Sum(c=>(c.Out??DialogueCompiler.Compile(c.Project,assets.Library).Duration)-c.In);
            double local=quickPreviewAtEnd?timeline.Duration-.05:position-start+(Selected?.In??0);
            preview.Image=new DialogueRenderer(assets).Render(p,timeline,Math.Clamp(local,0,timeline.Duration));
        }
        catch(Exception ex){quickSaved.Text=ex.Message;}
    }
    public void VerifyPlacementEditor()
    {
        Refresh(sequence.Clips[0].Id);SetPlacement(-.25,.08,.8);
        var p=Selected!.Project;
        if(p.AvatarX!=-.25 || p.AvatarY!=.08 || p.AvatarScale!=.8)throw new InvalidOperationException("Position controls not saved");
        NewClip();quickText.Text="Otra posición.";CommitQuick();SetPlacement(.25,0,1.1);
        if(sequence.Clips[0].Project.AvatarX!=-.25 || Selected!.Project.AvatarX!=.25)throw new InvalidOperationException("Position leaked between clips");
        History(false);if(Selected!.Project.AvatarX!=-.25)throw new InvalidOperationException("Undo position");
        History(true);if(Selected!.Project.AvatarX!=.25)throw new InvalidOperationException("Redo position");
    }
}
