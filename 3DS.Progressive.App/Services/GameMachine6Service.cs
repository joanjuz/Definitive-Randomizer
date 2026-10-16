using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>XY/ORAS TM01–100 and HM01–05/07, including localized item descriptions.</summary>
public sealed class GameMachine6Service : IProjectEditor
{
    internal static readonly byte[] Signature=[0xD4,0x00,0xAE,0x02,0xAF,0x02,0xB0,0x02];
    private readonly GameProjectSession _project;
    private readonly string _codePath,_textPath;
    private readonly GARCReference _textRef;
    private byte[] _codeHash,_textHash;
    private readonly int _offset,_itemIndex,_moveIndex;
    private readonly int[] _indices,_items;
    private readonly Guid _owner=Guid.NewGuid();
    private readonly IReadOnlyList<MoveChoice> _choices;
    public string Title=>"MT y MO · Gen6";
    public IReadOnlyList<RecordEntry> Entries {get;}=[new(1,"Tabla completa MT / MO")];
    private static EditorField F(string key,string label,int value,int min=0,int max=1)=>new(key,label,value,min,max);
    public IReadOnlyList<EditorGroup> RandomOptions=>[new("Opciones vanilla",[
        F("Tms","Randomizar MT",1),F("Field","Incluir movimientos de campo",0),
        F("Hms","Randomizar MO · puede impedir avanzar en la historia",0),
        new("HmRestriction","Restricción para olvidar MO",0,0,2,[new(0,"Conservar estado actual"),new(1,"Permitir olvidar MO"),new(2,"Restaurar restricción original")])])];
    public GameMachine6Service(GameProjectSession project)
    {
        if(project.Generation!=6)throw new ArgumentException("Este editor requiere XY u ORAS.");
        _project=project;var game=project.GameConfiguration;
        _choices=project.MoveNames.Take(ushort.MaxValue+1).Select((n,i)=>new MoveChoice(i,n)).ToArray();
        _codePath=new[]{".code.bin","code.bin"}.Select(n=>Path.Combine(game.ExeFS,n)).FirstOrDefault(File.Exists)
            ??throw new FileNotFoundException("Falta code.bin descomprimido.");
        byte[] code=File.ReadAllBytes(_codePath);_codeHash=SHA256.HashData(code);_offset=FindTable(code,game.ORAS);
        _indices=[..Enumerable.Range(0,92),..Enumerable.Range(game.ORAS?98:97,8),..Enumerable.Range(92,5),..(game.ORAS?new[]{97,106}:Array.Empty<int>())];
        _items=[..Enumerable.Range(328,92),618,619,620,690,691,692,693,694,..Enumerable.Range(420,5),..(game.ORAS?new[]{425,737}:Array.Empty<int>())];
        _itemIndex=game.GameText.First(t=>t.Name==TextName.ItemFlavor).Index;
        _moveIndex=game.GameText.First(t=>t.Name==TextName.MoveFlavor).Index;
        var original=game.GetGARCReference("gametext");_textRef=original.LanguageVariant?original.GetRelativeGARC(game.Language):original;
        _textPath=Path.Combine(game.RomFS,game.GetGARCFileName("gametext"));
        byte[] text=File.ReadAllBytes(_textPath);_textHash=SHA256.HashData(text);
        _=Read(1);_=Descriptions(text);
    }
    internal static int FindTable(byte[] code,bool oras)
    {
        if(code.Length%512!=0||code.Length<0x400000+Signature.Length)
            throw new InvalidDataException("code.bin comprimido o incompleto.");
        int found=code.AsSpan(0x400000).IndexOf(Signature);
        if(found<0||code.AsSpan(0x400000+found+1).IndexOf(Signature)>=0)
            throw new InvalidDataException("La firma MT/MO Gen6 no es única o no existe.");
        int offset=0x400000+found+Signature.Length;
        if((long)offset+2*(oras?107:105)>code.Length)throw new InvalidDataException("Tabla MT/MO truncada.");
        return offset;
    }
    private byte[] Code()=>GarcTransaction.ReadUnchanged(_codePath,_codeHash);
    private byte[] Text()=>GarcTransaction.ReadUnchanged(_textPath,_textHash);
    private string Key(int i)=>i<100?$"TM{i+1:000}":$"HM{i-99:00}";
    private string Label(int i)=>i<100?$"MT {i+1:000}":$"MO {i-99:00}";
    private IReadOnlyList<EditorGroup> Read(byte[] code)
    {
        var fields=_indices.Select((index,i)=>new EditorField(Key(i),Label(i),BitConverter.ToUInt16(code,_offset+index*2),0,_choices.Count-1,_choices)).ToArray();
        if(fields.Any(f=>f.Value>=_choices.Count))throw new InvalidDataException("Movimiento de MT/MO fuera del catálogo.");
        var restriction=Gen6HmRestriction.Locate(code,_project.GameConfiguration.ORAS);
        return [new("MT 001–050",fields[..50]),new("MT 051–100",fields[50..100],false),new("MO · movimientos de campo",fields[100..],false),
            new("Restricción MO",[F("ForgetHms",restriction.Offset<0?"MO olvidables · firma no disponible":"Permitir olvidar MO",restriction.Patched?1:0,0,restriction.Offset<0?0:1)],false)];
    }
    public IReadOnlyList<EditorGroup> Read(int id)
    {if(id!=1)throw new ArgumentOutOfRangeException(nameof(id));_=Text();return Read(Code());}
    private (byte[][] Records,string[] Items,string[] Moves) Descriptions(byte[] text)
    {
        var game=_project.GameConfiguration;byte[][] records=new GARC.MemGARC(text).Files;
        if(_itemIndex>=records.Length||_moveIndex>=records.Length)throw new InvalidDataException("Banco de textos incompleto.");
        string[] items=new TextFile(game,records[_itemIndex],game.RemapCharacters).Lines;
        string[] moves=new TextFile(game,records[_moveIndex],game.RemapCharacters).Lines;
        if(_items.Any(i=>i>=items.Length))throw new InvalidDataException("Faltan las descripciones de MT/MO.");
        return(records,items,moves);
    }
    private EditorProposal Build(IReadOnlyDictionary<string,int> values,int seed,string action,IReadOnlyDictionary<string,string> options)
    {
        byte[] code=Code(),text=Text();var before=Read(code);EditorValues.Validate(before,values);
        byte[] next=(byte[])code.Clone();var decoded=Descriptions(text);string[] old=(string[])decoded.Items.Clone();
        for(int i=0;i<_indices.Length;i++)
        {
            int move=values[Key(i)];if(move>=decoded.Moves.Length)throw new InvalidDataException("Falta la descripción del movimiento elegido.");
            BitConverter.GetBytes((ushort)move).CopyTo(next,_offset+_indices[i]*2);decoded.Items[_items[i]]=decoded.Moves[move];
        }
        Gen6HmRestriction.Set(next,_project.GameConfiguration.ORAS,values["ForgetHms"]==1);
        var changes=EditorValues.Diff(1,"MT / MO",before,Read(next)).ToList();byte[] textOutput=text;
        if(!old.SequenceEqual(decoded.Items))
        {
            var game=_project.GameConfiguration;byte[] encoded=new TextFile(game,remapChars:game.RemapCharacters){PreserveWhitespace=true,Lines=decoded.Items}.Data;
            var confirmed=new TextFile(game,encoded,game.RemapCharacters).Lines;
            if(!confirmed.SequenceEqual(decoded.Items))
            {
                int mismatch=Enumerable.Range(0,decoded.Items.Length).First(i=>confirmed[i]!=decoded.Items[i]);
                throw new InvalidDataException($"Descripción no reversible en objeto {mismatch}: {System.Text.Json.JsonSerializer.Serialize(decoded.Items[mismatch])} → {System.Text.Json.JsonSerializer.Serialize(confirmed[mismatch])}.");
            }
            decoded.Records[_itemIndex]=encoded;textOutput=GarcTransaction.Build(text,_textRef,decoded.Records,preserveRecordSize:false);
            foreach(int i in _items.Where(i=>old[i]!=decoded.Items[i]))changes.Add(new(i,"Objeto MT/MO","Descripción",old[i],decoded.Items[i]));
        }
        return new(_owner,action,seed,[new FileChange(_codePath,code,next),new FileChange(_textPath,text,textOutput)],changes,options);
    }
    public void Save(int id,IReadOnlyDictionary<string,int> values)
    {
        using var manual=ManualOperationScope.Record(this,id,values);if(id!=1)throw new ArgumentOutOfRangeException(nameof(id));Apply(Build(values,0,"Editar MT/MO",new Dictionary<string,string>{{"ActionId","machines6.edit"}}));}
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope) => Preview(options,scope,RandomNumberGenerator.GetInt32(int.MaxValue));
    internal EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope,int seed)
    {
        EditorValues.Validate(RandomOptions,options);
        if(scope.Count!=1||scope[0]!=1)throw new ArgumentException("El alcance requiere la tabla MT/MO completa.");
        var values=EditorValues.Flatten(Read(1));var random=new Random(seed);
        int[] field=[15,19,57,70,127,249,291,148,290];int[] banned=[165,621];
        int[] pool=Enumerable.Range(1,_choices.Count-1).Where(i=>!banned.Contains(i)).ToArray();random.Shuffle(pool);
        var used=new HashSet<int>();
        for(int i=0;i<_indices.Length;i++)
        {
            if(i<100&&(options["Tms"]==0||(options["Field"]==0&&field.Contains(values[Key(i)]))))continue;
            if(i>=100&&options["Hms"]==0)continue;
            int pick=pool.FirstOrDefault(m=>!used.Contains(m)&&(i>=100||options["Field"]==1||!field.Contains(m)));
            if(pick==0)throw new InvalidDataException("No hay suficientes movimientos para el alcance elegido.");
            used.Add(pick);values[Key(i)]=pick;
        }
        if(options["HmRestriction"]!=0)values["ForgetHms"]=options["HmRestriction"]==1?1:0;
        var parameters=options.ToDictionary(p=>p.Key,p=>p.Value.ToString());parameters["ActionId"]="machines6.randomize";parameters["Scope"]="All";
        return Build(values,seed,"Randomizar MT/MO",parameters);
    }
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("La propuesta pertenece a otro editor.");
        _=Code();_=Text();foreach(var file in proposal.Files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        _project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);
        _codeHash=SHA256.HashData(File.ReadAllBytes(_codePath));_textHash=SHA256.HashData(File.ReadAllBytes(_textPath));
        _project.GameConfiguration.GameTextStrings[_itemIndex]=Descriptions(Text()).Items;
        _project.RefreshMoveAssignments();
    }
}
