using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;

/// <summary>Optional real-data check. Source ROMs are read-only; Apply uses new private copies.</summary>
internal static class CatchZoneRealCopyChecks
{
    internal static void Run(string output,IEnumerable<string> roots,bool batch=false)
    {
        output=Path.GetFullPath(output);
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("La carpeta de copias debe ser nueva.");
        var sourceRoots=roots.Select(Path.GetFullPath).ToArray();
        foreach(string root in sourceRoots)
            if(output.Equals(root,StringComparison.OrdinalIgnoreCase)||output.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
                ||root.StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Las copias deben estar separadas de las ROMs originales.");
        Directory.CreateDirectory(output);
        foreach(string root in sourceRoots)
        {
            string[] inventory=Snapshot(root);
            var source=GameProjectSession.Open(root,recoverInterruptedSave:false);var config=source.GameConfiguration;
            string family=config.ORAS?"ORAS":"XY";
            if(source.Generation!=6)throw new InvalidDataException("Solo XY/ORAS.");
            var manifest=CatchZonePatchEngine.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"catch_zone_patches",family+".json")),family);
            var required=new[]{"personal","levelup","gametext","move","evolution","encdata","mapGR"}
                .Select(k=>Path.Combine(config.RomFS,config.GetGARCFileName(k)))
                .Concat(manifest.Files!.Where(f=>f.Scope=="raw").Select(f=>Path.Combine(config.RomFS,f.Path)))
                .Concat(Directory.GetFiles(config.ExeFS)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var fingerprints=required.ToDictionary(p=>p,Hash,StringComparer.OrdinalIgnoreCase);
            string copy=Path.Combine(output,Path.GetFileName(root));
            if(Directory.Exists(copy))throw new IOException("Raíz de prueba repetida.");
            foreach(string path in required)
            {
                string target=Path.Combine(copy,Path.GetRelativePath(root,path));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(path,target,false);
            }
            var game=new GameConfig(config.Version);
            game.Initialize(Path.Combine(copy,Path.GetRelativePath(root,config.RomFS)),Path.Combine(copy,Path.GetRelativePath(root,config.ExeFS)),config.Language);
            var session=Fixture.CreateSession(game,copy,Path.Combine(game.RomFS,game.GetGARCFileName("personal")));
            var service=new GameCatchZoneService(session);
            var before=required.ToDictionary(p=>Path.Combine(copy,Path.GetRelativePath(root,p)),p=>Hash(p),StringComparer.OrdinalIgnoreCase);
            int tablesBefore=new GameEncounterService(session).Entries.Count;
            var preview=service.Preview();
            if(before.Any(p=>Hash(p.Key)!=p.Value))throw new IOException("La vista previa escribió en la copia.");
            if(batch)NativeBatchGen6Bridge.ExecuteSession(session,CatchRecipe(session).ToJson(),37);else service.Apply(preview);
            if(preview.Files.Any(f=>!File.ReadAllBytes(f.Path).SequenceEqual(f.Proposed())))throw new IOException("Guardado distinto de la propuesta.");
            var containers=new Dictionary<string,GARC.MemGARC>();int verified=0,unchangedMembers=0;
            foreach(var scope in new[]{"encdata","mapGR"})
            {
                string path=Path.Combine(game.RomFS,game.GetGARCFileName(scope));
                var original=new GARC.MemGARC(File.ReadAllBytes(Path.Combine(config.RomFS,config.GetGARCFileName(scope))));
                var patched=new GARC.MemGARC(File.ReadAllBytes(path));containers[scope]=patched;
                var selected=manifest.Files!.Where(f=>f.Scope==scope).Select(f=>int.Parse(Regex.Match(f.Path,@"([0-9]+)\.bin$").Groups[1].Value)).ToHashSet();
                if(original.FileCount!=patched.FileCount)throw new IOException("Cambió la cantidad de miembros GARC.");
                for(int i=0;i<original.FileCount;i++)if(!selected.Contains(i))
                {if(!original.GetFile(i).SequenceEqual(patched.GetFile(i)))throw new IOException("Cambió un miembro excluido.");unchangedMembers++;}
            }
            foreach(var file in manifest.Files!)
            {
                byte[] bytes;
                if(file.Scope=="raw")bytes=File.ReadAllBytes(Path.Combine(game.RomFS,file.Path));
                else
                {
                    int index=int.Parse(Regex.Match(file.Path,@"([0-9]+)\.bin$").Groups[1].Value);
                    bytes=containers[file.Scope].GetFile(index);
                    if(file.Path.StartsWith("dec_"))
                    {using var decoded=new MemoryStream();LZSS.Decompress(new MemoryStream(bytes),bytes.Length,decoded);bytes=decoded.ToArray();}
                }
                if(!CatchZonePatchEngine.MatchesTarget(file,bytes.Length,CatchZonePatchEngine.Hash(bytes)))throw new IOException("Target Catch Zones incorrecto: "+file.Path);
                verified++;
            }
            var encounters=new GameEncounterService(session);
            foreach(var entry in encounters.Entries)_=encounters.Read(entry.Id);
            int tablesAfter=encounters.Entries.Count;
            int mirrored=game.ORAS?VerifyShared(game):0;
            var after=before.ToDictionary(p=>p.Key,p=>Hash(p.Key));
            var noop=service.Preview();service.Apply(noop);
            if(noop.Changes.Count!=0||after.Any(p=>Hash(p.Key)!=p.Value)||session.History.Read().Entries.Count!=1)throw new IOException("Catch Zones no es idempotente.");
            EditorProposal? expectedWild=null;Dictionary<string,int>? wildOptions=null;
            if(batch)
            {
                wildOptions=ThreeDS.Progressive.Models.EditorValues.Flatten(encounters.RandomOptions);wildOptions["Species"]=0;wildOptions["ModifyLevel"]=1;wildOptions["LevelScale"]=200;wildOptions["LevelFlat"]=1;
                expectedWild=encounters.Preview(wildOptions,encounters.Entries.Select(e=>e.Id).ToArray(),2055);
            }
            if(game.ORAS)
            {
                var entry=encounters.Entries.Single(e=>e.Name.StartsWith("007 ·",StringComparison.Ordinal));
                var values=ThreeDS.Progressive.Models.EditorValues.Flatten(encounters.Read(entry.Id));
                values["S0.Species"]=values["S0.Species"]==1?2:1;
                encounters.Save(entry.Id,values);
                if(VerifyShared(game)!=mirrored)throw new IOException("La edición nativa perdió tablas EN compartidas.");
                var reread=new GameEncounterService(session);
                if(ThreeDS.Progressive.Models.EditorValues.Flatten(reread.Read(reread.Entries.Single(e=>e.Name.StartsWith("007 ·",StringComparison.Ordinal)).Id))["S0.Species"]!=values["S0.Species"])
                    throw new IOException("La edición nativa no modificó el mapa físico esperado.");
                Console.WriteLine("PASS REAL NATIVE EDIT: "+Path.GetFileName(root)+"; new map 007 saved and reloaded; every shared EN table remains synchronized.");
            }
            if(batch)
            {
                foreach(string path in required)File.Copy(path,Path.Combine(copy,Path.GetRelativePath(root,path)),true);
                var restored=Fixture.CreateSession(game,copy,Path.Combine(game.RomFS,game.GetGARCFileName("personal")));
                var recipe=CatchRecipe(restored);
                GlobalEditorTemplateBridge.Capture(recipe,new GameEncounterService(restored),new(wildOptions!,0),6);
                // Deliberately list Wild first: the bridge must apply Catch Zones before it.
                recipe.SetSection("Actions",new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject{["Id"]="wild-encounters.modify-levels",["Parameters"]=new System.Text.Json.Nodes.JsonObject()},new System.Text.Json.Nodes.JsonObject{["Id"]="catchzones.enable",["Parameters"]=new System.Text.Json.Nodes.JsonObject()}));
                NativeBatchGen6Bridge.ExecuteSession(restored,recipe.ToJson(),37);
                if(expectedWild!.Files.Any(file=>!File.ReadAllBytes(file.Path).SequenceEqual(file.Proposed())))throw new IOException("Batch Catch Zones + Wild no coincide con el resultado manual esperado.");
                if(new GameEncounterService(restored).Entries.Count!=tablesAfter)throw new IOException("Batch perdió tablas de Catch Zones.");
                if(game.ORAS&&VerifyShared(game)!=mirrored)throw new IOException("Batch perdió sincronización EN.");
                Console.WriteLine("PASS REAL BATCH CATCH/WILD: "+Path.GetFileName(root)+"; reversed recipe executes Catch first; Wild exact manual output; all added tables readable; EN synchronized.");
            }
            foreach(var entry in fingerprints)if(Hash(entry.Key)!=entry.Value)throw new IOException("ROM original modificada: "+entry.Key);
            if(!inventory.SequenceEqual(Snapshot(root)))throw new IOException("Inventario original modificado.");
            Console.WriteLine($"PASS REAL COPY: {Path.GetFileName(root)}; family {family}; verified targets {verified}; excluded GARC members identical {unchangedMembers}; readable encounter tables {tablesBefore} -> {tablesAfter}; synchronized EN tables {mirrored}; preview read-only; Apply exact; second Apply no-op; one Catch Zones history entry; source SHA256 {fingerprints.Count} and full inventory unchanged.");
        }
        Console.WriteLine("REAL COPY LIMIT: structural and byte checks passed; capture availability, collision and game scripts still require in-game validation.");
    }
    private static GlobalTemplateDocument CatchRecipe(GameProjectSession session)
    {
        var doc=GlobalTemplateDocument.Empty(session.GameName,6);doc.SetSection("Actions",new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject{["Id"]="catchzones.enable",["Parameters"]=new System.Text.Json.Nodes.JsonObject()}));return doc;
    }
    internal static int VerifyShared(GameConfig game)
    {
        byte[] data=File.ReadAllBytes(Path.Combine(game.RomFS,game.GetGARCFileName("encdata")));
        var raw=new GARC.MemGARC(data);var maps=new GARC.LazyGARC(data);int count=raw.FileCount-2;
        byte[] shared=raw.GetFile(raw.FileCount-1);int header=4+(count+1)*4;
        if(BitConverter.ToUInt16(shared)!=0x4E45||BitConverter.ToUInt16(shared,2)!=count||BitConverter.ToInt32(shared,4)!=header
            ||BitConverter.ToInt32(shared,4+count*4)!=shared.Length)throw new IOException("Cabecera/punteros EN incorrectos.");
        int verified=0;
        for(int i=0;i<count;i++)
        {
            byte[] map=maps[i];int offset=map.Length>=20?BitConverter.ToInt32(map,16):-1;
            int start=BitConverter.ToInt32(shared,4+i*4),end=BitConverter.ToInt32(shared,4+(i+1)*4);
            bool hasTable=offset>=20&&offset<=map.Length-260;
            if(start<header||end<start||end>shared.Length||hasTable!=(end>start))throw new IOException("Mapa y punteros EN incompatibles: "+i);
            if(hasTable)
            {if(end-start!=260||!map.AsSpan(offset+14,244).SequenceEqual(shared.AsSpan(start+14,244)))throw new IOException("Tabla EN no sincronizada con mapa: "+i);verified++;}
        }
        return verified;
    }
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    private static string[] Snapshot(string root)=>Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)
        .Select(p=>{var f=new FileInfo(p);return p+"|"+f.Length+"|"+f.LastWriteTimeUtc.Ticks;}).ToArray();
}
