using ThreeDS.Progressive.Services;
internal static class TextClassificationChecks
{
    internal static void ExportEnglish(string output,IEnumerable<string> roots)
    {
        var result=new Dictionary<string,object>();
        var zoneResult=new Dictionary<string,object>();
        foreach(string root in roots)
        {
            var session=GameProjectSession.Open(root,recoverInterruptedSave:false);var c=session.GameConfiguration;c.Language=2;
            var files=c.GetGARCData("storytext").Files.Select(bytes=>new pk3DS.Core.TextFile(c,bytes).Lines).ToArray();
            result[c.Version.ToString()]=files;
            if(c.Generation==7)
            {
                var reference=c.GameText.First(t=>t.Name==pk3DS.Core.TextName.metlist_000000);
                var names=new pk3DS.Core.TextFile(c,c.GetGARCData("gametext").Files[reference.Index]).Lines;
                var data=c.GetlzGARCData("zonedata")[0];
                zoneResult[c.Version.ToString()]=Enumerable.Range(0,data.Length/0x54).Select(i=>new{zone=i,file=BitConverter.ToInt32(data,i*0x54+0x0C),place=BitConverter.ToInt32(data,i*0x54+0x1C)}).Where(x=>x.place>=0&&x.place<names.Length).Select(x=>new{x.zone,x.file,location=names[x.place]}).ToArray();
            }
        }
        System.IO.File.WriteAllText(output,System.Text.Json.JsonSerializer.Serialize(result));
        System.IO.File.WriteAllText(output+".zones.json",System.Text.Json.JsonSerializer.Serialize(zoneResult));
    }
    internal static void Diagnose(string root)
    {
        var session=GameProjectSession.Open(root,recoverInterruptedSave:false);var c=session.GameConfiguration;c.Language=6;
        foreach(bool story in new[]{false,true})
        {
            var records=c.GetGARCData(story?"storytext":"gametext").Files;
            Console.WriteLine($"BANK {c.Version} story={story} count={records.Length}");
            for(int i=0;i<records.Length;i++)
            {
                var lines=new pk3DS.Core.TextFile(c,records[i]).Lines;
                var samples=lines.Where(t=>!string.IsNullOrWhiteSpace(t)&&t!="[~ 0]").Take(!story&&i==0?50:2).Select(t=>t.Length>110?t[..110]:t);
                Console.WriteLine($"{i}: {lines.Length}: {string.Join(" | ",samples)}");
            }
        }
        if(c.Generation==6)
        {
            var archive=c.GetlzGARCData("encdata");
            Console.WriteLine("ENC COUNT "+archive.FileCount);
            for(int f=0;f<archive.FileCount;f++){var data=archive[f];if(f<3||f>archive.FileCount-5)Console.WriteLine($"ENC {f} size {data.Length} header {Convert.ToHexString(data[..Math.Min(20,data.Length)])}");}
            var zones=archive[0];
            Console.WriteLine($"GEN6 ZONES {zones.Length} remainder {zones.Length%56} bytes {Convert.ToHexString(zones[..Math.Min(60,zones.Length)])}");
            var refs=TextMapResolver.ReadGen6(c,c.GetGARCData("storytext").Files.Length);
            Console.WriteLine($"GEN6 REFERENCES {refs.Count}");
        }
        if(c.Generation==7)
        {
            byte[] zones=c.GetlzGARCData("zonedata")[0];
            for(int i=0;i<Math.Min(zones.Length/0x54,30);i++)Console.WriteLine("ZONE "+i+": "+string.Join(" ",Enumerable.Range(0,0x54/2).Select(j=>BitConverter.ToUInt16(zones,i*0x54+j*2))));
        }
    }
    internal static void ReadReal(IEnumerable<string> roots)
    {
        foreach(string root in roots)
        {
            var session=GameProjectSession.Open(root,recoverInterruptedSave:false);
            session.GameConfiguration.Language=6;
            foreach(bool story in new[]{false,true})
            {
                string path=System.IO.Path.Combine(session.GameConfiguration.RomFS,session.GameConfiguration.GetGARCFileName(story?"storytext":"gametext"));
                byte[] before=System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path));
                var clock=System.Diagnostics.Stopwatch.StartNew();
                var service=new GameTextService(session,story);
                int tagged=service.Entries.Count(e=>service.Tags(e.Id).Count>0);
                if(tagged!=service.Entries.Count)throw new Exception("Incomplete functional catalogue for "+session.GameName+" "+service.BankName);
                if(story&&!service.Entries.Any(e=>service.Classification(e.Id).Evidence.Any(v=>v.Source.StartsWith("https://bulbapedia.bulbagarden.net/",StringComparison.Ordinal))))throw new Exception("No independently matched quote metadata for "+session.GameName);
                Console.WriteLine("UNCLASSIFIED: "+string.Join(",",service.Entries.Where(e=>service.Tags(e.Id).Count==0).Select(e=>e.Id)));
                Console.WriteLine("MAP REFERENCES: "+service.Entries.Count(e=>service.MapReferences(e.Id).Count>0));
                Console.WriteLine($"{session.GameName} {service.BankName}: {tagged}/{service.Entries.Count} classified; {clock.Elapsed.TotalSeconds:F2}s");
                foreach(var group in service.Entries.SelectMany(e=>service.Tags(e.Id)).GroupBy(t=>t)) Console.WriteLine($"  {group.Key}: {group.Count()}");
                if(!before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path))))throw new Exception("Read-only audit changed ROM text");
            }
        }
    }
    internal static void RunUi(Action<bool,string> check)
    {
        ThreeDS.Progressive.StudioDialog.AutomatedChecks=true;
        var app=new ThreeDS.Progressive.App();app.InitializeComponent();
        foreach(var version in new[]{pk3DS.Core.GameVersion.XY,pk3DS.Core.GameVersion.ORAS,pk3DS.Core.GameVersion.SM,pk3DS.Core.GameVersion.USUM})
        {
            using var f=new Fixture(version);ModuleChecks.CreateArchives(f);
            foreach(bool story in new[]{false,true})
            {
                var view=new ThreeDS.Progressive.GameTextView(f.Session,story);
                var field=typeof(ThreeDS.Progressive.GameTextView).GetField("_evidence",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                var evidence=(System.Windows.Controls.ListBox)field.GetValue(view)!;
                check(evidence.ItemsSource is not null&&!view.HasPendingChanges,version+" "+(story?"Story":"Game")+" evidence UI initializes without pending edits or window");
            }
        }
    }
    internal static void Run(Action<bool,string> check)
    {
        string expected=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(TextContentClassifier.Normalize("Unique local test sentence."))));
        check(KnownStoryTextCatalogue.MatchesEnglishFile(["Unique local test sentence."],expected),"Reviewed metadata validates English file fingerprint");
        check(!KnownStoryTextCatalogue.MatchesEnglishFile(["Changed local sentence."],expected)&&!KnownStoryTextCatalogue.MatchesEnglishFile(["Unique local test sentence.","Added row"],expected),"Changed English content or shape invalidates reviewed classification");
        byte[] zone7=new byte[0x54];BitConverter.GetBytes(5).CopyTo(zone7,0x0C);BitConverter.GetBytes(1).CopyTo(zone7,0x1C);
        check(TextMapResolver.ParseGen7(zone7,["","Isla de prueba"],6)[5].Single().Location=="Isla de prueba","Gen7 resolves text-bank and parent-map fields");
        check(TextMapResolver.ParseGen7(zone7,["","Isla"],5).Count==0&&TextMapResolver.ParseGen7(new byte[83],["Isla"],10).Count==0,"Gen7 out-of-range or truncated references ignored");
        check(TextContentClassifier.Classify("SM",["n/a"]).Tags.Contains("Vacíos o reservados"),"Explicit unavailable marker not treated as dialogue");
        byte[] zone=new byte[56];BitConverter.GetBytes((ushort)2).CopyTo(zone,6);BitConverter.GetBytes((ushort)(1|0xFC00)).CopyTo(zone,0x1C);
        var resolved=TextMapResolver.ParseGen6(zone,["", "Pueblo de prueba"],3);
        check(resolved[2].Single().Location=="Pueblo de prueba","Gen6 resolves exact text index and masked parent location");
        check(TextMapResolver.ParseGen6(zone,[],3).Count==0&&TextMapResolver.ParseGen6(zone,["","Pueblo"],2).Count==0,"Invalid map and text references rejected");
        check(TextMapResolver.ParseGen6(new byte[55],["Lugar"],100).Count==0,"Truncated zone table ignored");
        check(KnownTextCatalogue.Find("XY",151,13)?.Category=="Movimientos y habilidades" && KnownTextCatalogue.Find("USUM",127,13)?.Category=="Entrenadores y combate","Same index has version-specific purpose");
        check(KnownTextCatalogue.Find("XY",152,13) is null,"Custom bank count never silently uses vanilla index catalogue");
        check(TextContentClassifier.Classify("USUM",["[~ 1]", ""]).Tags.Contains("Vacíos o reservados"),"Reserved messages distinguished from dialogue");
        check(TextContentClassifier.Classify("USUM",["[VAR TRNAME(0000)]"]).Tags.Contains("Solo códigos de control"),"Control-only messages distinguished from readable text");
        check(GameTextService.TagChoices.Distinct().Count()==GameTextService.TagChoices.Length,"Filter categories contain no duplicate labels");
        var result=TextContentClassifier.Classify("ORAS",["Sin coincidencias", "¿Me cambias tu Pokémon?", "Vamos a Ciudad Férrica."]);
        check(result.Tags.Contains("Intercambios") && result.Evidence.Any(e=>e.Category=="Intercambios"&&e.Line==1),"Trade detection records original line");
        check(result.Evidence.Any(e=>e.Match=="Ciudad Férrica"&&e.Line==2),"ORAS place mention keeps evidence");
        check(!TextContentClassifier.Classify("XY",["Vamos a Ciudad Férrica"]).Tags.Contains("Lugares mencionados"),"Version-specific context does not leak between games");
        check(!TextContentClassifier.Classify("USUM",["poniente y magnolias"]).Tags.Any(),"Word boundaries reject incidental substrings");
        check(TextContentClassifier.Classify("USUM",["[VAR TRNAME(0000)] ¡LYLIA!\\nHola"]).Tags.Contains("Personajes mencionados"),"Controls, case and line codes do not hide mentions");
        check(TextContentClassifier.Classify("XY",["Caja 1","Caja 2","Caja 3"]).Tags.Contains("Cajas del PC"),"Box name catalogue identified without hardcoded index");
        check(!TextContentClassifier.Classify("XY",["Caja 1","Alguien habla","Caja 2"]).Tags.Contains("Cajas del PC"),"Mixed dialogue not confused with box-name catalogue");
        check(!TextContentClassifier.Classify("USUM",["Unidad Ultra"]).Tags.Contains("Historia principal"),"Character mentions never asserted to be main story or speaker");
        check(TextContentClassifier.Classify("SM",["Equipo Rainbow Rocket"]).Tags.Count==0 && TextContentClassifier.Classify("USUM",["Equipo Rainbow Rocket"]).Tags.Count>0,"USUM-exclusive context absent in SM");
        check(TextContentClassifier.Normalize("Férrica") == TextContentClassifier.Normalize("FERRICA"),"Accent-insensitive catalogue filtering");
        foreach(string game in new[]{"XY","ORAS","SM","USUM"})
        {
            using var f=new Fixture(Enum.Parse<pk3DS.Core.GameVersion>(game));ModuleChecks.CreateArchives(f);
            var config=f.Game;string path=ModuleChecks.PathFor(f,"storytext");
            var records=new[]{pk3DS.Core.TextFile.GetBytes(config,["¿Me cambias tu Pokémon?"],config.RemapCharacters)};
            f.WriteGarc(path,records,config.GetGARCReference("storytext"));
            byte[] original=System.IO.File.ReadAllBytes(path);
            var service=new GameTextService(f.Session,true);
            check(service.Tags(0).Contains("Intercambios")&&service.Entries[0].Name.Contains("Intercambios"),game+" automatic content category in actual encrypted bank");
            check(service.CatalogueMatches(0,TextContentClassifier.Normalize("Intercambios")),game+" catalogue index finds automatic tags beyond displayed title");
            check(service.ClassificationDetails(0).Contains("línea 0")&&System.IO.File.ReadAllBytes(path).SequenceEqual(original),game+" classification read-only with evidence");
        }
    }
}
