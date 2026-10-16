using System.IO;
using pk3DS.Core;
using pk3DS.Core.Randomizers;
using pk3DS.Core.Structures;
using System.Security.Cryptography;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed class GameEvolutionService : ArchiveProjectEditor,ITemplateProjectEditor
{
    public GameEvolutionService(GameProjectSession project):base(project,"evolution")
    {
        var records=Records()["evolution"];var species=Config.GetText(TextName.SpeciesNames);
        Entries=Enumerable.Range(1,records.Length-1).Select(i=>new RecordEntry(i,
            Project.Entries.FirstOrDefault(p=>p.Index==i)?.Name??(i<species.Length?species[i]:$"Entrada {i}"))).ToArray();
    }
    public override string Title=>"Evoluciones";
    public string TemplateButton=>"Template de evoluciones";
    public string DefaultTemplatePath=>Path.Combine(AppContext.BaseDirectory,"custom_balance_templates",$"evolutions_gen{Config.Generation}.csv");
    public EditorProposal PreviewTemplate(string path,IReadOnlyList<int> scope)
    {
        path=Path.GetFullPath(path);byte[] source=File.ReadAllBytes(path);byte[] hash=System.Security.Cryptography.SHA256.HashData(source);
        int[] ids=scope.Distinct().Order().ToArray();
        if(ids.Length==0||ids.Any(id=>!Entries.Any(e=>e.Id==id)))throw new ArgumentException("El alcance no contiene evoluciones válidas.");
        var names=Config.GetText(TextName.SpeciesNames);var items=Config.GetText(TextName.ItemNames);
        var rows=EvolutionTemplates.Load(path,names,items,Config.Generation);
        var selected=rows.Where(r=>ids.Contains(r.Source)).ToArray();
        if(selected.Length==0)throw new InvalidDataException("No hay filas del template dentro del alcance elegido.");
        var records=Records();var changes=new List<BatchChange>();
        foreach(var group in selected.GroupBy(r=>r.Source))
        {
            int id=group.Key;var before=ReadRecord(id,records);var values=EditorValues.Flatten(before);
            EvolutionTemplates.Apply(values,group,Config.Generation);
            EditorValues.Validate(before,values);WriteRecord(id,values,records);
            changes.AddRange(EditorValues.Diff(id,Entries.First(e=>e.Id==id).Name,before,ReadRecord(id,records)));
        }
        GarcTransaction.ReadUnchanged(path,hash);
        return new(ProposalOwner,"Template de evoluciones",0,Build(records),changes,new Dictionary<string,string>
        {{"ActionId","evolutions.normalize"},{"Scope",ids.Length==Entries.Count?"All":string.Join(",",ids)},
         {"TemplateFile",Path.GetFileName(path)},{"TemplateSha256",Convert.ToHexString(hash)},{"TemplateCSV",System.Text.Encoding.UTF8.GetString(source)}},[(path,hash)]);
    }
    public override IReadOnlyList<EditorGroup> RandomOptions=>[new("Operación",[
        Field("Operation","Motor de evoluciones",0,0,3,[new(0,"Ajustes personalizados"),new(1,"Randomizador original"),new(2,"Evolucionar en cada nivel · original"),new(3,"Eliminar intercambios · original")])]),
        new("Filtros del motor original",[
        Field("OriginalBST","Especies con BST similar",0,0,1),Field("Experience","Mismo crecimiento de experiencia",0,0,1),
        Field("Type","Compartir tipo con especie previa",0,0,1),Field("Legends","Permitir legendarios",1,0,1),Field("Events","Permitir míticos",1,0,1)]),
        new("Ajustes personalizados",[
        Field("Species","Randomizar destinos de evolución",1,0,1),
        Field("BST","Tolerancia BST del destino (%) · 0 = libre",30,0,100),
        Field("Trade","Cambiar evoluciones por intercambio a nivel",0,0,1),
        Field("TradeLevel","Nivel para evoluciones por intercambio",37,1,100)])];
    public override EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)
    {
        EditorValues.Validate(RandomOptions,options);
        if(options["Operation"]==0)return base.Preview(options,scope);
        int[] ids=scope.Distinct().Order().ToArray();
        if(ids.Length==0||ids.Any(id=>!Entries.Any(e=>e.Id==id)))throw new ArgumentException("El alcance no contiene evoluciones válidas.");
        var records=Records();var sets=records["evolution"].Select(bytes=>Config.Generation==6?(EvolutionSet)new EvolutionSet6(bytes):new EvolutionSet7(bytes)).ToArray();
        var engine=new EvolutionRandomizer(Config,sets);
        engine.Randomizer.rBST=options["OriginalBST"]==1;engine.Randomizer.rEXP=options["Experience"]==1;
        engine.Randomizer.rType=options["Type"]==1;engine.Randomizer.L=options["Legends"]==1;engine.Randomizer.E=options["Events"]==1;
        int seed=RandomNumberGenerator.GetInt32(int.MaxValue);Util.ReseedRand(seed);engine.Randomizer.Initialize();
        switch(options["Operation"]){case 1:engine.Execute();break;case 2:engine.ExecuteEvolveEveryLevel();engine.Execute();break;case 3:engine.ExecuteTrade();break;}
        var changes=new List<BatchChange>();
        foreach(int id in ids){var before=ReadRecord(id,records);records["evolution"][id]=sets[id].Write();changes.AddRange(EditorValues.Diff(id,Entries.First(e=>e.Id==id).Name,before,ReadRecord(id,records)));}
        var parameters=options.ToDictionary(p=>p.Key,p=>p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        parameters["Scope"]=string.Join(",",ids);parameters["Seed"]=seed.ToString();
        parameters["ActionId"]=options["Operation"] switch{1=>"evolutions.randomize",2=>"evolutions.every-level",_=>"evolutions.remove-trade"};
        return new(ProposalOwner,"Evoluciones · motor original",seed,Build(records),changes,parameters);
    }
    protected override IReadOnlyList<EditorGroup> ReadRecord(int id,Dictionary<string,byte[][]> records)
    {
        byte[] record=records["evolution"][id];int size=Config.Generation==7?8:6;
        if(record.Length!=size*8)throw new InvalidDataException("La tabla de evoluciones no tiene ocho entradas válidas.");
        var groups=new List<EditorGroup>();
        for(int i=0;i<8;i++)
        {
            int at=i*size;string k=$"E{i}.";
            var fields=new List<EditorField>{Field(k+"Method","Método",BitConverter.ToUInt16(record,at),0,65535,Methods),
                Field(k+"Argument","Parámetro del método · nivel / objeto / movimiento",BitConverter.ToUInt16(record,at+2),0,65535),
                Field(k+"Species","Especie resultante",BitConverter.ToUInt16(record,at+4),0,Config.MaxSpeciesID,SpeciesChoices)};
            if(Config.Generation==7){fields.Add(Field(k+"Form","Forma resultante · -1 = conservar",unchecked((sbyte)record[at+6]),-1,127));fields.Add(Field(k+"Level","Nivel requerido",record[at+7],0,100));}
            groups.Add(new($"Evolución {i+1}",fields,i==0||record[at]!=0));
        }
        return groups;
    }
    protected override void WriteRecord(int id,IReadOnlyDictionary<string,int> values,Dictionary<string,byte[][]> records)
    {
        byte[] record=records["evolution"][id];int size=Config.Generation==7?8:6;
        for(int i=0;i<8;i++)
        {
            int at=i*size;string k=$"E{i}.";
            foreach(var pair in new[]{("Method",0),("Argument",2),("Species",4)})BitConverter.GetBytes((ushort)values[k+pair.Item1]).CopyTo(record,at+pair.Item2);
            if(Config.Generation==7){record[at+6]=unchecked((byte)(sbyte)values[k+"Form"]);record[at+7]=(byte)values[k+"Level"];}
        }
    }
    protected override Dictionary<string,int> Randomize(int id,IReadOnlyDictionary<string,int> options,Dictionary<string,byte[][]> records,Random random)
    {
        var values=EditorValues.Flatten(ReadRecord(id,records));
        for(int i=0;i<8;i++)
        {
            string k=$"E{i}.";if(values[k+"Method"]==0)continue;
            if(options["Species"]==1&&values[k+"Species"]>0){values[k+"Species"]=PickSpecies(values[k+"Species"],random,options["BST"]);if(Config.Generation==7)values[k+"Form"]=0;}
            if(options["Trade"]==1&&values[k+"Method"] is 5 or 6 or 7)
            {values[k+"Method"]=4;values[k+"Argument"]=Config.Generation==6?options["TradeLevel"]:0;if(Config.Generation==7)values[k+"Level"]=options["TradeLevel"];}
        }
        return values;
    }
    private static readonly string[] MethodNames=["Ninguna","Amistad","Amistad de día","Amistad de noche","Subir de nivel","Intercambio","Intercambio con objeto","Intercambio Shelmet/Karrablast","Usar objeto","Ataque > Defensa","Ataque = Defensa","Ataque < Defensa","Azar < 5","Azar ≥ 5","Ninjask","Shedinja","Belleza","Objeto · macho","Objeto · hembra","Objeto equipado de día","Objeto equipado de noche","Conocer movimiento","Especie en el equipo","Nivel · macho","Nivel · hembra","Zona eléctrica","Bosque","Zona fría","Consola invertida","Afecto y tipo de movimiento","Tipo Siniestro en el equipo","Lluvia","Nivel de día","Nivel de noche","Hembra · forma 1","Sin uso","Versión del juego","De día · versión","De noche · versión","Monte","Atardecer","Ultraespacio · nivel","Ultraespacio · objeto"];
    private IReadOnlyList<MoveChoice> Methods=>MethodNames.Take(Config.Generation==6?34:Config.USUM?43:40).Select((n,i)=>new MoveChoice(i,n)).ToArray();
}
