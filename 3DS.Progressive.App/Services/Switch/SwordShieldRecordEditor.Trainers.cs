using System.Globalization;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Models;
namespace ThreeDS.Progressive.Services.Switch;
internal sealed partial class SwordShieldRecordEditor
{
    public TrainerRuleIdentity TrainerIdentity(int id)=>new(id,_project.TrainerKind(id),_project.TrainerName(id),_cells.Where(c=>c.Entry==id&&c.Field=="Nivel").Max(c=>c.Value));
    internal IReadOnlyDictionary<int,int> CapPreset => DivineSwordCaps.ForTitle(_project.Identity.TitleId);
    private EditorField[] CategoryFields(string prefix)=>new[]{Flag(prefix+"Normal","Normales",true),Flag(prefix+"Important","Importantes",true),Flag(prefix+"Boss","Líderes de gimnasio",true)};
    private IReadOnlyList<EditorGroup> TrainerOptions()
    {
        var groups=new List<EditorGroup>
        {
            new("Pokémon y equipos",new[]{Flag("Species","Randomizar especies",true)}),
            new("Niveles de entrenadores",new[]{new EditorField("LevelPercent","Nivel de entrenadores (%)",100,1,1000)}),
            new("Combates dobles",new[]{new EditorField("DoubleBattleChance","Probabilidad de combate doble (%)",0,0,100)}),
            new("Better Movesets",new[]{Flag("GlobalMoves.Better","Better Movesets",false),Flag("GlobalMoves.IncludeTMs","Incluir MT y TR compatibles",false)}.Concat(CategoryFields("GlobalMoves.Better")).ToArray()),
            new("Ataque más fuerte",new[]{Flag("GlobalMoves.StrongStat","Usar el ataque más fuerte",false),new EditorField("GlobalMoves.Tolerance","Tolerancia mixto",15,0,255)}.Concat(CategoryFields("GlobalMoves.Strong")).ToArray()),
            new("Movimientos y reglas",new[]{Flag("GlobalMoves.Status","Permitir movimientos de estado",true),Flag("GlobalMoves.STAB","Priorizar STAB",true),new EditorField("GlobalMoves.Ban","Movimiento prohibido · 0 = ninguno",0,0,826)}),
            new("Potencia mínima",new[]{new EditorField("GlobalMoves.Power","Potencia mínima · 0 = sin mínimo",0,0,255)}.Concat(CategoryFields("GlobalMoves.Power")).ToArray()),
            new("Objetos equipados",new[]{Flag("GlobalItems.Smart","Smart Items",false)}.Concat(new[]{"Normal","Important","Boss"}.SelectMany((category,index)=>new[]{Flag("GlobalItems."+category,new[]{"Normales","Importantes","Líderes de gimnasio"}[index],true),new EditorField("GlobalItems.Quality"+category,"Calidad · "+new[]{"Normales","Importantes","Líderes de gimnasio"}[index],2,0,2,new[]{new MoveChoice(0,"Normal"),new(1,"Relevantes"),new(2,"Competitivos")})})).ToArray()),
            new("BST progresivo",new[]{Flag("CHK_BST","Especies con BST similar",true),Flag("CHK_ProgressiveBST","Usar rangos Progressive BST por nivel",false)}),
        };
        for(int i=0;i<SwordShieldBstRange.Defaults.Count;i++)
        {
            var range=SwordShieldBstRange.Defaults[i];string key="BST."+i+".";groups.Add(new("Rango "+(i+1),new[]{new EditorField(key+"MinLevel","Nivel mínimo",range.MinLevel,1,100),new EditorField(key+"MaxLevel","Nivel máximo",range.MaxLevel,1,100),new EditorField(key+"MinBST","BST mínimo",range.MinBst,0,1530),new EditorField(key+"MaxBST","BST máximo",range.MaxBst,0,1530),Flag(key+"FullRandom","Full Random",false)}));
        }
        // Explicit Sword story mapping already used by the level-cap runtime. Shield is deliberately not assigned these IDs.
        foreach(int id in Entries.Select(e=>e.Id).Where(id=>CapPreset.Count>0?CapPreset.ContainsKey(id):_project.TrainerKind(id)!="Normal"))
        {
            var levels=_cells.Where(c=>c.Entry==id&&c.Field=="Nivel").ToArray();if(levels.Length==0)continue;string key="Rule."+id+".";
            groups.Add(new(_project.TrainerKind(id)+" · "+_project.TrainerName(id)+" · #"+id,new[]{Flag(key+"CapUse","Aplicar nivel exacto",false),new EditorField(key+"LevelCap","Nivel del equipo",levels.Max(c=>c.Value),1,100)},false));
        }
        return groups;
    }
    private EditorProposal PreviewTrainers(IReadOnlyDictionary<string,int> values,IReadOnlyList<int> scope)
    {
        EditorValues.Validate(TrainerOptions().Select(g=>g with{Fields=g.Fields.Where(f=>!f.Key.StartsWith("BST.",StringComparison.Ordinal)).ToArray()}),values);
        if(scope.Count==0||scope.Any(id=>!Entries.Any(e=>e.Id==id)))throw new ArgumentException("Alcance de entrenadores inválido.");
        int V(string key,int fallback=0)=>values.GetValueOrDefault(key,fallback);
        var ranges=values.Where(p=>p.Key.StartsWith("BST.",StringComparison.Ordinal)).GroupBy(p=>p.Key.Split('.')[1]).Select(g=>{string key="BST."+g.Key+".";return new SwordShieldBstRange(V(key+"MinLevel"),V(key+"MaxLevel"),V(key+"MinBST"),V(key+"MaxBST"),V(key+"FullRandom")==1);}).ToArray();
        var levels=values.Where(p=>p.Key.StartsWith("Rule.",StringComparison.Ordinal)&&p.Key.EndsWith(".CapUse",StringComparison.Ordinal)&&p.Value==1).Select(p=>{int id=int.Parse(p.Key.Split('.')[1],CultureInfo.InvariantCulture);return new SwordShieldTrainerLevel(id,V("Rule."+id+".LevelCap"));}).ToArray();
        int[] CategoryScope(string prefix)=>Entries.Select(e=>e.Id).Where(id=>V(prefix+(_project.TrainerKind(id)=="Normal"?"Normal":_project.TrainerKind(id).StartsWith("Jefes",StringComparison.Ordinal)?"Boss":"Important"),1)==1).ToArray();
        uint seed=SwitchSeed.New32()&int.MaxValue;var rules=new SwordShieldTrainerOptions{DoubleBattleChance=V("DoubleBattleChance"),BetterMovesets=V("GlobalMoves.Better")==1,IncludeMachines=V("GlobalMoves.IncludeTMs")==1,StrongestAttackStat=V("GlobalMoves.StrongStat")==1,MixedTolerance=V("GlobalMoves.Tolerance"),AllowStatus=V("GlobalMoves.Status")==1,PreferStab=V("GlobalMoves.STAB")==1,MinMovePower=V("GlobalMoves.Power"),SmartItems=V("GlobalItems.Smart")==1,SmartItemMode=V("GlobalItems.Quality",2),ItemModes=Entries.Select(e=>new SwordShieldTrainerItemMode(e.Id,V("GlobalItems.Quality"+(_project.TrainerKind(e.Id)=="Normal"?"Normal":_project.TrainerKind(e.Id).StartsWith("Jefes",StringComparison.Ordinal)?"Boss":"Important"),V("GlobalItems.Quality",2)))).ToArray(),LevelPercent=V("LevelPercent",100),BannedMoves=V("GlobalMoves.Ban")>0?new[]{V("GlobalMoves.Ban")}:Array.Empty<int>(),Levels=levels,BetterScope=CategoryScope("GlobalMoves.Better"),StrongScope=CategoryScope("GlobalMoves.Strong"),PowerScope=CategoryScope("GlobalMoves.Power"),SmartItemScope=CategoryScope("GlobalItems.")};
        var config=new SwordShieldGameplayOptions(seed,Trainers:V("Species")==1||rules.NeedsMoves||rules.SmartItems||rules.LevelPercent!=100||rules.Levels.Count>0||rules.DoubleBattleChance>0,SimilarBst:V("CHK_BST")==1){ProgressiveBst=V("CHK_ProgressiveBST")==1,BstRanges=ranges,RandomizeTrainerSpecies=V("Species")==1,TrainerRules=rules,TrainerScope=scope.ToArray()};
        var data=_project.PreviewGameplay(config);var changes=data.Result.Changes.Select(c=>new BatchChange(0,c.Location,c.Field,c.Before.ToString(CultureInfo.InvariantCulture),c.After.ToString(CultureInfo.InvariantCulture))).ToArray();
        var proposal=new EditorProposal(_owner,"Randomizar entrenadores",(int)seed,Array.Empty<FileChange>(),changes,new Dictionary<string,string>{{"Platform","SWSH"},{"Seed",seed.ToString(CultureInfo.InvariantCulture)},{"Warnings",string.Join("\n",data.Result.Warnings)},{"WarningCount",data.Result.Warnings.Count.ToString(CultureInfo.InvariantCulture)}});_trainerProposals.Add(proposal,data);return proposal;
    }
}
