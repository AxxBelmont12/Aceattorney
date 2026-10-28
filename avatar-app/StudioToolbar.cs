using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AvatarStudio;
public sealed partial class DialogueSequenceWindow
{
    void BuildStudioToolbar()
    {
        toolbar.Orientation=Orientation.Horizontal;toolbar.Children.Add(Text("ESTUDIO DE VIDEO",23,Brushes.Gold));
        toolbar.Children.Add(Button("＋ Fragmento",(_,_)=>NewClip()));
        toolbar.Children.Add(MenuButton("Escenas ▾",("Modo juicio…",ConfigureCourt),("Plantillas de conversación…",ShowTemplates),("Fondo y elementos del clip…",EditScene)));
        toolbar.Children.Add(MenuButton("Audio ▾",("Transcribir audio…",()=>OpenTranscription(false)),("Transcribir y crear fragmentos…",()=>OpenTranscription(true))));
        toolbar.Children.Add(Button("Biblioteca…",(_,_)=>ShowFragmentLibrary()));toolbar.Children.Add(Button("Proyecto…",(_,_)=>ShowOrganizer()));
        toolbar.Children.Add(MenuButton("Archivo ▾",("Nuevo montaje",NewProject),("Abrir montaje…",Open),("Guardar montaje…",Save),("Importar diálogo…",ImportClip),("Versiones anteriores…",ShowVersions),("Exportar por lotes…",ShowBatchExport)));
        toolbar.Children.Add(Button("↶",(_,_)=>History(false)));toolbar.Children.Add(Button("↷",(_,_)=>History(true)));
    }
    Button MenuButton(string title,params (string Title,Action Action)[] entries)
    {
        var button=new Button{Content=title};StyleButton(button);var menu=new ContextMenu{PlacementTarget=button,Placement=PlacementMode.Bottom};button.ContextMenu=menu;
        foreach(var entry in entries){var item=new MenuItem{Header=entry.Title};item.Click+=(_,_)=>{if(CommitQuick())entry.Action();};menu.Items.Add(item);}
        button.Click+=(_,_)=>{if(CommitQuick())menu.IsOpen=true;};return button;
    }
    void NewProject()
    {
        try
        {
            Pause();if(sequence.Clips.Count>0)ProjectStorage.WriteVersion(sequence,JsonSerializer.Serialize(sequence,AssetStore.Json));
            Remember();sequence=new(){Actors=sequence.Actors.ToDictionary(p=>p.Key,p=>p.Value with {}),Court=sequence.Court with {}};compiled=null;Refresh(null);NewClip();Changed();status.Text="Nuevo montaje. Deshacer recupera el anterior; su versión también quedó guardada.";
        }
        catch(Exception ex){status.Text=ex.Message;}
    }
}
