using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace AvatarStudio;
public sealed class MontageRuler : FrameworkElement
{
    double duration,pixels=65,position;
    public event Action<double>? Seek;
    public void Update(double length,double zoom,double at){duration=length;pixels=zoom;position=at;Width=Math.Max(1,length*zoom);Height=32;HorizontalAlignment=HorizontalAlignment.Left;InvalidateVisual();}
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){base.OnMouseLeftButtonDown(e);Seek?.Invoke(Math.Clamp(e.GetPosition(this).X/pixels,0,duration));e.Handled=true;}
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(17,27,45)),null,new Rect(0,0,ActualWidth,ActualHeight));
        double step=pixels>=70?.5:pixels>=35?1:2;
        var pen=new Pen(Brushes.SlateGray,1);
        for(double at=0;at<=duration;at+=step)
        {
            double x=at*pixels;dc.DrawLine(pen,new Point(x,22),new Point(x,32));
            var text=new FormattedText(at.ToString("0.#",CultureInfo.CurrentCulture)+" s",CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,Brushes.LightSteelBlue,1);
            dc.DrawText(text,new Point(x+3,2));
        }
        dc.DrawLine(new Pen(Brushes.Gold,2),new Point(position*pixels,0),new Point(position*pixels,32));
    }
}
