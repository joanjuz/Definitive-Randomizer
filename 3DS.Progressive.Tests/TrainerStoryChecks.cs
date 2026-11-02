using System.IO;
using pk3DS.Core;
using pk3DS.Core.NX;
using pk3DS.Core.Randomizers;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;
using ThreeDS.Progressive.Services.Switch;

/// <summary>Story chronology (TrainerStoryProfile): the shipped WikiDex profiles and the USUM-style scaling rules.</summary>
internal static class TrainerStoryChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(string key in new[]{"DP","Pt","HGSS","BW","B2W2","XY","ORAS","SM","SWSH"})
        {
            var profile=TrainerStoryProfile.Load(key);
            check(profile is not null&&profile.Stages.Count>=5&&profile.Stages.Zip(profile.Stages.Skip(1)).All(p=>p.First.BaseCap<=p.Second.BaseCap)
                &&profile.Stages.SelectMany(s=>s.Milestones).All(profile.Points.ContainsKey)&&profile.Points.Values.Any(p=>p.Stage==profile.Stages.Count)
                &&profile.Points.Values.Count(p=>p.FromWalkthrough)>profile.Points.Count/4,$"Story_ProfileLoadsWithOrderedBossesAndPostgame ({key})");
        }
        check(TrainerStoryProfile.Load("USUM") is null,"Story_UsumKeepsItsAuditedTable");

        // Synthetic game: windows end at 20, 30 and 50 (Champion); trainer 9 is postgame.
        var stages=new[]{new TrainerStoryStage("Líder · A",[100],20),new TrainerStoryStage("Líder · B",[200],30),new TrainerStoryStage("Final · C",[300],50)};
        var points=new Dictionary<int,TrainerStoryPoint>{[1]=new(0,1,true),[2]=new(0,2,true),[3]=new(0,3,true),[100]=new(0,4,true),
            [4]=new(1,10,true),[5]=new(1,11,false),[6]=new(1,12,true),[200]=new(1,13,true),[300]=new(2,20,true),[9]=new(3,30,true)};
        var profile2=new TrainerStoryProfile("Test",stages,points);
        var aces=new Dictionary<int,int>{[1]=5,[2]=8,[3]=12,[100]=20,[4]=18,[5]=22,[6]=25,[200]=30,[300]=50,[9]=60};
        int? Ace(int id)=>aces.TryGetValue(id,out int a)?a:null;
        int? None(IReadOnlyList<int> _)=>null;
        var window0=new[]{1,2,3}.Select(id=>profile2.Target(id,None,Ace,2)!.Value).ToArray();
        var window1=new[]{4,5,6}.Select(id=>profile2.Target(id,None,Ace,2)!.Value).ToArray();
        check(window0[0]==5&&window0.SequenceEqual(window0.Order())&&window0.All(l=>l<=18),"Story_FirstWindowStartsAtItsLowestAceAndStaysBelowTheNextBoss");
        // The boss holds the last position of its window (as in USUM), so the last regular trainer stays below cap − gap.
        check(window1[0]==20&&window1.All(l=>l is >=20 and <=28)&&window1.SequenceEqual(window1.Order())&&window1[^1]>window1[0],"Story_WindowGoesFromThePreviousCapToJustBelowTheNext");
        check(profile2.Target(100,None,Ace,2) is null&&profile2.Target(300,None,Ace,2) is null&&profile2.Target(9,None,Ace,2) is null&&profile2.Target(77,None,Ace,2) is null,
            "Story_BossesWithoutRulePostgameAndUnknownKeepTheirLevel");
        // A Level Cap rule on the boss moves its window: the cap and the scaled trainers follow it.
        int? Rule(IReadOnlyList<int> m)=>m.Contains(200)?40:null;
        check(profile2.Target(200,Rule,Ace,2)==40&&profile2.Target(6,Rule,Ace,2)>window1[^1]&&profile2.Target(6,Rule,Ace,2)<=38&&profile2.Target(4,Rule,Ace,2)==20,"Story_LevelCapRuleOfTheBossDrivesItsWindow");
        // A lower later cap never pushes a trainer over its next boss.
        int? Lower(IReadOnlyList<int> m)=>m.Contains(200)?15:null;
        check(new[]{4,5,6}.All(id=>profile2.Target(id,Lower,Ace,2)<15),"Story_LowerLaterCapKeepsTrainersBelowIt");
        check(profile2.Matches(Ace)&&!profile2.Matches(id=>id==200?31:Ace(id)),"Story_RomhackWithOtherBossLevelsFallsBackToCapsByLevel");
    }

    /// <summary>
    /// --story-probe: the scaling on real games, in memory (3DS extractions through GameConfig, a Sword extraction through its
    /// project; nothing is written). Every placed trainer ends below the final level of the boss that closes its window, in
    /// walkthrough order, and postgame trainers keep their level.
    /// </summary>
    internal static void Probe(IReadOnlyList<string> sources,Action<bool,string> check)
    {
        foreach(string source in sources)
        {
            var (key,original,scaled)=Directory.Exists(Path.Combine(source,"romfs","bin","trainer"))?Sword(source):ThreeDs(source);
            var story=TrainerStoryProfile.Load(key)!;
            int? Ace(int id)=>original.TryGetValue(id,out int a)?a:null;
            var regular=story.Points.Where(p=>p.Value.Stage<story.Stages.Count&&!story.IsMilestone(p.Key)&&scaled.ContainsKey(p.Key)).ToArray();
            int BossLevel(int stage)=>story.Stages[stage].Milestones.Where(scaled.ContainsKey).Select(id=>scaled[id]).Min();
            bool below=regular.All(p=>scaled[p.Key]<BossLevel(p.Value.Stage));
            bool ordered=regular.GroupBy(p=>p.Value.Stage).All(g=>{var o=g.OrderBy(p=>p.Value.Order).Select(p=>scaled[p.Key]).ToArray();return o.Zip(o.Skip(1)).All(z=>z.First<=z.Second);});
            bool postgame=story.Points.Where(p=>p.Value.Stage==story.Stages.Count&&original.ContainsKey(p.Key)).All(p=>scaled[p.Key]==original[p.Key]);
            int changed=regular.Count(p=>scaled[p.Key]!=original[p.Key]);
            if(!(story.Matches(Ace)&&below&&ordered&&postgame))
            {
                Console.WriteLine($"  matches={story.Matches(Ace)} below={below} ordered={ordered} postgame={postgame}");
                foreach(var p in regular.Where(p=>scaled[p.Key]>=BossLevel(p.Value.Stage)).Take(6))Console.WriteLine($"  above #{p.Key} ventana {p.Value.Stage} {original[p.Key]}->{scaled[p.Key]} jefe {BossLevel(p.Value.Stage)}");
                foreach(var p in story.Points.Where(p=>p.Value.Stage==story.Stages.Count&&original.ContainsKey(p.Key)&&scaled[p.Key]!=original[p.Key]).Take(6))Console.WriteLine($"  postgame #{p.Key} {original[p.Key]}->{scaled[p.Key]}");
            }
            check(story.Matches(Ace)&&below&&ordered&&postgame&&changed>regular.Length/3,$"Story_Real_ScalingFollowsTheWalkthrough ({key}: {regular.Length} entrenadores, {changed} cambian)");
        }
    }

    private static (string Key,Dictionary<int,int> Original,Dictionary<int,int> Scaled) ThreeDs(string root)
    {
        string romfs=Directory.GetDirectories(root).First(d=>Path.GetFileName(d).Contains("rom",StringComparison.OrdinalIgnoreCase)&&Directory.Exists(Path.Combine(d,"a")));
        string exefs=Directory.GetDirectories(root).First(d=>Path.GetFileName(d).Contains("exe",StringComparison.OrdinalIgnoreCase));
        var config=new GameConfig(Directory.EnumerateFiles(Path.Combine(romfs,"a"),"*",SearchOption.AllDirectories).Count(f=>Path.GetFileName(f).Length==1));
        config.Initialize(romfs,exefs,2);
        string key=config.Version switch{GameVersion.XY=>"XY",GameVersion.ORAS=>"ORAS",GameVersion.SM=>"SM",_=>throw new NotSupportedException("Sin cronología generada para "+config.Version+" (USUM usa su tabla auditada).")};
        byte[][] headers=config.GetGARCData("trdata").Files,teams=config.GetGARCData("trpoke").Files;
        Dictionary<int,int> Aces(byte[][] h,byte[][] t)=>Enumerable.Range(1,h.Length-1).Where(i=>t[i].Length>0).ToDictionary(i=>i,i=>config.Generation==7
            ?new TrainerData7(h[i],t[i]).Pokemon.Select(p=>p.Level).DefaultIfEmpty(0).Max():new TrainerData6(h[i],t[i],config.ORAS).Team.Select(p=>(int)p.Level).DefaultIfEmpty(0).Max());
        var original=Aces(headers,teams);
        var host=new ThreeDS.Progressive.Services.ProgressiveLegacy.TrainerLogicHost(config,headers,teams);
        var values=EditorValues.Flatten(host.Options()).ToDictionary(p=>p.Key,p=>p.Value);
        // Only the levels move: no species, no level boost; Level Caps on with the scaling and the chronology.
        foreach(var (k,v) in new[]{("CHK_RandomPKM",0),("CHK_Level",0),("CHK_LevelCaps",1),("GlobalCaps.ApplyPrevious",1),("GlobalCaps.Story",1),("GlobalCaps.PreviousGap",2)})
            if(values.ContainsKey(k))values[k]=v;
        var (h2,t2)=host.Run(values,12345);
        return (key,original,Aces(h2,t2));
    }

    private static (string Key,Dictionary<int,int> Original,Dictionary<int,int> Scaled) Sword(string root)
    {
        var project=SwordShieldProject.Open(root);
        if(project.Identity.TitleId!="0100ABF008968000")throw new NotSupportedException("La cronología de Galar solo se aplica a Espada.");
        Dictionary<int,int> Aces()=>project.ReadData(SwordShieldDataSection.TrainerTeams).Where(c=>c.Field=="Nivel").GroupBy(c=>c.Entry).ToDictionary(g=>g.Key,g=>g.Max(c=>c.Value));
        var original=Aces();
        var editor=new SwordShieldRecordEditor(project,SwordShieldDataSection.TrainerTeams);
        var values=EditorValues.Flatten(editor.RandomOptions).ToDictionary(p=>p.Key,p=>p.Value);
        values["Species"]=0;values["StoryScaling"]=1;values["StoryGap"]=2;
        editor.Apply(editor.Preview(values,editor.Entries.Select(e=>e.Id).ToArray()));
        return ("SWSH",original,Aces());
    }
}
