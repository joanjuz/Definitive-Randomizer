using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Randomizers;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); checks++;
}
void Reject(Action action, string name)
{
    try { action(); } catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or InvalidOperationException)
    { Check(true, name); return; }
    throw new Exception("FAIL: expected rejection: " + name);
}
bool Equal(byte[][] a, byte[][] b) => a.Length == b.Length && a.Select((r,i) => r.SequenceEqual(b[i])).All(x => x);

foreach (var version in new[] { GameVersion.XY, GameVersion.ORAS, GameVersion.SM, GameVersion.USUM })
{
    using var f = new Fixture(version);
    string tag = version.ToString();
    var baseline = f.PersonalRecords;
    var disk = File.ReadAllBytes(f.PersonalPath);
    var all = f.Session.Entries.Select(e => e.Index).ToArray();
    var options = new BatchSettings { Seed = 78123, Stats = true, Shuffle = true, Types = true, Abilities = true,
        Items = true, Eggs = true, Catch = true, Tms = true, Hms = f.Game.Generation == 6,
        TypeTutors = true, SpecialTutors = f.Session.SupportsSpecialTutors };
    var preview = f.Session.PreviewPersonal(BatchAction.PersonalRandomize, options, all);
    Check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(disk), tag + " preview writes nothing");
    Check(Equal(f.PersonalRecords, baseline), tag + " preview does not mutate live config");
    Check(Equal(preview.CopyRecords(), f.Session.PreviewPersonal(BatchAction.PersonalRandomize, options, all).CopyRecords()), tag + " same seed identical bytes");
    Check(!Equal(preview.CopyRecords(), f.Session.PreviewPersonal(BatchAction.PersonalRandomize, options with { Seed = 9821 }, all).CopyRecords()), tag + " different seeds change results");
    var expected = new PersonalTable((byte[])baseline[^1].Clone(), version).Table;
    Util.ReseedRand(options.Seed);
    var original = new PersonalRandomizer(expected, f.Game)
    {
        TypeCount = 18, ModifyStats = true, ShuffleStats = true, StatsToRandomize = [true,true,true,true,true,true],
        StatDeviation = 25, ModifyTypes = true, SameTypeChance = 50, ModifyAbilities = true, AllowWonderGuard = false,
        ModifyHeldItems = true, ModifyCatchRate = true, ModifyEggGroup = true, SameEggGroupChance = 50,
        ModifyLearnsetTM = true, ModifyLearnsetHM = f.Game.Generation == 6, ModifyLearnsetTypeTutors = true,
        ModifyLearnsetMoveTutors = f.Session.SupportsSpecialTutors,
    };
    original.Execute();
    Check(Equal(expected.Select(p => p.Write()).ToArray(), preview.CopyRecords()[..^1]), tag + " exact Core randomizer parity");
    Check(preview.CopyRecords()[292][0] == 1, tag + " Shedinja HP protected");
    Check(preview.CopyRecords()[..^1].SelectMany(r => r).SequenceEqual(preview.CopyRecords()[^1]), tag + " master table rebuilt");
    var subset = f.Session.PreviewPersonal(BatchAction.PersonalModify, new BatchSettings { SetCatch = true, CatchValue = 201 }, [1, 5]);
    Check(subset.ChangedCount == 2 && subset.ScopeCount == 2, tag + " scope and diff count");
    Check(baseline.Where((r,i) => i is not (1 or 5) && i != baseline.Length-1).Select((r,i) => r)
        .SequenceEqual(subset.CopyRecords().Where((r,i) => i is not (1 or 5) && i != baseline.Length-1), ByteArrayComparer.Instance), tag + " excluded records preserved");
    Check(subset.CopyRecords()[1].Where((b,i) => i != 8).SequenceEqual(baseline[1].Where((b,i) => i != 8)), tag + " modifier changes only requested byte");
    var noOp = f.Session.PreviewPersonal(BatchAction.PersonalModify, new BatchSettings(), all);
    Check(Equal(noOp.CopyRecords(), baseline) && noOp.ChangedCount == 0, tag + " no-op preserves every byte");
    Reject(() => f.Session.PreviewPersonal(BatchAction.PersonalModify,
        new BatchSettings { ClearCompatibility = true, FullTms = true }, [1]), tag + " incompatible options rejected");
    Reject(() => f.Session.PreviewPersonal(BatchAction.PersonalModify,
        new BatchSettings { ScaleExperience = true, ExperiencePercent = 10000 }, [1]), tag + " EXP overflow rejected");
    var quick = f.Session.PreviewPersonal(BatchAction.PersonalModify,
        new BatchSettings { RemoveEvs = true, QuickHatch = true, SlowGrowth = true, SetCatch = true, CatchValue = 255,
            SetCall = f.Game.Generation == 7, CallValue = 29 }, [1]);
    var qp = new PersonalTable(quick.CopyRecords()[1], version).Table[0];
    Check(qp.HatchCycles == 1 && qp.EXPGrowth == 5 && qp.EV_HP == 0 && qp.CatchRate == 255 &&
        (f.Game.Generation != 7 || qp.EscapeRate == 29), tag + " coherent modifiers");
    var clear = f.Session.PreviewPersonal(BatchAction.PersonalModify, new BatchSettings { ClearCompatibility = true }, [1]);
    var cp = new PersonalTable(clear.CopyRecords()[1], version).Table[0];
    Check(!cp.TMHM.Take(100).Any(x => x), tag + " clears all 100 TMs");
    Check(cp.TMHM.Skip(100).SequenceEqual(f.Game.Personal[1].TMHM.Skip(100)), tag + " clear preserves HMs and reserved TM bits");
    Check(cp.TypeTutors.Skip(version == GameVersion.XY ? 7 : 8).SequenceEqual(f.Game.Personal[1].TypeTutors.Skip(version == GameVersion.XY ? 7 : 8)), tag + " clear preserves reserved tutor bits");
    var full = f.Session.PreviewPersonal(BatchAction.PersonalModify, new BatchSettings { FullTms = true,
        FullHms = f.Game.Generation == 6, FullTypeTutors = true, FullSpecialTutors = f.Session.SupportsSpecialTutors }, [1]);
    var fp = new PersonalTable(full.CopyRecords()[1], version).Table[0];
    int hm = version == GameVersion.XY ? 5 : f.Game.Generation == 6 ? 7 : 0;
    Check(fp.TMHM.Take(100 + hm).All(x => x) && fp.TMHM.Skip(100 + hm).SequenceEqual(f.Game.Personal[1].TMHM.Skip(100 + hm)), tag + " full compatibility preserves reserved bits");
    int[] count = version == GameVersion.ORAS ? [15,17,16,15] : version == GameVersion.USUM ? [67] : [];
    Check(count.Select((n,k) => fp.SpecialTutors[k].Take(n).All(x => x) && fp.SpecialTutors[k].Skip(n).SequenceEqual(f.Game.Personal[1].SpecialTutors[k].Skip(n))).All(x => x), tag + " special tutor boundaries");
    f.Session.ApplyPersonalPreview(subset);
    Check(Equal(f.PersonalRecords, subset.CopyRecords()), tag + " apply readback exact bytes");
    Reject(() => f.Session.ApplyPersonalPreview(preview), tag + " stale preview rejected after save");
    Reject(() => f.Session.ApplyPersonalPreview(subset), tag + " already applied preview rejected");
    f.Session.SavePersonal(1, f.Session.ReadPersonal(1) with { Attack = 123 });
    Check(f.Session.ReadPersonal(1).Attack == 123, tag + " individual save after bulk");
    Check(!Directory.EnumerateFiles(f.Root,"*.tmp",SearchOption.AllDirectories).Any(), tag + " no transaction debris");
    var ban = f.Session.PreviewPersonal(BatchAction.BanTrappingAbilities, new BatchSettings(), [2]);
    var bp = new PersonalTable(ban.CopyRecords()[2],version).Table[0];
    Check(!bp.Abilities.Any(PersonalRandomizer.IsTrappingAbility), tag + " deterministic trapping ability replacement");
    f.Session.ApplyPersonalPreview(ban);
    Check(f.Session.TrappingAbilitiesBanned, tag + " ban persists in session");
    var bannedRandom = f.Session.PreviewPersonal(BatchAction.PersonalRandomize, new BatchSettings { Abilities=true }, all);
    Check(!new PersonalTable(bannedRandom.CopyRecords()[^1],version).Table.Skip(1).SelectMany(p=>p.Abilities).Any(PersonalRandomizer.IsTrappingAbility),tag + " subsequent randomization honors ban");

    string table = f.Session.ExportPersonalTable([1,5]);
    Check(table.Split(Environment.NewLine).Length==3 && table.Contains("EXP base") && table.Contains("Habilidad oculta"),tag + " full saved personal table with Spanish headers");
    var moves = new GameMoveService(f.Session);
    string mtable = moves.ExportMoveTable([1,5]);
    Check(mtable.Split(Environment.NewLine).Length==3 && mtable.Contains("Efecto Z") && mtable.Contains("Flags (hex)"),tag + " full saved move table");
    byte[][] mbase = f.MoveRecords;
    int[] mids = moves.Entries.Select(m=>m.Id).ToArray();
    var mp = moves.PreviewMoves(BatchAction.MoveRandomize, new BatchSettings { Seed=97, MoveTypes=true, MoveCategories=true },mids);
    Check(Equal(f.MoveRecords,mbase),tag + " move preview is read-only");
    Check(Equal(mp.CopyRecords(),moves.PreviewMoves(BatchAction.MoveRandomize,new BatchSettings {Seed=97,MoveTypes=true,MoveCategories=true},mids).CopyRecords()),tag + " move seed reproducible");
    Check(mp.CopyRecords()[165].SequenceEqual(mbase[165]) && mp.CopyRecords()[174].SequenceEqual(mbase[174]), tag + " protected moves unchanged");
    Check(mp.CopyRecords().Select((r,i)=>r.Where((b,n)=>n is not (0 or 2)).SequenceEqual(mbase[i].Where((b,n)=>n is not (0 or 2)))).All(x=>x), tag + " move randomization preserves Z/effects/flags");
    Check(mp.CopyRecords().Select((r,i)=>mbase[i][2] != 0 || r[2]==0).All(x=>x), tag + " status moves stay status");
    var met = moves.PreviewMoves(BatchAction.Metronome,new BatchSettings(),mids);
    Check(met.CopyRecords()[118][5]==40 && met.CopyRecords()[33][5]==1 && met.CopyRecords()[1][5]==0,tag + " Metronome PP semantics");
    Check(met.CopyRecords().Select((r,i)=>r.Where((b,n)=>n!=5).SequenceEqual(mbase[i].Where((b,n)=>n!=5))).All(x=>x), tag + " Metronome only edits PP");
    Check(met.CopyRecords()[117][5]==0 && met.CopyRecords()[32][5]==0,tag + " Metronome uses real IDs rather than UI indices");
    var oneMove = moves.PreviewMoves(BatchAction.Metronome,new BatchSettings(),[118]);
    Check(oneMove.ChangedCount==1 && mbase.Where((r,i)=>i!=118).SequenceEqual(oneMove.CopyRecords().Where((r,i)=>i!=118),ByteArrayComparer.Instance),tag + " selected move scope");
    moves.ApplyMovePreview(mp);
    Check(Equal(f.MoveRecords,mp.CopyRecords()),tag + " XY/WD move commit readback");
    Reject(()=>moves.ApplyMovePreview(met),tag + " stale move preview rejected");
    var moveDraft=moves.Read(1) with { Power=84 };
    moves.Save(1,moveDraft);
    Check(moves.Read(1).Power==84,tag + " individual move save after bulk");
    var pending=moves.PreviewMoves(BatchAction.Metronome,new BatchSettings(),mids);
    byte[] edited=File.ReadAllBytes(f.MovePath); edited[^1]^=1; File.WriteAllBytes(f.MovePath,edited);
    Reject(()=>moves.ApplyMovePreview(pending),tag + " external move write rejected");
    Check(File.ReadAllBytes(f.MovePath).SequenceEqual(edited),tag + " external move data not overwritten");
    var personalPending=f.Session.PreviewPersonal(BatchAction.PersonalModify,new BatchSettings {SetCatch=true,CatchValue=47},[1]);
    byte[] other=File.ReadAllBytes(f.PersonalPath);other[^1]^=1;File.WriteAllBytes(f.PersonalPath,other);
    Reject(()=>f.Session.ApplyPersonalPreview(personalPending),tag + " external personal write rejected");
    Check(File.ReadAllBytes(f.PersonalPath).SequenceEqual(other),tag + " external personal data not overwritten");
}

using(var a=new Fixture(GameVersion.USUM))
using(var b=new Fixture(GameVersion.USUM))
{
    var p=a.Session.PreviewPersonal(BatchAction.PersonalModify,new BatchSettings {SetCatch=true,CatchValue=37},[1]);
    Reject(()=>b.Session.ApplyPersonalPreview(p),"cross-session personal proposal rejected");
    var ma=new GameMoveService(a.Session);var mb=new GameMoveService(b.Session);
    var mp=ma.PreviewMoves(BatchAction.Metronome,new BatchSettings(),[1]);
    Reject(()=>mb.ApplyMovePreview(mp),"cross-session move proposal rejected");
    var copy=p.CopyRecords();copy[1][0]=0;
    Check(p.CopyRecords()[1][0]!=0,"preview records cannot be mutated through returned copies");
    var malformed=a.PersonalRecords;malformed[^1][0]^=1;
    new GARCFile(new GARC.MemGARC(File.ReadAllBytes(a.PersonalPath)),a.Game.GetGARCReference("personal"),a.PersonalPath){Files=malformed}.Save();
    a.Game.InitializePersonal();
    var bad=Fixture.CreateSession(a.Game,a.Root,a.PersonalPath);
    Reject(()=>bad.PreviewPersonal(BatchAction.PersonalModify,new BatchSettings {SetCatch=true},[1]),"inconsistent master table rejected");
    Reject(()=>BatchAlgorithms.Validate(new BatchSettings {Deviation=101}),"invalid stat deviation rejected");
    Reject(()=>BatchAlgorithms.Validate(new BatchSettings {CatchValue=256}),"invalid capture rejected");
    Reject(()=>b.Session.PreviewPersonal(BatchAction.PersonalModify,new BatchSettings(),[0]),"entry zero protected");
    Reject(()=>b.Session.PreviewPersonal(BatchAction.PersonalModify,new BatchSettings(),[]),"empty scope rejected");
}
TemplateChecks.Run(Check, Reject);
if (OperatingSystem.IsWindows())
{
    Exception? failure = null;
    var ui = new Thread(() => { try { UiSmoke.Run(Check); } catch(Exception ex) { failure = ex; } });
    ui.SetApartmentState(ApartmentState.STA); ui.Start(); ui.Join();
    if (failure is not null) throw new Exception("WPF smoke checks failed", failure);
}
Console.WriteLine($"Completed {checks} regression checks with synthetic XY, ORAS, SM and USUM data.");

sealed class ByteArrayComparer : IEqualityComparer<byte[]>
{
    public static readonly ByteArrayComparer Instance=new();
    public bool Equals(byte[]? a,byte[]? b)=>a is not null && b is not null && a.SequenceEqual(b);
    public int GetHashCode(byte[] a)=>a.Length;
}
sealed class Fixture : IDisposable
{
    public string Root {get;}=Path.Combine(Path.GetTempPath(),"3ds-progressive-tests-"+Guid.NewGuid().ToString("N"));
    public GameConfig Game {get;}
    public GameProjectSession Session {get;}
    public string PersonalPath=>Path.Combine(Game.RomFS,Game.GetGARCFileName("personal"));
    public string MovePath=>Path.Combine(Game.RomFS,Game.GetGARCFileName("move"));
    public byte[][] PersonalRecords=>new GARC.MemGARC(File.ReadAllBytes(PersonalPath)).Files;
    public byte[][] MoveRecords {get {var outer=new GARC.MemGARC(File.ReadAllBytes(MovePath)).Files;return Game.XY?outer:Mini.UnpackMini(outer[0],"WD");}}
    public Fixture(GameVersion version)
    {
        Directory.CreateDirectory(Root);
        Game=new GameConfig(version){RomFS=Path.Combine(Root,"RomFS"),ExeFS=Path.Combine(Root,"ExeFS")};
        Directory.CreateDirectory(Game.RomFS);Directory.CreateDirectory(Game.ExeFS);
        var refs=version switch {GameVersion.XY=>GARCReference.GARCReference_XY,GameVersion.ORAS=>GARCReference.GARCReference_AO,
            GameVersion.SM=>GARCReference.GARCReference_SN,_=>GARCReference.GARCReference_US};
        var text=version switch {GameVersion.XY=>TextReference.GameText_XY,GameVersion.ORAS=>TextReference.GameText_AO,
            GameVersion.SM=>TextReference.GameText_SM,_=>TextReference.GameText_USUM};
        Set("Files",refs);Set("GameText",text);Set("Info",new GameInfo(Game));
        Set("Variables",Game.Generation==7?TextVariableCode.VariableCodes_SM:version==GameVersion.XY?TextVariableCode.VariableCodes_XY:TextVariableCode.VariableCodes_AO);
        string[][] names=Enumerable.Range(0,text.Max(t=>t.Index)+1).Select(_=>Enumerable.Range(0,1000).Select(i=>"Entry"+i).ToArray()).ToArray();
        names[text.First(t=>t.Name==TextName.Types).Index]=Enumerable.Range(0,18).Select(i=>"Type"+i).ToArray();
        names[text.First(t=>t.Name==TextName.SpeciesNames).Index][289]="Slaking";
        Set("GameTextStrings",names);
        int size=version==GameVersion.XY?0x40:version==GameVersion.ORAS?0x50:0x54;
        byte[][] personal=Enumerable.Range(0,840).Select(i=>
        {
            byte[] data=Enumerable.Range(0,size).Select(n=>(byte)((n*17+i*3)%256)).ToArray();
            data[0x1C]=data[0x1D]=0;
            var p=new PersonalTable(data,version).Table[0];
            p.HP=i==292?1:60;p.ATK=80;p.DEF=70;p.SPE=75;p.SPA=65;p.SPD=70;p.Types=[1,2];
            p.CatchRate=100;p.FormeCount=1;p.BaseEXP=1000;p.EXPGrowth=0;
            p.Abilities=i==2?[23,71,23]:[1,2,3];p.EggGroups=[1,2];p.Items=[1,2,3];
            return p.Write();
        }).ToArray();
        byte[][] full=personal.Append(personal.SelectMany(r=>r).ToArray()).ToArray();
        WriteGarc(PersonalPath,full,Game.GetGARCReference("personal"));Game.InitializePersonal();
        byte[][] moves=Enumerable.Range(0,800).Select(i=>
        {
            // Distinct opaque bytes around fields; custom flag/Z data must survive.
            byte[] data=new byte[Game.Generation==7?0x28:0x22];
            data[^1]=0xA3;data[0x1F]=0x53;data[0x1E]=0xD7;
            Move m=Game.Generation==7?new Move7(data):new Move6(data);m.Type=2;m.Category=i%3;m.Power=75;m.Accuracy=100;m.PP=15;
            return m.Write();
        }).ToArray();
        WriteGarc(MovePath,Game.XY?moves:[Mini.PackMini(moves,"WD")],Game.GetGARCReference("move"));Game.InitializeMoves();
        Session=CreateSession(Game,Root,PersonalPath);
    }
    internal void WriteGarc(string path,byte[][] files,GARCReference reference)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var garc=GARC.PackGARC(files,Game.GARCVersion,4);new GARCFile(garc,reference,path).Save();
    }
    void Set(string name,object value)=>typeof(GameConfig).GetProperty(name)!.SetValue(Game,value);
    public static GameProjectSession CreateSession(GameConfig game,string root,string path)=>(GameProjectSession)
        typeof(GameProjectSession).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single().Invoke([game,root,path]);
    public void Dispose()=>Directory.Delete(Root,true);
}
