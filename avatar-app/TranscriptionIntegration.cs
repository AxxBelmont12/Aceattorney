using System.IO;

namespace AvatarStudio;
public sealed partial class DialogueSequenceWindow
{
    void OpenTranscription(bool fragments)
    {
        if(busy)return;Pause();var reference=(quickTemplate??incoming).Copy();
        string vocabulary=string.Join(", ",sequence.Clips.Select(c=>assets.Library.Poses.First(p=>p.Id==c.Project.DefaultPose).Character).Append(assets.Library.Poses.First(p=>p.Id==reference.DefaultPose).Character).Distinct().Take(8));
        var window=new TranscriptionWindow(reference.Recording,fragments,Selected!=null,vocabulary){Owner=this};window.ShowDialog();
        if(window.Result is not {} result)return;
        try{ApplyTranscript(result,reference);}catch(Exception ex){status.Text=ex.Message;}
    }
    void ApplyTranscript(TranscriptionChoice result,DialogueProject reference)
    {
        var projects=result.Fragments?result.Document.Fragments(reference,result.Voice):new List<DialogueProject>{result.Document.Single(reference,result.Voice)};
        var candidate=DialogueSequence.Clone(sequence);var current=Selected;int index=current==null?candidate.Clips.Count:sequence.Clips.IndexOf(current)+(result.Replace?0:1);
        if(result.Replace && current!=null)candidate.Clips.RemoveAt(index);
        if(candidate.Clips.Count+projects.Count>100)throw new InvalidDataException("El resultado supera los 100 fragmentos del montaje. Usa una grabación más corta o une filas de la transcripción.");
        string scene=current?.SceneName??candidate.Clips.LastOrDefault()?.SceneName??"Escena 1";string? firstId=null;
        for(int i=0;i<projects.Count;i++)
        {
            var p=projects[i];p.Validate(assets.Library);var shot=new DialogueShot{Project=p,SceneName=scene,Name=result.Replace && current!=null?current.Name+(i==0?"":$" · {i+1}"):$"Transcripción {i+1:00}"};
            if(i==0 && result.Replace && current!=null)shot.Id=current.Id;firstId??=shot.Id;candidate.Clips.Insert(index+i,shot);
            candidate.Actors[assets.Library.Poses.First(a=>a.Id==p.DefaultPose).Character]=ActorPreset.From(p);
        }
        candidate.Validate(assets.Library);Remember();sequence=candidate;compiled=null;Refresh(firstId);Changed();status.Text=$"Transcripción aplicada: {projects.Count} fragmento(s), voz incluida y escritura sincronizada por palabras. Puedes deshacer esta operación.";
    }
}
