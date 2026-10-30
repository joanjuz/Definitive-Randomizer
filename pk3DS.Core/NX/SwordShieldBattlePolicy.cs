#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Buffers.Binary;

namespace pk3DS.Core.NX;

public sealed record SwordShieldBstRange(int MinLevel,int MaxLevel,int MinBst,int MaxBst,bool FullRandom=false)
{
    public static IReadOnlyList<SwordShieldBstRange> Defaults { get; } = Array.AsReadOnly(new[]{new SwordShieldBstRange(1,10,180,320),new(11,20,220,380),new(21,30,280,450),new(31,40,340,520),new(41,50,400,580),new(51,100,480,680)});
}
public sealed record SwordShieldTrainerItemMode(int TrainerId,int Mode);
public sealed record SwordShieldTrainerLevel(int TrainerId,int Level);
public sealed record SwordShieldTrainerMoveRule(int TrainerId,bool Better,bool Strong,int Power,int Tolerance,bool Status);
public sealed record SwordShieldTrainerOptions
{
    public int DoubleBattleChance {get;init;}
    public bool IncludeMachines {get;init;}
    public IReadOnlyList<SwordShieldTrainerItemMode> ItemModes {get;init;}=Array.Empty<SwordShieldTrainerItemMode>();
    public bool BetterMovesets { get; init; }
    public bool StrongestAttackStat { get; init; }
    public int MixedTolerance { get; init; } = 15;
    public bool PreferStab { get; init; } = true;
    public bool AllowStatus { get; init; } = true;
    public int MinMovePower { get; init; }
    public bool SmartItems { get; init; }
    public int SmartItemMode { get; init; } = 2;
    public int LevelPercent { get; init; } = 100;
    public IReadOnlyList<int> BannedMoves { get; init; } = Array.Empty<int>();
    public IReadOnlyList<SwordShieldTrainerLevel> Levels { get; init; } = Array.Empty<SwordShieldTrainerLevel>();
    public IReadOnlyList<SwordShieldTrainerMoveRule> MoveRules {get;init;}=Array.Empty<SwordShieldTrainerMoveRule>();
    // Null retains legacy global behavior; an empty list explicitly disables the rule for every trainer.
    public IReadOnlyList<int>? BetterScope {get;init;}
    public IReadOnlyList<int>? StrongScope {get;init;}
    public IReadOnlyList<int>? PowerScope {get;init;}
    public IReadOnlyList<int>? SmartItemScope {get;init;}
    public bool NeedsMoves => BetterMovesets || StrongestAttackStat || MinMovePower>0 || !AllowStatus || MoveRules.Any(r=>r.Better||r.Strong||r.Power>0||!r.Status);
    internal SwordShieldTrainerOptions ForTrainer(int id) {var scoped=this with{SmartItemMode=ItemModes.FirstOrDefault(m=>m.TrainerId==id)?.Mode??SmartItemMode,BetterMovesets=BetterMovesets&&(BetterScope is null||BetterScope.Contains(id)),StrongestAttackStat=StrongestAttackStat&&(StrongScope is null||StrongScope.Contains(id)),MinMovePower=PowerScope is null||PowerScope.Contains(id)?MinMovePower:0,SmartItems=SmartItems&&(SmartItemScope is null||SmartItemScope.Contains(id))};var rule=MoveRules.FirstOrDefault(r=>r.TrainerId==id);return rule is null?scoped with{MoveRules=Array.Empty<SwordShieldTrainerMoveRule>()}:scoped with{BetterMovesets=rule.Better,StrongestAttackStat=rule.Strong,MinMovePower=rule.Power,MixedTolerance=rule.Tolerance,AllowStatus=rule.Status,MoveRules=Array.Empty<SwordShieldTrainerMoveRule>()};}
}

/// <summary>Gen8 adapter: move categories are 0 status, 1 physical, 2 special (same IDs as CTR).
/// Applies rules to final species/moves, never to stale pre-randomization teams.</summary>
public static class SwordShieldBattlePolicy
{
    public static void Validate(SwordShieldGameplayOptions options)
    {
        var r=options.TrainerRules??throw new InvalidDataException("Faltan reglas de entrenadores.");
        if(options.EmptyMinLevel is <1 or >100||options.EmptyMaxLevel<options.EmptyMinLevel||options.EmptyMaxLevel>100)throw new InvalidDataException("Niveles para encuentros vacíos inválidos.");
        if(r.DoubleBattleChance is <0 or >100 || r.MixedTolerance is <0 or >255 || r.MinMovePower is <0 or >255 || r.SmartItemMode is <0 or >2 || r.LevelPercent is <1 or >1000)throw new InvalidDataException("Reglas de entrenadores fuera de rango.");
        if(r.Levels is null||r.BannedMoves is null||r.Levels.Any(t=>t.TrainerId<0||t.Level is <1 or >100)||r.Levels.Select(t=>t.TrainerId).Distinct().Count()!=r.Levels.Count||r.BannedMoves.Any(m=>m is <1 or >826))throw new InvalidDataException("Lista de reglas de entrenador inválida.");
        if(r.ItemModes is null||r.ItemModes.Any(m=>m.TrainerId<0||m.Mode is <0 or >2)||r.ItemModes.Select(m=>m.TrainerId).Distinct().Count()!=r.ItemModes.Count)throw new InvalidDataException("Calidad por entrenador inválida.");
        if(r.MoveRules is null||r.MoveRules.Any(t=>t.TrainerId<0||t.Power is <0 or >255||t.Tolerance is <0 or >255)||r.MoveRules.Select(t=>t.TrainerId).Distinct().Count()!=r.MoveRules.Count)throw new InvalidDataException("Reglas individuales inválidas.");
        foreach(var scope in new[]{r.BetterScope,r.StrongScope,r.PowerScope,r.SmartItemScope})if(scope is not null&&(scope.Any(id=>id<0)||scope.Distinct().Count()!=scope.Count))throw new InvalidDataException("Alcance de regla inválido.");
        if(options.TrainerScope is not null&&(options.TrainerScope.Any(id=>id<0)||options.TrainerScope.Distinct().Count()!=options.TrainerScope.Count))throw new InvalidDataException("Alcance de entrenadores inválido.");
        if(!options.ProgressiveBst)return;
        if(options.BstRanges is null||options.BstRanges.Count==0)throw new InvalidDataException("Faltan rangos Progressive BST.");
        foreach(var range in options.BstRanges)if(range.MinLevel<1||range.MaxLevel>100||range.MinLevel>range.MaxLevel||range.MinBst<0||range.MaxBst>1530||range.MinBst>range.MaxBst)throw new InvalidDataException("Rango BST inválido.");
        for(int level=1;level<=100;level++)if(options.BstRanges.Count(r=>r.MinLevel<=level&&r.MaxLevel>=level)!=1)throw new InvalidDataException("Los rangos BST deben cubrir 1–100 sin huecos ni solapamientos.");
    }
    public static int TrainerLevel(int trainer,int original,SwordShieldTrainerOptions rules)
        => (rules.Levels.FirstOrDefault(r=>r.TrainerId==trainer)??(trainer is 189 or 190?rules.Levels.FirstOrDefault(r=>r.TrainerId==149):null))?.Level??Math.Clamp((int)Math.Round(original*rules.LevelPercent/100.0,MidpointRounding.AwayFromZero),1,100);
    internal sealed record Move(int Id,int Type,int Category,int Power,int Accuracy);
    public sealed class TrainerContext
    {
        internal SwordShieldPersonalTable Table=null!;
        internal Dictionary<int,Move> Moves=null!;
        internal HashSet<int> Usable=null!;
        internal IReadOnlyList<SwordShieldDataCell> Learn=null!;
        internal HashSet<int> Items=null!;
        internal byte[] Personal=null!;
        internal Dictionary<(int Species,int Form),SwordShieldPersonalEntry> Entries=null!;
        internal Dictionary<int,(int Move,int Level)[]> LearnByEntry=null!;
    }
    public static TrainerContext Prepare(byte[] personal,IReadOnlyDictionary<string,byte[]> source,SwordShieldTrainerOptions rules)
    {
        var table=new SwordShieldPersonalTable(personal);
        var rows=SwordShieldDataEditor.Read(SwordShieldDataSection.Moves,source,personal);
        var usable=SwordShieldDataEditor.UsableMoves(source).ToHashSet();
        var moves=rows.GroupBy(c=>c.Entry).ToDictionary(g=>g.Key,g=>new Move(g.Key,g.First(c=>c.Field=="Tipo").Value,g.First(c=>c.Field=="Categoría").Value,g.First(c=>c.Field=="Potencia").Value,g.First(c=>c.Field=="Precisión").Value));
        IReadOnlyList<SwordShieldDataCell> learn=rules.NeedsMoves?SwordShieldDataEditor.Read(SwordShieldDataSection.Learnsets,source,personal):Array.Empty<SwordShieldDataCell>();
        var items=rules.SmartItems?SwordShieldDataEditor.Read(SwordShieldDataSection.Items,source,personal).Select(c=>c.Entry).ToHashSet():new HashSet<int>();
        return new(){Table=table,Moves=moves,Usable=usable,Learn=learn,Items=items,Personal=personal,Entries=table.Entries().GroupBy(e=>(e.Species,e.Form)).ToDictionary(g=>g.Key,g=>g.First()),LearnByEntry=learn.GroupBy(c=>c.Entry).ToDictionary(g=>g.Key,g=>g.GroupBy(c=>c.Slot).Select(slot=>(Move:slot.First(c=>c.Field=="Movimiento").Value,Level:slot.First(c=>c.Field=="Nivel").Value)).ToArray())};
    }
    public static void ApplyTrainer(byte[] team,byte[] original,string path,TrainerContext? context,SwordShieldGameplayOptions options,Action<string,int,int,int> change,Action<string>? warn=null)
    {
        var rules=options.TrainerRules.ForTrainer(int.Parse(System.IO.Path.GetFileNameWithoutExtension(path).Replace("trainer_poke_",""),System.Globalization.CultureInfo.InvariantCulture));if(!rules.NeedsMoves&&!rules.SmartItems)return;
        if(context is null)throw new InvalidOperationException("Falta el contexto de entrenadores.");
        var table=context.Table;var moves=context.Moves;var usable=context.Usable;var learn=context.Learn;var items=context.Items;
        void Write(int at,int slot,string name,int value){int before=BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(at));BinaryPrimitives.WriteUInt16LittleEndian(team.AsSpan(at),(ushort)value);if(before!=value)change(name,slot,before,value);}
        for(int slot=0;slot<team.Length/32;slot++)
        {
            int at=slot*32,species=BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(at+12)),form=BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(at+14)),level=BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(at+10));
            if(!context.Entries.TryGetValue((species,form),out var entry))throw new InvalidDataException("No se pudo resolver la forma del equipo.");
            var finalMoves=Enumerable.Range(0,4).Select(m=>(int)BinaryPrimitives.ReadUInt16LittleEndian(team.AsSpan(at+18+m*2))).ToArray();
            if(rules.NeedsMoves)
            {
                var learnset=context.LearnByEntry.TryGetValue(entry.Index,out var list)?list:Array.Empty<(int Move,int Level)>();
                bool Valid(int id)=>usable.Contains(id)&&moves.ContainsKey(id)&&!rules.BannedMoves.Contains(id);
                var ids=learnset.Where(m=>m.Level<=level).Select(m=>m.Move);
                if(rules.BetterMovesets&&rules.IncludeMachines)ids=ids.Concat(SwordShieldMachines.Compatible(context.Personal,entry.Index));
                var learnt=ids.Distinct().Where(Valid).Select(id=>moves[id]).ToArray();
                int delta=entry.Values[1]-entry.Values[4];bool allowStatus=rules.AllowStatus;
                var candidates=learnt.Where(m=>m.Category==0?allowStatus:m.Power>=rules.MinMovePower&&(!rules.StrongestAttackStat||Math.Abs(delta)<=rules.MixedTolerance||m.Category==(delta>0?1:2))).ToArray();
                if(!candidates.Any(m=>m.Category!=0))
                {
                    string reason;
                    if(learnt.Any(m=>m.Category!=0)){candidates=learnt.Where(m=>m.Category!=0||allowStatus).ToArray();reason="se relajaron potencia/categoría para conservar un ataque legal";}
                    else
                    {
                        var future=learnset.Where(m=>Valid(m.Move)&&moves[m.Move].Category!=0).OrderBy(m=>m.Level).ToArray();
                        if(future.Length>0){int first=future[0].Level;candidates=learnt.Where(m=>allowStatus||m.Category!=0).Concat(future.Where(m=>m.Level==first).Select(m=>moves[m.Move])).DistinctBy(m=>m.Id).ToArray();reason=$"sin ataque ofensivo al nivel {level}; se usó el primer aprendizaje ofensivo (nivel {first})";}
                        else if(learnt.Length>0){candidates=learnt;allowStatus=true;reason="la especie solo dispone de movimientos de estado; se conservaron";}
                        else if(Valid(165)){candidates=new[]{moves[165]};reason="sin aprendizajes utilizables; se asignó el movimiento #165 como recurso de emergencia";}
                        else throw new InvalidDataException($"{path}, Pokémon {slot+1}: no hay aprendizajes ni movimiento de emergencia utilizables; revisa los datos y movimientos prohibidos.");
                    }
                    warn?.Invoke($"{path}, Pokémon {slot+1}: {reason}.");
                }
                // Stable tie breaking avoids turning all teams into the same four highest-power moves.
                var selected=new List<Move>();var remaining=candidates.ToList();
                for(int n=0;n<4&&remaining.Count>0;n++)
                {
                    int Score(Move m)=> (m.Category==0?10:m.Power)+(rules.PreferStab&&m.Category!=0&&(m.Type==entry.Values[6]||m.Type==entry.Values[7])?60:0)+(selected.All(s=>s.Type!=m.Type)?20:0);
                    int best=remaining.Max(Score);var top=remaining.Where(m=>Score(m)>=best-15&&(n>0||m.Category!=0)).ToArray();
                    if(top.Length==0)top=remaining.Where(m=>m.Category!=0).ToArray();
                    if(top.Length==0&&allowStatus)top=remaining.ToArray();if(top.Length==0)break;var chosen=top[Pick(options.Seed,path+"/"+slot+"/move/"+n,top.Length)];selected.Add(chosen);remaining.Remove(chosen);
                }
                finalMoves=Enumerable.Range(0,4).Select(i=>i<selected.Count?selected[i].Id:0).ToArray();
                for(int m=0;m<4;m++)Write(at+18+m*2,slot,"Movimiento "+(m+1),finalMoves[m]);
            }
            if(rules.SmartItems)
            {
                var selected=finalMoves.Where(m=>m!=0).Select(m=>moves.TryGetValue(m,out var info)?info:throw new InvalidDataException("Movimiento del equipo desconocido para Smart Items.")).ToArray();
                bool status=selected.Any(m=>m.Category==0),physical=selected.Any(m=>m.Category==1),special=selected.Any(m=>m.Category==2);
                // Stable item IDs shared with CTR. Conservative subset; no species/ability-specific items without their conditions.
                var scored=new List<(int Id,int Score)>{(234,60),(158,40)}; // Leftovers / Sitrus Berry
                if(physical)scored.Add((268,55));if(special)scored.Add((269,55)); // Muscle Band / Wise Glasses
                if(!status&&selected.Length>0){scored.Add((640,60));scored.Add((287,65));if(physical&&!special)scored.Add((220,75));if(special&&!physical)scored.Add((297,75));}
                if(physical||special){scored.Add((272,70));scored.Add((275,60));if(selected.Where(m=>m.Category!=0).Select(m=>m.Type).Distinct().Count()>=3)scored.Add((270,70));}
                if(entry.Values[6]==3||entry.Values[7]==3)scored.Add((281,65));
                var eligible=scored.Where(i=>items.Contains(i.Id)).Where(i=>rules.SmartItemMode!=0||i.Score<=60).ToArray();
                if(eligible.Length==0)throw new InvalidDataException("No hay objetos compatibles disponibles para Smart Items.");
                if(rules.SmartItemMode==2){int best=eligible.Max(i=>i.Score);eligible=eligible.Where(i=>i.Score>=best-10).ToArray();}
                Write(at+16,slot,"Objeto equipado",eligible[Pick(options.Seed,path+"/"+slot+"/item",eligible.Length)].Id);
            }
        }
    }
    private static int Pick(uint seed,string key,int count)=> (int)((SwitchGfPack.NameHash(key)^seed)%(uint)count);
}
