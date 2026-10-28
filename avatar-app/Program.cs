using System.IO;
using System.Windows;

namespace AvatarStudio;
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
            using var assets = new AssetStore();
            if (args.Contains("--self-test")) return Verification.Run(assets);
            if(args.Contains("--dialogue-self-test") || args.Contains("--transcription-self-test") || args.Contains("--update-self-test"))
            {
                var qa=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                qa.Startup+=async(_,_)=>
                {
                    int code=0;try{if(args.Contains("--update-self-test"))await UpdateVerification.RunAsync();else if(args.Contains("--transcription-self-test"))await TranscriptionVerification.RunAsync(assets);else await DialogueVerification.RunAsync(assets);}catch(Exception ex){File.WriteAllText(args.Contains("--update-self-test")?"update-selftest-error.txt":args.Contains("--transcription-self-test")?"transcription-selftest-error.txt":"dialogue-selftest-error.txt",ex.ToString());code=1;}
                    qa.Shutdown(code);
                };
                return qa.Run();
            }
            var app = new Application();
            return app.Run(new MainWindow(assets, Preferences.Load()));
        }
        catch (Exception ex)
        {
            if (args.Contains("--self-test") || args.Contains("--dialogue-self-test") || args.Contains("--transcription-self-test")) File.WriteAllText(args.Contains("--transcription-self-test")?"transcription-selftest-error.txt":"avatar-selftest-error.txt", ex.ToString());
            else MessageBox.Show(ex.Message, "Avatar Studio");
            return 1;
        }
    }
}
