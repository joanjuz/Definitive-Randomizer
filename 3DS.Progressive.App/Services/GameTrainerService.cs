using System.IO;
using pk3DS.Core;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed class GameTrainerService : ArchiveProjectEditor,IOptionTemplateProjectEditor
{
    private ProgressiveLegacy.TrainerLogicHost? _original;
    public GameTrainerService(GameProjectSession project):base(project,"trdata","trpoke")
    {
        var records=Records();if(records["trdata"].Length!=records["trpoke"].Length)
            throw new InvalidDataException("Las tablas de entrenadores y equipos no coinciden.");
        var names=Config.GetText(TextName.TrainerNames);
        Entries=Enumerable.Range(1,records["trdata"].Length-1)
            .Select(i=>new RecordEntry(i,i<names.Length?names[i]:$"Entrenador {i}")).ToArray();
    }
    public override string Title=>"Entrenadores";
    private ProgressiveLegacy.TrainerLogicHost Original=>_original??=new(Config,Records()["trdata"],Records()["trpoke"]);
    public void SelectOptionsEntry(int id) {if(OperatingSystem.IsWindows())Original.SelectedTrainer=id;}
    public void ImportOptions(string path)=>Original.Restore(File.ReadAllText(path));
    public void RestoreOptions(string json)=>Original.Restore(json);
    public string CaptureOptions(IReadOnlyDictionary<string,int> options)=>Original.Capture(options);
    public void ExportOptions(string path,IReadOnlyDictionary<string,int> options)=>File.WriteAllText(path,Original.Capture(options));
    public override IReadOnlyList<EditorGroup> RandomOptions=>OperatingSystem.IsWindows()?Original.Options():[new("Randomización",[
        Field("Species","Randomizar especies",1,0,1),
        Field("BST","Tolerancia BST (%) · 0 = cualquier BST",30,0,100),
        Field("LevelScale","Escala de niveles (%)",100,1,1000)])];
    public override EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)
    {
        if(!OperatingSystem.IsWindows())return base.Preview(options,scope);
        var input=new Dictionary<string,int>(options);
        if(input.Remove("Species",out int species))input["CHK_RandomPKM"]=species;
        if(input.Remove("LevelScale",out int scale)) {input["CHK_Level"]=1;input["NUD_LevelBoost"]=scale;}
        input.Remove("BST");
        EditorValues.Validate(RandomOptions,input);
        int[] ids=scope.Distinct().Order().ToArray();
        if(ids.Length==0||ids.Any(id=>!Entries.Any(e=>e.Id==id)))throw new ArgumentException("El alcance no contiene entrenadores válidos.");
        var records=Records();var dependencies=new List<(string Path,byte[] Fingerprint)>();
        foreach(string name in new[]{"personal","move","levelup","evolution","eggmove","encounterstatic"})
        {
            var reference=Config.GetGARCReference(name);if(reference is null)continue;
            string path=Path.Combine(Config.RomFS,reference.Reference);
            if(File.Exists(path))dependencies.Add((path,System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        }
        foreach(string path in (Directory.Exists(Config.ExeFS)?Directory.EnumerateFiles(Config.ExeFS):[]).Where(p=>Path.GetFileNameWithoutExtension(p).Contains("code")))
            dependencies.Add((path,System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        Config.InitializePersonal();Config.InitializeMoves();Config.InitializeLearnset();Config.InitializeEvos();
        int seed=System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue);
        var engine=new ProgressiveLegacy.TrainerLogicHost(Config,records["trdata"],records["trpoke"]);
        engine.Restore(Original.Capture(input));
        var output=engine.Run(input,seed);
        var changes=new List<BatchChange>();
        foreach(int id in ids)
        {
            var before=ReadRecord(id,records);
            records["trdata"][id]=output.Headers[id];records["trpoke"][id]=output.Teams[id];
            changes.AddRange(EditorValues.Diff(id,Entries.First(e=>e.Id==id).Name,before,ReadRecord(id,records)));
        }
        foreach(var d in dependencies)GarcTransaction.ReadUnchanged(d.Path,d.Fingerprint);
        return new(ProposalOwner,"Randomizar Entrenadores",seed,Build(records),changes,
            new Dictionary<string,string>{{"ActionId","trainers.randomize"},{"Scope",ids.Length==Entries.Count?"All":string.Join(",",ids)},
                {"Controls",System.Text.Json.JsonSerializer.Serialize(input)},{"TrainerTemplate",engine.Capture(input)}},dependencies);
    }
    protected override IReadOnlyList<EditorGroup> ReadRecord(int id,Dictionary<string,byte[][]> records)
    {
        byte[] header=records["trdata"][id],team=records["trpoke"][id];int o=Config.ORAS?4:0;
        if(header.Length<0x14+o)throw new InvalidDataException("Cabecera de entrenador incompleta.");
        int count=header[3+o];if(count>6)throw new InvalidDataException("El entrenador tiene más de seis Pokémon.");
        int flags=Config.ORAS?U16(header,0):header[0],classId=Config.ORAS?U16(header,2):Config.Generation==7?U16(header,0):header[1];
        var fields=new List<EditorField> {
            Field("Class","Clase de entrenador",classId,0,Config.XY?255:65535,Choices(TextName.TrainerClasses)),
            Field("Mode","Modo de combate",header[2+o],0,255,[new(0,"Individual"),new(1,"Doble"),new(2,"Múltiple")]),
            Field("Count","Pokémon en el equipo",count,0,6),Field("AI","IA · máscara de bits",header[12+o]),
            Field("Money","Multiplicador de dinero",header[17+o])};
        for(int j=0;j<4;j++)fields.Add(Field("Item"+j,$"Objeto de combate {j+1}",U16(header,4+o+2*j),0,65535,Choices(TextName.ItemNames)));
        if(Config.Generation==7)fields.Add(Field("Flag","Flag del entrenador",header[13]));
        else
        {
            fields.Add(Field("HasItems","Objetos equipados",(flags>>1)&1,0,1));
            fields.Add(Field("HasMoves","Movimientos personalizados",flags&1,0,1));
            fields.Add(Field("Healer","Cura después del combate",header[16+o]!=0?1:0,0,1));
            fields.Add(Field("Prize","Premio",U16(header,18+o),0,65535,Choices(TextName.ItemNames)));
        }
        var groups=new List<EditorGroup> {new("Datos del entrenador",fields)};
        int stride=Config.Generation==7?0x20:count>0?team.Length/count:8+(((flags>>1)&1)==1?2:0)+((flags&1)==1?8:0);
        if(count>0&&(team.Length%count!=0||stride<(Config.Generation==7?0x20:8+((flags&2)!=0?2:0)+((flags&1)!=0?8:0))))
            throw new InvalidDataException("El equipo del entrenador está incompleto.");
        for(int slot=0;slot<6;slot++)
        {
            string k=$"P{slot}.";byte[] raw=slot<count?team.Skip(slot*stride).Take(stride).ToArray():new byte[stride];
            var f=new List<EditorField>();
            if(Config.Generation==7)
            {
                var p=new TrainerPoke7(raw);
                f.Add(Field(k+"Species","Especie",slot<count?p.Species:1,1,Config.MaxSpeciesID,SpeciesChoices));
                f.Add(Field(k+"Form","Forma",p.Form,0,255));f.Add(Field(k+"Level","Nivel",slot<count?p.Level:1,1,100));
                f.Add(Field(k+"Gender","Género",p.Gender,0,3));f.Add(Field(k+"Ability","Slot de habilidad",p.Ability,0,3));
                f.Add(Field(k+"Nature","Naturaleza",p.Nature,0,255,Choices(TextName.Natures)));
                f.Add(Field(k+"Shiny","Shiny",p.Shiny?1:0,0,1));
                for(int j=0;j<6;j++){f.Add(Field(k+"IV"+j,"IV "+StatNames[j],p.IVs[j],0,31));f.Add(Field(k+"EV"+j,"EV "+StatNames[j],p.EVs[j]));}
                f.Add(Field(k+"Item","Objeto equipado",p.Item,0,65535,Choices(TextName.ItemNames)));
                for(int j=0;j<4;j++)f.Add(Field(k+"Move"+j,$"Movimiento {j+1}",p.Moves[j],0,65535,Choices(TextName.MoveNames)));
            }
            else
            {
                f.Add(Field(k+"Species","Especie",slot<count?U16(raw,4):1,1,Config.MaxSpeciesID,SpeciesChoices));
                f.Add(Field(k+"Form","Forma",U16(raw,6),0,65535));f.Add(Field(k+"Level","Nivel",slot<count?U16(raw,2):1,1,100));
                f.Add(Field(k+"Gender","Género",raw[1]&3,0,3));f.Add(Field(k+"Ability","Slot de habilidad",raw[1]>>4,0,15));
                f.Add(Field(k+"IVs","IV combinado (0–255)",raw[0]));int pos=8;
                f.Add(Field(k+"Item","Objeto equipado",(flags&2)!=0?U16(raw,pos):0,0,65535,Choices(TextName.ItemNames)));if((flags&2)!=0)pos+=2;
                for(int j=0;j<4;j++)f.Add(Field(k+"Move"+j,$"Movimiento {j+1}",(flags&1)!=0?U16(raw,pos+j*2):0,0,65535,Choices(TextName.MoveNames)));
            }
            groups.Add(new($"Pokémon {slot+1}"+(slot<count?"":" · fuera del equipo"),f,slot<count));
        }
        return groups;
    }
    protected override void WriteRecord(int id,IReadOnlyDictionary<string,int> v,Dictionary<string,byte[][]> records)
    {
        byte[] h=(byte[])records["trdata"][id].Clone(),old=records["trpoke"][id];int o=Config.ORAS?4:0,count=v["Count"],previous=h[3+o];
        if(v["Mode"]==1&&count<2)throw new ArgumentException("Un combate doble necesita al menos dos Pokémon.");
        int flags=Config.ORAS?U16(h,0):h[0],oldStride=Config.Generation==7?32:previous>0?old.Length/previous:8;
        if(Config.Generation==7)Set16(h,0,v["Class"]);
        else if(Config.ORAS){Set16(h,0,(flags&~3)|v["HasMoves"]|(v["HasItems"]<<1));Set16(h,2,v["Class"]);}
        else {h[0]=(byte)((flags&~3)|v["HasMoves"]|(v["HasItems"]<<1));h[1]=(byte)v["Class"];}
        h[2+o]=(byte)v["Mode"];h[3+o]=(byte)count;h[12+o]=(byte)v["AI"];h[17+o]=(byte)v["Money"];
        for(int j=0;j<4;j++)Set16(h,4+o+j*2,v["Item"+j]);
        if(Config.Generation==7)h[13]=(byte)v["Flag"];
        else{if((h[16+o]!=0)!=(v["Healer"]==1))h[16+o]=(byte)v["Healer"];Set16(h,18+o,v["Prize"]);}
        int originalLength=8+((flags&2)!=0?2:0)+((flags&1)!=0?8:0);
        int extra=Config.Generation==7?0:Math.Max(0,oldStride-originalLength);
        int stride=Config.Generation==7?32:8+(v["HasItems"]==1?2:0)+(v["HasMoves"]==1?8:0)+extra;
        byte[] team=new byte[stride*count];
        for(int i=0;i<count;i++)
        {
            string k=$"P{i}.";byte[] raw=i<previous?old.Skip(i*oldStride).Take(oldStride).ToArray():new byte[oldStride];
            if(Config.Generation==7)
            {
                var p=new TrainerPoke7(raw) {Species=v[k+"Species"],Form=v[k+"Form"],Level=v[k+"Level"],Gender=v[k+"Gender"],Ability=v[k+"Ability"],Nature=v[k+"Nature"],Shiny=v[k+"Shiny"]==1,Item=v[k+"Item"]};
                p.IVs=Enumerable.Range(0,6).Select(j=>v[k+"IV"+j]).ToArray();p.EVs=Enumerable.Range(0,6).Select(j=>v[k+"EV"+j]).ToArray();
                p.Moves=Enumerable.Range(0,4).Select(j=>v[k+"Move"+j]).ToArray();raw=p.Write();
            }
            else
            {
                byte[] edited=new byte[stride];raw.AsSpan(0,Math.Min(8,raw.Length)).CopyTo(edited);
                if(extra>0&&raw.Length>=originalLength+extra)raw.AsSpan(originalLength,extra).CopyTo(edited.AsSpan(stride-extra));
                edited[0]=(byte)v[k+"IVs"];edited[1]=(byte)((edited[1]&12)|(v[k+"Ability"]<<4)|v[k+"Gender"]);
                Set16(edited,2,v[k+"Level"]);Set16(edited,4,v[k+"Species"]);Set16(edited,6,v[k+"Form"]);int pos=8;
                if(v["HasItems"]==1){Set16(edited,pos,v[k+"Item"]);pos+=2;}
                if(v["HasMoves"]==1)for(int j=0;j<4;j++)Set16(edited,pos+j*2,v[k+"Move"+j]);raw=edited;
            }
            raw.CopyTo(team,i*stride);
        }
        records["trdata"][id]=h;records["trpoke"][id]=team;
    }
    protected override Dictionary<string,int> Randomize(int id,IReadOnlyDictionary<string,int> options,Dictionary<string,byte[][]> records,Random random)
    {
        var values=EditorValues.Flatten(ReadRecord(id,records));
        for(int i=0;i<values["Count"];i++)
        {
            string k=$"P{i}.";
            if(options["Species"]==1){values[k+"Species"]=PickSpecies(values[k+"Species"],random,options["BST"]);values[k+"Form"]=0;}
            values[k+"Level"]=Math.Clamp((int)Math.Round(values[k+"Level"]*options["LevelScale"]/100.0),1,100);
        }
        return values;
    }
    private static readonly string[] StatNames=["PS","Ataque","Defensa","At. especial","Def. especial","Velocidad"];
    private static int U16(byte[] b,int offset)=>BitConverter.ToUInt16(b,offset);
    private static void Set16(byte[] b,int offset,int value)=>BitConverter.GetBytes((ushort)value).CopyTo(b,offset);
}
