using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using pk3DS.Core.NX;
using ThreeDS.Progressive.Services.Switch;
internal static class SwordSurpriseChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var text=new byte[SwordSurprisePreflight.TextSize];var empty=SwordSurprisePreflight.AuditText(text);
        check(empty.Signatures.Count==12&&!empty.SignaturesMatch&&empty.Signatures.All(s=>!s.Matches),"Surprise_PreflightRejectsAllZeroInstructions");
        Convert.FromHexString("602A40B9E1031F2A21F7FF97E00F1F32").CopyTo(text,0x766D64);
        var single=SwordSurprisePreflight.AuditText(text);
        check(single.Signatures.Count(s=>s.Matches)==1&&!single.SignaturesMatch,"Surprise_PreflightDoesNotAcceptPartialSignatureSet");
        byte[] nso=new byte[0x100+text.Length];"NSO0"u8.CopyTo(nso);Convert.FromHexString(SwordSurprisePreflight.BuildId).CopyTo(nso,0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(nso.AsSpan(0x10),0x100);BinaryPrimitives.WriteUInt32LittleEndian(nso.AsSpan(0x18),(uint)text.Length);text.CopyTo(nso,0x100);
        check(SwordSurprisePreflight.Audit(nso).TextSha256==Convert.ToHexString(SHA256.HashData(text)),"Surprise_PreflightReadsUncompressedNsoWithoutMutation");
        byte[] original=SHA256.HashData(nso);_ = SwordSurprisePreflight.Audit(nso);check(original.SequenceEqual(SHA256.HashData(nso)),"Surprise_PreflightNeverWritesExecutable");
        BinaryPrimitives.WriteUInt32LittleEndian(nso.AsSpan(0xC),8);reject(()=>SwordSurprisePreflight.Audit(nso),"Surprise_PreflightRejectsTextHashMismatch");SHA256.HashData(text).CopyTo(nso,0xA0);
        check(SwordSurprisePreflight.Audit(nso).Signatures.Count==12,"Surprise_PreflightAcceptsVerifiedTextHash");
        nso[0x40]^=1;bool rejected=false;try{SwordSurprisePreflight.Audit(nso);}catch(NotSupportedException){rejected=true;}check(rejected,"Surprise_PreflightRejectsOtherBuildsIncludingShield");nso[0x40]^=1;
        BinaryPrimitives.WriteUInt32LittleEndian(nso.AsSpan(0x10),uint.MaxValue);reject(()=>SwordSurprisePreflight.Audit(nso),"Surprise_PreflightRejectsOutOfBoundsSegment");
        reject(()=>SwordSurprisePreflight.AuditText(text.AsSpan(0,100)),"Surprise_PreflightRejectsWrongTextLength");
    }
    internal static void Probe(string root)
    {
        var project=SwordShieldProject.Open(root);
        if(project.Identity.TitleId!="0100ABF008968000")throw new NotSupportedException("Solo Espada en esta investigación.");
        byte[] nso=File.ReadAllBytes(Path.Combine(root,"exefs/main"));byte[] original=SHA256.HashData(nso);var audit=SwordSurprisePreflight.Audit(nso);
        foreach(var signature in audit.Signatures)Console.WriteLine($"{(signature.Matches?"MATCH":"MISMATCH")}: {signature.Name} · 0x{signature.Offset:X}");
        if(!audit.SignaturesMatch)throw new InvalidDataException("Una o más firmas difieren.");
        if(!SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"exefs/main"))).SequenceEqual(original))throw new IOException("El origen cambió durante la lectura.");
        Console.WriteLine($"PASS read-only Sword preflight: {audit.Signatures.Count} instruction signatures, .text SHA256 {audit.TextSha256}. No runtime patch emitted; gameplay not tested.");
    }
}
