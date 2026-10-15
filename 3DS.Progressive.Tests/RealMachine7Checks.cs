using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using ThreeDS.Progressive.Services;

internal static class RealMachine7Checks
{
    internal static void Run(IEnumerable<string> roots)
    {
        foreach(string root in roots)
        {
            var p=GameProjectSession.Open(root,recoverInterruptedSave:false);var c=p.GameConfiguration;
            if(!c.USUM)throw new IOException("Este diagnóstico requiere USUM.");
            string[] paths=[Directory.EnumerateFiles(c.ExeFS).Single(file=>Path.GetFileName(file).Contains("code",StringComparison.OrdinalIgnoreCase)),Path.Combine(c.RomFS,"Shop.cro"),..new[]{"personal","levelup","evolution","gametext"}.Select(key=>Path.Combine(c.RomFS,c.GetGARCFileName(key)))];
            var hashes=paths.ToDictionary(path=>path,path=>SHA256.HashData(File.ReadAllBytes(path)));
            var a=new GameMachineTutorService(p);a.RandomizeMachines(123);a.RandomizeTutorMoves(123);var tutorA=a.PreviewTutorEdits();var tmA=a.PrepareMachines(true);
            var b=new GameMachineTutorService(p);b.RandomizeMachines(123);b.RandomizeTutorMoves(123);var tutorB=b.PreviewTutorEdits();var tmB=b.PrepareMachines(true);
            if(!tutorA.ShopOutput.SequenceEqual(tutorB.ShopOutput)||!tutorA.CodeOutput.SequenceEqual(tutorB.CodeOutput)||!tmA.Files.Zip(tmB.Files).All(pair=>pair.First.Proposed().SequenceEqual(pair.Second.Proposed())))throw new IOException("La semilla no reproduce MT/tutores.");
            var machines=new Gen7MachineState(true,true,true,true,true,true,true);var marts=new Gen7MartState(true,true,true,true,true,true,true,false);var doc=GlobalTemplateDocument.Empty(p.GameName,7);NativeGen7MachineBridge.Capture(doc,machines);NativeGen7MartBridge.Capture(doc,marts);NativeGen7MachineBridge.ValidateSession(p,doc);_=NativeGen7MartBridge.ValidateSession(p,doc);
            Exception? failure=null;var thread=new Thread(()=>
            {
                try
                {
                    var app=new ThreeDS.Progressive.App();app.InitializeComponent();var main=new ThreeDS.Progressive.MainWindow();main.LoadProject(p);
                    try
                    {
                        var flags=BindingFlags.Instance|BindingFlags.NonPublic;typeof(ThreeDS.Progressive.MainWindow).GetMethod("RestoreGlobalOptions",flags)!.Invoke(main,[doc]);
                        var machineView=(ThreeDS.Progressive.MachineTutorView)typeof(ThreeDS.Progressive.MainWindow).GetMethod("CreateMachineTutorView",flags)!.Invoke(main,null)!;var martView=(ThreeDS.Progressive.MartEditorView)typeof(ThreeDS.Progressive.MainWindow).GetMethod("CreateMartView",flags)!.Invoke(main,null)!;
                        if(machineView.RememberRecipe()!=machines||martView.RememberRecipe()!=marts)throw new IOException("La interfaz no restauró las opciones.");
                        var output=GlobalTemplateDocument.Empty(p.GameName,7);typeof(ThreeDS.Progressive.MainWindow).GetMethod("CaptureGlobalOptions",flags)!.Invoke(main,[output]);if(NativeGen7MachineBridge.Read(output)!=machines||NativeGen7MartBridge.Read(output)!=marts)throw new IOException("La captura WPF no coincide.");
                    }
                    finally{main.Close();}
                }
                catch(Exception e){failure=e;}
            });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure is not null)throw failure;
            if(hashes.Any(pair=>!SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)))throw new IOException("La validación alteró un origen.");
            Console.WriteLine("PASS: "+Path.GetFileName(root)+" native TM/tutor ARM proposals reproduce seed; all machine/shop presets validate; MainWindow and both WPF views roundtrip exact settings; six source hashes intact.");
        }
    }
}
