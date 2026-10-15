using System.IO;
using System.Security.Cryptography;
using pk3DS.Core.Randomizers;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Progressive Gen6 trade profiles; requests and ORAS PC selector are committed together.</summary>
public sealed class GameTrade6Service : IProjectEditor
{
    internal static readonly int[] XYRequests=[0x4450E0,0x445104,0x445128],XYOffers=[0x4450C0,0x4450E4,0x445108];
    internal static readonly int[] ORASRequests=[0x486B04,0x486B28,0x486B4C],ORASOffers=[0x486AE4,0x486B08,0x486B2C,0x486B50];
    internal static readonly ushort[] XYExpected=[659,370,39],ORASExpected=[287,327,182];
    internal static readonly int[] SelectorOffsets=[0x3196C8,0x319F38];
    internal static readonly byte[][] SelectorClean=[Convert.FromHexString("0140A0E1"),Convert.FromHexString("0050D0E5")];
    internal static readonly byte[][] SelectorPatched=[Convert.FromHexString("0340A0E3"),Convert.FromHexString("0350A0E3")];
    private readonly GameProjectSession _project;
    private readonly string _path;
    private byte[] _hash;
    private readonly int[] _requests,_offers,_selectors;
    private readonly ushort[] _expected;
    private readonly Guid _owner=Guid.NewGuid();
    public string Title=>"Intercambios Progressive · Gen6";
    public IReadOnlyList<RecordEntry> Entries {get;}=[new(1,"Aceptar cualquier Pokémon · ofertas")];
    public IReadOnlyList<EditorGroup> RandomOptions=>[new("Acción Progressive",[new("Offers","Randomizar ofertas al habilitar",1,0,1)])];
    public GameTrade6Service(GameProjectSession project)
    {
        if(project.Generation!=6)throw new InvalidDataException("Intercambios Progressive requiere XY u ORAS.");
        _project=project;bool oras=project.GameConfiguration.ORAS;
        _requests=oras?ORASRequests:XYRequests;_offers=oras?ORASOffers:XYOffers;_expected=oras?ORASExpected:XYExpected;
        _path=new[]{".code.bin","code.bin"}.Select(n=>Path.Combine(project.GameConfiguration.ExeFS,n)).FirstOrDefault(File.Exists)
            ??throw new FileNotFoundException("Falta code.bin descomprimido.");
        byte[] code=File.ReadAllBytes(_path);_selectors=oras?Gen6TradeSelectorProfile.Resolve(code):[];Validate(code);_hash=SHA256.HashData(code);
    }
    private void Validate(byte[] code)
    {
        if(code.Length%512!=0 || code.Length<_offers.Concat(_requests).Max()+2)throw new InvalidDataException("code.bin comprimido o incompleto para el perfil conocido.");
        for(int i=0;i<_requests.Length;i++)
        {
            ushort value=BitConverter.ToUInt16(code,_requests[i]);
            if(value!=0&&value!=_expected[i])throw new InvalidDataException($"Solicitud incompatible en 0x{_requests[i]:X}; no se aplicará el parche.");
        }
        if(_project.GameConfiguration.ORAS)for(int i=0;i<2;i++)
            if(!code.AsSpan(_selectors[i],4).SequenceEqual(SelectorClean[i])&&!code.AsSpan(_selectors[i],4).SequenceEqual(SelectorPatched[i]))
                throw new InvalidDataException("Selector PC ORAS desconocido; no se guardará ningún cambio.");
    }
    private byte[] Source()=>GarcTransaction.ReadUnchanged(_path,_hash);
    private IReadOnlyList<EditorGroup> Read(byte[] code)
    {
        var requests=_requests.Select((at,i)=>{int n=BitConverter.ToUInt16(code,at);return new EditorField("Request"+i,$"Solicitud {i+1} · 0 acepta cualquiera",n,n,n);}).ToArray();
        var offers=_offers.Select((at,i)=>{int n=BitConverter.ToUInt16(code,at);return new EditorField("Offer"+i,$"Pokémon ofrecido {i+1}",n,0,Math.Max(n,_project.GameConfiguration.MaxSpeciesID));}).ToArray();
        var status=new List<EditorField>(requests);
        if(_project.GameConfiguration.ORAS)for(int i=0;i<2;i++)
        {int value=code.AsSpan(_selectors[i],4).SequenceEqual(SelectorPatched[i])?1:0;status.Add(new("Selector"+i,$"Selector PC {i+1} · 1 habilitado",value,value,value));}
        return [new("Ofertas · guardar habilita aceptar cualquier Pokémon",offers),new("Estado actual · solo lectura",status,false)];
    }
    public IReadOnlyList<EditorGroup> Read(int id)
    {if(id!=1)throw new ArgumentOutOfRangeException(nameof(id));return Read(Source());}
    private EditorProposal Build(IReadOnlyDictionary<string,int> values,int seed,string action)
    {
        byte[] original=Source();Validate(original);var before=Read(original);EditorValues.Validate(before,values);byte[] output=(byte[])original.Clone();
        for(int i=0;i<_offers.Length;i++)
        {
            int value=values["Offer"+i];int old=BitConverter.ToUInt16(original,_offers[i]);
            if(value>_project.GameConfiguration.MaxSpeciesID&&value!=old)throw new InvalidDataException("Oferta nueva fuera del catálogo.");
            BitConverter.GetBytes((ushort)value).CopyTo(output,_offers[i]);
        }
        foreach(int at in _requests)output.AsSpan(at,2).Clear();
        if(_project.GameConfiguration.ORAS)for(int i=0;i<2;i++)SelectorPatched[i].CopyTo(output,_selectors[i]);
        Validate(output);
        return new(_owner,action,seed,[new FileChange(_path,original,output)],EditorValues.Diff(1,"Intercambios",before,Read(output)),
            new Dictionary<string,string>{{"ActionId","trades6.acceptany"},{"Profile",_project.GameConfiguration.ORAS?"ORAS":"XY"}});
    }
    public void Save(int id,IReadOnlyDictionary<string,int> values)
    {if(id!=1)throw new ArgumentOutOfRangeException(nameof(id));Apply(Build(values,0,"Habilitar intercambios y editar ofertas"));}
    public EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope)=>Preview(options,scope,RandomNumberGenerator.GetInt32(int.MaxValue));
    internal EditorProposal Preview(IReadOnlyDictionary<string,int> options,IReadOnlyList<int> scope,int seed)
    {
        EditorValues.Validate(RandomOptions,options);if(scope.Count!=1||scope[0]!=1)throw new ArgumentException("El alcance requiere todos los intercambios del perfil.");
        var random=new Random(seed);var values=EditorValues.Flatten(Read(1));
        if(options["Offers"]==1)
        {
            var filter=new SpeciesRandomizer(_project.GameConfiguration){rBST=false,rEXP=false,rType=false,L=false,E=false,Shedinja=false};
            int[] pool=filter.GetAllowedSpeciesPool().Where(s=>s>0&&s<=_project.GameConfiguration.MaxSpeciesID).ToArray();
            if(pool.Length==0)throw new InvalidDataException("No hay especies legales para las ofertas.");
            random.Shuffle(pool);int used=0;
            for(int i=0;i<_offers.Length;i++)if(values["Offer"+i]>0&&values["Offer"+i]<=_project.GameConfiguration.MaxSpeciesID)
            {
                int old=values["Offer"+i];
                if(!pool.Any(s=>s!=old))throw new InvalidDataException("No hay una especie distinta para esta oferta.");
                int pick;
                do {if(used==pool.Length){random.Shuffle(pool);used=0;}pick=pool[used++];}while(pick==old);
                values["Offer"+i]=pick;
            }
        }
        return Build(values,seed,options["Offers"]==1?"Habilitar y randomizar ofertas":"Habilitar aceptar cualquier Pokémon");
    }
    public void Apply(EditorProposal proposal)
    {
        if(proposal.Owner!=_owner)throw new InvalidOperationException("La propuesta pertenece a otro editor.");
        _=Source();foreach(var file in proposal.Files)GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);
        _project.History.Commit(proposal.Files,Title,proposal.Action,proposal.Changes,proposal.Parameters);_hash=SHA256.HashData(File.ReadAllBytes(_path));
    }
}
