using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using pk3DS.Core;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services.ProgressiveLegacy;

/// <summary>Uses the complete original trainer flow on cloned arrays. Original windows never open.</summary>
internal sealed class TrainerLogicHost
{
    private const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
    private static readonly object Gate=new();
    private readonly GameConfig _config;
    private readonly byte[][] _headers,_teams;
    private readonly string _work=Path.Combine(Path.GetTempPath(),"progressive-trainer-host-"+Guid.NewGuid().ToString("N"));
    private TrainerRandomizerTemplate? _template;
    internal int SelectedTrainer {get;set;}=1;
    internal TrainerLogicHost(GameConfig config,byte[][] headers,byte[][] teams)
    { _config=config;_headers=headers;_teams=teams; }
    private IDisposable Context()
    {
        Directory.CreateDirectory(_work);
        string templates=Path.Combine(_work,"Templates");Directory.CreateDirectory(templates);
        foreach(string name in new[]{"trainer_held_items.txt","trainer_better_movesets.txt"})
        {
            string source=Path.Combine(AppContext.BaseDirectory,"Templates",name),target=Path.Combine(templates,name);
            if(File.Exists(source)&&!File.Exists(target))File.Copy(source,target);
        }
        return Main.Enter(_work,_config);
    }
    private Form CreateOptions()
    {
        TrainerRandomizerTemplateFile.ClearCurrent();
        if(_config.Learnsets is null)_config.InitializeLearnset();
        if(_config.Evolutions is null)_config.InitializeEvos();
        Form form;
        if(_config.Generation==7)form=new SMTE(Clone(_headers),Clone(_teams));
        else
        {
            using var data=new RSTE(Clone(_headers),Clone(_teams));
            var caps=(List<TrainerLevelCapRule>)Call(data,"GetLevelCapCandidates")!;
            form=new TrainerRand(caps);
        }
        if(_template is not null)Call(form,"ApplyTrainerTemplate",_template,false);
        return form;
    }
    internal IReadOnlyList<EditorGroup> Options()=>OnSta(OptionsCore);
    private IReadOnlyList<EditorGroup> OptionsCore()
    {
        lock(Gate)
        {
            using var context=Context();using var form=CreateOptions();
            var root=_config.Generation==7?(Control)Field(form,"Tab_Rand")!:form;
            var controls=Walk(root).ToArray();
            var groups=new Dictionary<string,List<EditorField>>();
            foreach(var control in controls)
            {
                string group=Group(control);if(!groups.TryGetValue(group,out var fields))groups[group]=fields=[];
                if(control is CheckBox check&&!string.IsNullOrEmpty(check.Name))fields.Add(new(check.Name,Translate(check.Text),check.Checked?1:0,0,1));
                else if(control is NumericUpDown number&&!string.IsNullOrEmpty(number.Name))
                {
                    int scale=(int)Math.Pow(10,number.DecimalPlaces);
                    fields.Add(new(number.Name,NumberLabel(number.Name),checked((int)(number.Value*scale)),checked((int)(number.Minimum*scale)),checked((int)(number.Maximum*scale)),Scale:scale));
                }
                else if(control is ComboBox combo&&combo.Items.Count>0&&!string.IsNullOrEmpty(combo.Name))
                    fields.Add(new(combo.Name,ComboLabel(combo.Name),Math.Max(0,combo.SelectedIndex),0,combo.Items.Count-1,
                        combo.Items.Cast<object>().Select((item,i)=>new MoveChoice(i,Translate(item.ToString()??""))).ToArray()));
            }
            foreach(var radios in controls.OfType<RadioButton>().GroupBy(r=>r.Parent))
            {
                var list=radios.ToArray();if(list.Length==0)continue;string group=Group(list[0]);
                if(!groups.TryGetValue(group,out var fields))groups[group]=fields=[];
                fields.Add(new("Radio:"+radios.Key!.Name,string.IsNullOrWhiteSpace(radios.Key.Text)?"Origen de movimientos":Translate(radios.Key.Text),Math.Max(0,Array.FindIndex(list,r=>r.Checked)),0,list.Length-1,
                    list.Select((r,i)=>new MoveChoice(i,Translate(r.Text))).ToArray()));
            }
            var template=(TrainerRandomizerTemplate)Call(form,"CaptureTrainerTemplate")!;
            _template=template;
            var result=groups.Where(g=>g.Value.Count>0).Select(g=>new EditorGroup(g.Key,g.Value)).ToList();
            if(template.ProgressiveBST is {Ranges:var ranges})
                result.Add(new("Rangos Progressive BST",ranges.SelectMany((r,i)=>new[]{
                    F($"BST.{i}.MinLevel",$"Rango {i+1} · nivel mínimo",r.MinLevel,1,100),F($"BST.{i}.MaxLevel",$"Rango {i+1} · nivel máximo",r.MaxLevel,1,100),
                    F($"BST.{i}.MinBST",$"Rango {i+1} · BST mínimo",r.MinBST,0,1530),F($"BST.{i}.MaxBST",$"Rango {i+1} · BST máximo",r.MaxBST,0,1530),F($"BST.{i}.FullRandom",$"Rango {i+1} · libre",r.FullRandom?1:0,0,1)}).ToArray(),false));
            int id=SelectedTrainer;
            var existingCap=template.LevelCaps?.Trainers.FirstOrDefault(r=>r.TrainerID==id);
            var existingRule=template.TrainerMoveRules?.Trainers.FirstOrDefault(r=>r.TrainerID==id);
            if(existingCap is null&&existingRule is null)return result;
            var cap=existingCap??new TrainerLevelCapTemplateEntry {TrainerID=id,Use=false};
            var rule=template.TrainerMoveRules?.Trainers.FirstOrDefault(r=>r.TrainerID==id)??new TrainerMoveRuleTemplateEntry {TrainerID=id,Use=false};
            string k=$"Rule.{id}.";
            result.Add(new($"Reglas del entrenador #{id:000}",[
                F(k+"CapUse","Aplicar límite individual",cap.Use?1:0,0,1),F(k+"LevelCap","Nivel límite · 0 = Ace dinámico",cap.LevelCap,0,100),
                F(k+"Nerf","Máximo tres Pokémon",cap.NerfTeam?1:0,0,1),F(k+"Mega","Garantizar Mega",cap.Mega?1:0,0,1),F(k+"ZMove","Garantizar movimiento Z",cap.ZMove?1:0,0,1),
                F(k+"MoveUse","Aplicar regla de movimientos/objetos",rule.Use?1:0,0,1),F(k+"Power","Potencia mínima · 0 = desactivar",rule.MinMovePower,0,250),
                F(k+"Strong","Usar el ataque más fuerte",rule.StrongStat?1:0,0,1),F(k+"Tolerance","Tolerancia de atacante mixto",rule.MixedTolerance,0,100),
                F(k+"Status","Permitir movimientos de estado",rule.AllowStatusMoves?1:0,0,1),F(k+"Better","Better Movesets individual",rule.BetterMovesets?1:0,0,1),
                F(k+"Smart","Smart Items individual",rule.SmartItems?1:0,0,1),F(k+"EV","EV en las dos stats más fuertes · -1 = conservar",rule.EVs,-1,252)],false));
            return result;
        }
    }
    internal string Capture(IReadOnlyDictionary<string,int> options)=>OnSta(()=>CaptureCore(options));
    private string CaptureCore(IReadOnlyDictionary<string,int> options)
    {
        lock(Gate){using var context=Context();using var form=CreateOptions();Configure(form,options);_template=(TrainerRandomizerTemplate)Call(form,"CaptureTrainerTemplate")!;return JsonSerializer.Serialize(_template);}
    }
    internal void Restore(string json)
    {
        var template=JsonSerializer.Deserialize<TrainerRandomizerTemplate>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Template de entrenadores vacío.");
        TrainerRandomizerTemplateFile.Validate(template,_config.Version.ToString());_template=template;
    }
    internal (byte[][] Headers,byte[][] Teams) Run(IReadOnlyDictionary<string,int> options,int seed)=>OnSta(()=>RunCore(options,seed));
    private (byte[][] Headers,byte[][] Teams) RunCore(IReadOnlyDictionary<string,int> options,int seed)
    {
        lock(Gate)
        {
            using var context=Context();
            _=Capture(options);
            byte[][] headers=Clone(_headers),teams=Clone(_teams);
            WinFormsUtil.Messages.Clear();
            TrainerRandomizerTemplateFile.ClearCurrent();
            Util.ReseedRand(seed);
            ClearMoveCaches();
            if(_config.Generation==7)
            {
                using var engine=new SMTE(headers,teams);Configure(engine,options);
                TrainerEngineHooks.Invoke(engine,"B_Randomize_Click");
            }
            else
            {
                TrainerEngineHooks.Configure=f=>Configure(f,options);
                try {using var engine=new RSTE(headers,teams);TrainerEngineHooks.Invoke(engine,"B_Randomize_Click");}
                finally {TrainerEngineHooks.Configure=_=>{};}
            }
            return(headers,teams);
        }
    }
    private void Configure(Form form,IReadOnlyDictionary<string,int> values)
    {
        if(_template is not null)Call(form,"ApplyTrainerTemplate",_template,false);
        foreach(var pair in values)
        {
            var control=Walk(form).FirstOrDefault(c=>c.Name==pair.Key);
            if(control is CheckBox check)check.Checked=pair.Value==1;
            else if(control is NumericUpDown number)
            {decimal value=pair.Value/(decimal)Math.Pow(10,number.DecimalPlaces);if(value<number.Minimum||value>number.Maximum)throw new ArgumentException("Fuera de rango: "+NumberLabel(pair.Key));number.Value=value;}
            else if(control is ComboBox combo)combo.SelectedIndex=pair.Value;
            else if(pair.Key.StartsWith("Radio:"))
            {
                var parent=Walk(form).FirstOrDefault(c=>c.Name==pair.Key[6..]);var radios=parent?.Controls.OfType<RadioButton>().ToArray();
                if(radios is not null&&pair.Value>=0&&pair.Value<radios.Length)radios[pair.Value].Checked=true;
            }
        }
        var template=(TrainerRandomizerTemplate)Call(form,"CaptureTrainerTemplate")!;
        foreach(var pair in values.Where(p=>p.Key.StartsWith("BST.")))
        {
            var key=pair.Key.Split('.');var range=template.ProgressiveBST!.Ranges[int.Parse(key[1])];
            switch(key[2]){case "MinLevel":range.MinLevel=pair.Value;break;case "MaxLevel":range.MaxLevel=pair.Value;break;case "MinBST":range.MinBST=pair.Value;break;case "MaxBST":range.MaxBST=pair.Value;break;case "FullRandom":range.FullRandom=pair.Value==1;break;}
        }
        foreach(int id in values.Keys.Where(k=>k.StartsWith("Rule.")).Select(k=>int.Parse(k.Split('.')[1])).Distinct())
        {
            string k=$"Rule.{id}.";int V(string name)=>values[k+name];
            template.LevelCaps??=new();template.TrainerMoveRules??=new();
            template.LevelCaps.Trainers.RemoveAll(r=>r.TrainerID==id);
            template.LevelCaps.Trainers.Add(new(){TrainerID=id,Use=V("CapUse")==1,LevelCap=V("LevelCap"),NerfTeam=V("Nerf")==1,Mega=V("Mega")==1,ZMove=V("ZMove")==1});
            template.TrainerMoveRules.Trainers.RemoveAll(r=>r.TrainerID==id);
            template.TrainerMoveRules.Trainers.Add(new(){TrainerID=id,Use=V("MoveUse")==1,MinMovePower=V("Power"),StrongStat=V("Strong")==1,MixedTolerance=V("Tolerance"),AllowStatusMoves=V("Status")==1,BetterMovesets=V("Better")==1,SmartItems=V("Smart")==1,EVs=V("EV")});
        }
        TrainerRandomizerTemplateFile.Validate(template,_config.Version.ToString());
        _template=template;Call(form,"ApplyTrainerTemplate",template,false);
    }
    private static EditorField F(string key,string label,int value,int min,int max)=>new(key,label,value,min,max);
    private static byte[][] Clone(byte[][] source)=>source.Select(r=>(byte[])r.Clone()).ToArray();
    private static object? Field(object obj,string name)=>obj.GetType().GetField(name,Private)?.GetValue(obj);
    private static object? Call(object obj,string name,params object[] args)
    {
        try{return obj.GetType().GetMethod(name,Private)!.Invoke(obj,args);}
        catch(TargetInvocationException ex) when(ex.InnerException is not null)
        {System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw;}
    }
    private static IEnumerable<Control> Walk(Control root)
    {yield return root;foreach(Control child in root.Controls)foreach(var item in Walk(child))yield return item;}
    private static string Group(Control control)
    {for(var p=control.Parent;p is not null;p=p.Parent)if(p is GroupBox or TabPage&&!string.IsNullOrWhiteSpace(p.Text))return Translate(p.Text);return "Opciones generales";}
    private static string ComboLabel(string name)=>name switch
    {
        "CB_SmartHeldItemMode"=>"Calidad Smart Items · normales",
        "CB_SmartHeldItemModeImportant"=>"Calidad Smart Items · importantes",
        "CB_SmartHeldItemModeBoss"=>"Calidad Smart Items · jefes",
        _=>System.Text.RegularExpressions.Regex.Replace(name.Replace("CB_", ""), "([a-z])([A-Z])", "$1 $2").Replace('_',' ')
    };
    private static string NumberLabel(string name)=>name switch
    {
        "NUD_LevelBoost"=>"Multiplicador de niveles","NUD_RMin"=>"Pokémon mínimos por equipo","NUD_RMax"=>"Pokémon máximos por equipo",
        "NUD_Shiny"=>"Probabilidad shiny (%)","NUD_Damage"=>"Mínimo de movimientos ofensivos","NUD_STAB"=>"Mínimo de movimientos STAB",
        "NUD_StrongStatTolerance"=>"Tolerancia de atacante mixto","NUD_DoubleBattleChance"=>"Probabilidad de combate doble (%)",
        "NUD_ForceFullyEvolvedLevel"=>"Evolucionar totalmente desde nivel","NUD_LevelPower"=>"Potencia de la curva de niveles",
        _=>Translate(name.Replace("NUD_","").Replace('_',' '))
    };
    private static string Translate(string text)
    {
        var words=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){["Pokemon"]="Pokémon",["Trainer"]="Entrenadores",["Moves"]="Movimientos",["Rules"]="Reglas",["Level Caps"]="Límites de nivel",["Randomize Pokemon"]="Randomizar especies",["Randomize Class"]="Randomizar clase",["Random Held Items"]="Objetos equipados aleatorios",["Don't Modify"]="Conservar",["Randomize All"]="Randomizar todos",["Level Up Only"]="Solo por nivel",["Metronome"]="Metrónomo",["Normal"]="Normal",["Strong"]="Fuerte",["Competitive"]="Competitivo",["Include TMs"]="Incluir MT",["Max AI"]="IA máxima",["Ban Bad Items"]="Excluir objetos poco útiles",["Item Clause"]="No repetir objetos en el equipo"};
        return words.TryGetValue(text.Trim(),out string? value)?value:text.Replace("Normal trainers","Entrenadores normales").Replace("Important trainers","Entrenadores importantes").Replace("Bosses","Jefes");
    }
    private static T OnSta<T>(Func<T> action)
    {
        if(Thread.CurrentThread.GetApartmentState()==ApartmentState.STA)return action();
        T result=default!;Exception? failure=null;
        var thread=new Thread(()=>{try{result=action();}catch(Exception ex){failure=ex;}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }
    private static void ClearMoveCaches()
    {
        foreach(var field in typeof(SmartTrainerMovePicker).GetFields(Private))
            if(field.Name.Contains("Cache"))
            {if(field.GetValue(null) is System.Collections.IDictionary cache)cache.Clear();else if(!field.IsInitOnly)field.SetValue(null,null);}
    }
}
