namespace ThreeDS.Progressive.Models;

/// <summary>Important/boss trainer data from the original pk3DS engine, not an inferred roster.</summary>
public sealed class TrainerRuleTableRow
{
    public int Id {get;set;}
    public string Group {get;set;}="";
    public string Trainer {get;set;}="";
    public int Ace {get;set;}
    public bool CapUse {get;set;}=true;
    public int Cap {get;set;}
    public bool Max3 {get;set;}
    public bool Mega {get;set;}
    public bool SupportsZMoves {get;set;}=true;
    public bool ZMove {get;set;}
    public bool MoveUse {get;set;}=true;
    public int MinPower {get;set;}
    public bool StrongStat {get;set;}
    public int Tolerance {get;set;}=15;
    public bool AllowStatus {get;set;}=true;
    public bool Better {get;set;}=true;
    public bool SmartItems {get;set;}=true;
    public int EVs {get;set;}=-1;
}
