using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AvatarStudio;
public sealed partial class DialogueSequenceWindow
{
    readonly Button deleteClipButton=new(){Content="× Eliminar fragmento",ToolTip="Elimina el seleccionado · Supr. Para recuperarlo: Ctrl+Z."};
    bool timelineActionDown;
    string? contextClipId;
    void BuildTimelineDeletion(WrapPanel tools)
    {
        StyleButton(deleteClipButton);deleteClipButton.Foreground=Brushes.LightSalmon;deleteClipButton.Click+=(_,_)=>Remove();tools.Children.Add(deleteClipButton);
        var menu=new ContextMenu();var remove=new MenuItem{Header="Eliminar este fragmento",InputGestureText="Supr"};remove.Click+=(_,_)=>{if(contextClipId!=null)RemoveClip(contextClipId);};menu.Items.Add(remove);
        menu.Items.Add(new Separator());var undo=new MenuItem{Header="Deshacer último cambio",InputGestureText="Ctrl+Z"};undo.Click+=(_,_)=>History(false);menu.Items.Add(undo);track.ContextMenu=menu;
        track.PreviewMouseRightButtonDown+=(_,e)=>{var container=ItemsControl.ContainerFromElement(track,e.OriginalSource as DependencyObject) as ListBoxItem;contextClipId=(container?.DataContext as Card)?.Id;};
        track.ContextMenuOpening+=(_,e)=>{if(contextClipId==null || busy || resizingClip){e.Handled=true;return;}remove.IsEnabled=sequence.Clips.Any(c=>c.Id==contextClipId);undo.IsEnabled=this.undo.Count>0;};
    }
    static bool IsTimelineAction(DependencyObject? source)
    {
        for(var current=source;current!=null;current=current is Visual?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current))if(current is ButtonBase or Thumb)return true;
        return false;
    }
    bool TryDeleteShortcut(Key key,ModifierKeys modifiers,IInputElement? focus)
    {
        if(key!=Key.Delete || modifiers!=ModifierKeys.None || focus is TextBoxBase or ComboBox || busy || resizingClip || Selected==null)return false;
        Remove();return true;
    }
    void RemoveClip(string id)
    {
        if(busy || resizingClip || !sequence.Clips.Any(c=>c.Id==id))return;
        Pause();quickTimer.Stop();
        if(quickId!=id && !CommitQuick())return;
        if(quickId==id && quickDirty && !string.IsNullOrWhiteSpace(quickText.Text))CommitQuick();
        int index=sequence.Clips.FindIndex(c=>c.Id==id);if(index<0)return;
        string label=sequence.Clips[index].Name;Remember();quickDirty=false;quickId=null;sequence.Clips.RemoveAt(index);compiled=null;
        string? next=sequence.Clips.Count==0?null:sequence.Clips[Math.Min(index,sequence.Clips.Count-1)].Id;Refresh(next);Render();track.Focus();Changed();status.Text=$"«{label}» eliminado. Recuperar: Ctrl+Z o ↶ Deshacer.";
    }
    public void VerifyEasyDeletion()
    {
        var baseline=DialogueSequence.Clone(sequence);if(baseline.Clips.Count==0)throw new InvalidDataException("Deletion test needs a clip");
        var first=baseline.Clips[0];sequence.Clips.Add(new(){Name="Otro",Project=first.Project.Copy()});var ids=sequence.Clips.Select(c=>c.Id).ToList();Refresh(ids[0]);
        quickText.Text="Edición pendiente que debe recuperarse al deshacer.";
        if(TryDeleteShortcut(Key.Delete,ModifierKeys.None,quickText) || sequence.Clips.Count!=ids.Count)throw new InvalidDataException("Supr inside text must never remove a fragment");
        if(!TryDeleteShortcut(Key.Delete,ModifierKeys.None,track) || sequence.Clips.Any(c=>c.Id==ids[0]) || Selected?.Id!=ids[1])throw new InvalidDataException("Supr removes selected and selects its next neighbor");
        History(false);if(sequence.Clips[0].Id!=ids[0] || !sequence.Clips[0].Project.Text.StartsWith("Edición pendiente"))throw new InvalidDataException("Undo restores removed clip and its latest text");
        RemoveClip(ids[^1]);if(sequence.Clips.Any(c=>c.Id==ids[^1]) || !sequence.Clips.Any(c=>c.Id==ids[0]))throw new InvalidDataException("Card and context deletion must target their own clip");
        sequence=new(){Clips=new(){DialogueSequence.Clone(first)}};compiled=null;quickDirty=false;Refresh(first.Id);quickText.Text="";Remove();
        if(sequence.Clips.Count!=0 || quickId!=null || preview.Image!=null || !CommitQuick() || sequence.Clips.Count!=0)throw new InvalidDataException("Deleting last or empty clip must not resurrect it or retain preview");
        History(false);if(sequence.Clips.Count!=1 || sequence.Clips[0].Id!=first.Id)throw new InvalidDataException("Last fragment deletion is undoable");
        sequence=baseline;quickDirty=false;compiled=null;undo.Clear();redo.Clear();Refresh(baseline.Clips[0].Id);Render();
    }
}
