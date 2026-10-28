using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AvatarStudio;
public static class Verification
{
    public static int Run(AssetStore assets)
    {
        var folder = Environment.GetEnvironmentVariable("AVATAR_QA_DIR") ?? "avatar-qa";
        Directory.CreateDirectory(folder);
        int keys = 0, images = 0;
        var migration = new Preferences { Talk = new Shortcut(0x72,3) };
        migration.Shortcuts["pl0000"] = new Shortcut(0x73,3);
        migration.Migrate();
        Require(migration.Shortcuts["pl0000"] == new Shortcut(0x73,3), "Preserve custom pose shortcut");
        Require(!migration.Shortcuts.ContainsKey("pl0300") && !migration.Shortcuts.ContainsKey("pl0100"), "Avoid migrated shortcut conflicts");
        Require(migration.Shortcuts["pl0200"] == new Shortcut(0x74,3), "Add free new-character shortcut");
        migration.Shortcuts.Remove("pl0200"); migration.Migrate();
        Require(!migration.Shortcuts.ContainsKey("pl0200"), "Respect removed shortcut on later launches");
        Require(assets.Library.Poses.Select(p=>p.Character).Distinct().Count() == 27, "Complete animated first-game cast");
        var uniqueImages = new HashSet<string>();
        foreach (var pose in assets.Library.Poses)
        {
            Require(pose.Idle < pose.Clips.Count && pose.Talk < pose.Clips.Count, "Clip binding");
            foreach (var clip in pose.Clips)
            {
                Require(clip.Total > 0 && clip.Keys.Count > 0 && clip.Keys[0].At == 0, "Clip duration / start");
                foreach (var key in clip.Keys)
                {
                    Require(key.Frame >= 0 && key.Frame < pose.Frames.Count, "Frame reference");
                    Require(clip.At(key.At).Frame == key.Frame, "Key duration boundary");
                    keys++;
                }
                Require(clip.At(clip.Total + 20).Frame == clip.Keys[^1].Frame, "Terminal frame");
            }
            foreach (var image in pose.Frames)
            {
                var bitmap = assets.Frame(image);
                Require(bitmap.PixelWidth == assets.Library.Width && bitmap.PixelHeight == assets.Library.Height, "Common PNG canvas"); uniqueImages.Add(image);
            }
            assets.ClearCache();
        }
        images = uniqueImages.Count;
        Require(assets.Library.Poses.First(p=>p.Id=="pl1900").Talk != assets.Library.Poses.First(p=>p.Id=="pl1900").Idle, "Meekins native cross-file speech pairing");
        var app = new Application();
        var window = new MainWindow(assets, new Preferences().Migrate(), testing: true);
        window.VerifyGestureContinuity();window.TestPose(assets.Library.Poses.First(p=>p.Id=="pl0000"),false);
        var visual = (FrameworkElement)window.Content;
        visual.Measure(new Size(1050,760)); visual.Arrange(new Rect(0,0,1050,760)); visual.UpdateLayout();
        Save(visual, Path.Combine(folder,"apollo.png"));
        var daryan = assets.Library.Poses.First(p => p.Id == "pl21100"); window.TestPose(daryan,true); visual.UpdateLayout();
        Save(visual, Path.Combine(folder,"daryan.png"));
        foreach (var (id, name) in new[]{("pl0300","phoenix"),("pl11300","phoenix-abogado"),("pl0100","trucy"),("pl0200","klavier")})
        {
            window.TestPose(assets.Library.Poses.First(p=>p.Id==id),true); visual.UpdateLayout();
            Save(visual, Path.Combine(folder,name+".png"));
        }
        foreach (var character in assets.Library.Poses.Select(p=>p.Character).Distinct())
        {
            var pose=assets.Library.Poses.First(p=>p.Character==character);
            window.TestPose(pose,true);visual.UpdateLayout();
            Save(visual,Path.Combine(folder,pose.Id+"-cast.png"));
        }
        File.WriteAllText(Path.Combine(folder,"verification.json"), JsonSerializer.Serialize(new { poses=assets.Library.Poses.Count, images, keys,
            version=typeof(MainWindow).Assembly.GetName().Version!.ToString(3), dialogueEditorEnabled=true, characters=assets.Library.Poses.Select(p=>p.Character).Distinct().ToArray(), shortcutMigrationPassed=true,singleClipSpeechContinuityPassed=true,
            canvas=new[]{assets.Library.Width,assets.Library.Height}, sourceTimelinesVerified=true, allFrameReferencesDecoded=true, gameModified=false,
            remaining="Global hotkeys and OBS capture need a live user check; exact native interpolation and special breakdown scenes are not certified." }, AssetStore.Json));
        window.Close(); return 0;
    }
    static void Require(bool condition, string name) { if (!condition) throw new InvalidDataException(name); }
    static void Save(FrameworkElement visual, string path)
    {
        var target = new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);
        target.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path); png.Save(stream);
    }
}
