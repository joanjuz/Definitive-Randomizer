using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
namespace ThreeDS.Progressive.Services;

public sealed partial class GameStaticEncounterService
{
    private sealed record StarterTextBank(string StoryPath,GARCReference StoryReference,byte[] Story,string NamesPath,byte[] Names);
    private readonly List<StarterTextBank> _starterTextBanks=[];
    private EncounterGift7[] _starterBaseline=[];
    public int StarterTextLanguages=>_starterTextBanks.Count;
    private void CaptureStarterText()
    {
        byte[] gifts=Records()["encounterstatic"][0];
        _starterBaseline=Enumerable.Range(0,Math.Min(3,gifts.Length/EncounterGift7.SIZE)).Select(i=>new EncounterGift7(gifts.AsSpan(i*EncounterGift7.SIZE,EncounterGift7.SIZE).ToArray())).ToArray();
        _starterTextBanks.Clear();if(_starterBaseline.Length!=3)return;
        var story=Config.GetGARCReference("storytext");var names=Config.GetGARCReference("gametext");
        for(int language=0;language<10;language++)
        {
            var sr=story.LanguageVariant?story.GetRelativeGARC(language):story;var nr=names.LanguageVariant?names.GetRelativeGARC(language):names;
            string sp=Path.Combine(Config.RomFS,sr.Reference),np=Path.Combine(Config.RomFS,nr.Reference);if(!File.Exists(sp))continue;
            if(!File.Exists(np))throw new InvalidDataException("Falta el catálogo localizado para el texto de iniciales: "+np);
            _starterTextBanks.Add(new(sp,sr,File.ReadAllBytes(sp),np,File.ReadAllBytes(np)));
            if(!story.LanguageVariant)break;
        }
    }
    protected override void AfterCommit()=>CaptureStarterText();
    private protected override IReadOnlyList<FileChange> Build(Dictionary<string,byte[][]> records)
    {
        var files=base.Build(records).ToList();if(_starterBaseline.Length!=3)return files;
        var gifts=Enumerable.Range(0,3).Select(i=>new EncounterGift7(records["encounterstatic"][0].AsSpan(i*EncounterGift7.SIZE,EncounterGift7.SIZE).ToArray())).ToArray();
        if(gifts.Select((gift,i)=>gift.Species==_starterBaseline[i].Species&&gift.Form==_starterBaseline[i].Form).All(same=>same))return files;
        foreach(var bank in _starterTextBanks)
        {
            byte[] story=GarcTransaction.ReadUnchanged(bank.StoryPath,SHA256.HashData(bank.Story));byte[] names=GarcTransaction.ReadUnchanged(bank.NamesPath,SHA256.HashData(bank.Names));
            byte[][] data=new GARC.MemGARC(story).Files,nameData=new GARC.MemGARC(names).Files;int index=Config.USUM?39:41;
            int speciesBank=Config.GameText.First(row=>row.Name==TextName.SpeciesNames).Index,typeBank=Config.GameText.First(row=>row.Name==TextName.Types).Index;
            if(index>=data.Length||speciesBank>=nameData.Length||typeBank>=nameData.Length)throw new InvalidDataException("Los bancos de selección de iniciales están incompletos.");
            var lines=new TextFile(Config,data[index],Config.RemapCharacters).Lines;var species=new TextFile(Config,nameData[speciesBank],Config.RemapCharacters).Lines;var types=new TextFile(Config,nameData[typeBank],Config.RemapCharacters).Lines;
            if(lines.Length<(Config.USUM?17:4))throw new InvalidDataException("Faltan líneas de selección de iniciales.");
            int[] vanilla=[722,725,728];
            for(int i=0;i<3;i++)
            {
                if(gifts[i].Species==_starterBaseline[i].Species&&gifts[i].Form==_starterBaseline[i].Form)continue;
                if(gifts[i].Species<=0||gifts[i].Species>=species.Length)throw new InvalidDataException("Falta el nombre localizado del inicial.");
                string next=species[gifts[i].Species];lines[i+1]=ReplaceStarterName(lines[i+1],species,_starterBaseline[i].Species,vanilla[i],next);
                if(Config.USUM)lines[i+14]=next;
                else
                {
                    int oldIndex=Config.Personal.GetFormIndex(_starterBaseline[i].Species,_starterBaseline[i].Form),newIndex=Config.Personal.GetFormIndex(gifts[i].Species,gifts[i].Form);
                    int oldType=Config.Personal[oldIndex].Types[0],newType=Config.Personal[newIndex].Types[0];
                    if(oldType>=types.Length||newType>=types.Length)throw new InvalidDataException("Falta el nombre localizado del tipo.");
                    if(!string.IsNullOrWhiteSpace(types[oldType]))lines[i+1]=lines[i+1].Replace(types[oldType],types[newType],StringComparison.Ordinal);
                }
            }
            byte[] encoded=TextFile.GetBytes(Config,lines,Config.RemapCharacters);if(!new TextFile(Config,encoded,Config.RemapCharacters).Lines.SequenceEqual(lines))throw new InvalidDataException("La selección de iniciales perdió caracteres al codificar.");
            data[index]=encoded;files.Add(new(bank.StoryPath,story,GarcTransaction.Build(story,bank.StoryReference,data,preserveRecordSize:false)));
            // Include the read-only naming bank in the proposal fingerprint checks.
            files.Add(new(bank.NamesPath,names,names));
        }
        return files;
    }
    internal static string ReplaceStarterName(string line,string[] names,int old,int vanilla,string next)
    {
        bool FullName(string name)=>line.Contains(name,StringComparison.Ordinal)&&!names.Any(longer=>longer.Length>name.Length&&longer.Contains(name,StringComparison.Ordinal)&&line.Contains(longer,StringComparison.Ordinal));
        if(string.IsNullOrEmpty(line)||string.IsNullOrEmpty(next)||FullName(next))return line;
        foreach(int id in new[]{old,vanilla})if(id>0&&id<names.Length&&!string.IsNullOrWhiteSpace(names[id])&&FullName(names[id]))return line.Replace(names[id],next,StringComparison.Ordinal);
        string? found=names.Where(name=>!string.IsNullOrWhiteSpace(name)&&line.Contains(name,StringComparison.Ordinal)).OrderByDescending(name=>name.Length).FirstOrDefault();return found is null?line:line.Replace(found,next,StringComparison.Ordinal);
    }
}
