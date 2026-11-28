using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Services;
using ThreeDS.Progressive.Models;

internal static class Gen6MintIconChecks
{
    private static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        using var picture=new Bitmap(32,32);for(int y=4;y<28;y++)for(int x=4;x<28;x++)picture.SetPixel(x,y,x%2==0?Color.Red:y%3==0?Color.White:Color.Black);
        byte[] source=GameGraphicsCodec.Encode(picture,XLIMEncoding.RGB5A1,true,XLIMOrientation.Rotate90);
        byte[] converted=Gen6MintIconService.ConvertTexture(source);
        byte[] expanded=(byte[])converted.Clone();Gen6MintIconService.ExpandPalette(converted).CopyTo(expanded,0);
        using(var decoded=GameGraphicsCodec.Decode(expanded))
        using(var quantized=GameGraphicsCodec.Decode(source))
            check(decoded.Width==30&&decoded.Height==30&&decoded.GetPixel(10,13).ToArgb()==quantized.GetPixel(11,14).ToArgb(),"Gen6 icon conversion preserves visual orientation and crop");
        reject(()=>Gen6MintIconService.ExpandPalette(GameGraphicsCodec.Encode(picture,XLIMEncoding.RGB5A1)),"Gen6 icon rejects direct pixels that appeared empty in game");
        check(BitConverter.ToUInt16(converted,0)==2&&BitConverter.ToUInt16(converted,2)==4&&
            Gen6MintIconService.ExpandPalette(converted).Where((value,i)=>i%2==0&&(value&1)!=0).Any(),"Gen6 icon native palette contains visible alpha");
        foreach(int count in new[]{1,4,16,17,70})
        {
            byte[] fixture=new byte[2088];BitConverter.GetBytes((ushort)2).CopyTo(fixture,0);BitConverter.GetBytes((ushort)count).CopyTo(fixture,2);
            for(int i=0;i<count;i++)BitConverter.GetBytes((ushort)(i==0?0xfffe:0xf801)).CopyTo(fixture,4+i*2);
            int at=4+count*2;fixture[at]=count<=16?(byte)((count-1)<<4):(byte)(count-1);
            byte[] raw=Gen6MintIconService.ExpandPalette(fixture);
            check(BitConverter.ToUInt16(raw,0)==(count==1?0xfffe:0xf801)&&BitConverter.ToUInt16(raw,2)==0xfffe,"Gen6 native palette high-first and 16-color boundary "+count);
            if(count>1){fixture[at]=count<=16?(byte)(count-1):(byte)0;check(!Gen6MintIconService.ExpandPalette(fixture).SequenceEqual(raw),"Gen6 palette index mutation changes native first pixel "+count);}
        }
        using(var gradient=new Bitmap(32,32))
        {
            for(int y=4;y<28;y++)for(int x=4;x<28;x++)gradient.SetPixel(x,y,Color.FromArgb(255,x*8,y*8,32));
            byte[] input=GameGraphicsCodec.Encode(gradient,XLIMEncoding.RGB5A1,true,XLIMOrientation.Rotate90);
            byte[] quantized=Gen6MintIconService.ConvertTexture(input),raw=Gen6MintIconService.ExpandPalette(quantized);
            check(BitConverter.ToUInt16(quantized,2)<=70&&quantized.SequenceEqual(Gen6MintIconService.ConvertTexture(input)),"Gen6 palette reduction is bounded and deterministic");
            check(Enumerable.Range(0,1024).Count(i=>(BitConverter.ToUInt16(raw,2*i)&1)!=0)==24*24,"Gen6 palette reduction preserves every opaque and transparent pixel");
        }
        var ids=Gen6NatureMintCatalog.ExperimentalItemMap.Items.Select(m=>m.ItemId).ToArray();
        var sources=ids.Select(id=>new UsumMintIconSource(id,"synthetic",Hash(source),Hash(source))).ToArray();
        byte[][] textures=Enumerable.Range(0,21).Select(_=>(byte[])source.Clone()).ToArray();
        foreach(bool y in new[]{false,true})
        {
            using var f=new Fixture(GameVersion.XY);string tag=y?"Y":"X";
            var node=JsonNode.Parse(Gen6MintServiceChecks.Setup(f,y).GetRawText())!;
            string codePath=Path.Combine(f.Game.ExeFS,".code.bin"),iconPath=Path.Combine(f.Game.RomFS,Gen6MintIconService.ArchivePath);
            byte[] code=File.ReadAllBytes(codePath);Array.Resize(ref code,Gen6MintIconService.TableOffset+718*4);
            foreach(int id in ids)BitConverter.GetBytes(Gen6MintIconService.OriginalImage(id)).CopyTo(code,Gen6MintIconService.TableOffset+id*4);
            File.WriteAllBytes(codePath,code);Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
            byte[] icons=Gen6MintIconService.Pack(Enumerable.Range(0,579).Select(i=>new byte[]{1,2,(byte)i,4}).ToArray());File.WriteAllBytes(iconPath,icons);
            node["sha256"]!["ExeFS/.code.bin"]=Hash(code);node["sha256"]!["RomFS/a/0/9/4"]=Hash(icons);
            node["native"]!["icon_texture_sha256"]=new JsonArray(Enumerable.Range(0,21).Select(_=>JsonValue.Create(Hash(converted))).ToArray());
            JsonElement profile=JsonSerializer.SerializeToElement(node);
            var native=new Gen6NatureMintService(f.Session,true,_=>profile);native.Apply(native.Inspect());
            var service=new Gen6MintIconService(f.Session,_=>profile,sources);
            check(service.Inspect().State==Gen6MintNativeState.Original,"Gen6 icons recognize original references "+tag);
            var preview=service.Prepare(textures);check(preview.Proposal!.Files.Count==2&&File.ReadAllBytes(codePath).SequenceEqual(code),"Gen6 icon preview detached and two files "+tag);
            reject(()=>new Gen6MintIconService(f.Session,_=>profile,sources).Apply(preview),"Gen6 icon foreign preview rejected "+tag);
            var bad=(byte[])source.Clone();bad[0]^=1;reject(()=>service.Prepare([bad,..textures.Skip(1)]),"Gen6 icon source mutation rejected "+tag);
            var proposal=preview.Proposal!;byte[] vector=(byte[])icons.Clone();BitConverter.GetBytes(3).CopyTo(vector,0x1c+12+579*4+12);
            var metadata=new Dictionary<string,string>(proposal.Parameters){{"Tampered","true"}};
            var forged=new EditorProposal(proposal.Owner,proposal.Action,proposal.Seed,proposal.Files,proposal.Changes,metadata,proposal.Dependencies);
            reject(()=>service.Apply(preview with{Proposal=forged}),"Gen6 icon altered preview rejected "+tag);
            foreach(int failureAt in new[]{1,2})
            {
                reject(()=>FileTransaction.Commit(proposal.Files,index=>{if(index==failureAt)throw new IOException("Synthetic icon transaction failure");}),"Gen6 icon transaction failure "+tag+failureAt);
                check(File.ReadAllBytes(codePath).SequenceEqual(code)&&File.ReadAllBytes(iconPath).SequenceEqual(icons)&&f.Session.History.Read().Entries.Count==1,"Gen6 icon rollback preserves native transaction "+tag+failureAt);
            }
            File.WriteAllBytes(codePath,proposal.Files.Single(f=>f.Path==codePath).Proposed());
            check(service.Inspect().State==Gen6MintNativeState.Incompatible,"Gen6 icon partial installation rejected "+tag);File.WriteAllBytes(codePath,code);
            reject(()=>Gen6MintIconService.Records(vector),"Gen6 icon vectorized archive rejected "+tag);
            reject(()=>Gen6MintIconService.Records(icons[..30]),"Gen6 icon truncated archive rejected "+tag);
            service.Apply(preview);
            check(service.Inspect().State==Gen6MintNativeState.Installed&&native.Inspect().State==Gen6MintNativeState.Installed,"Gen6 icons and native handler recognized together "+tag);
            byte[] installedCode=File.ReadAllBytes(codePath),installedIcons=File.ReadAllBytes(iconPath);
            check(Gen6MintIconService.NormalizeCode(installedCode).SequenceEqual(code)&&Gen6MintIconService.NormalizeArchive(installedIcons).SequenceEqual(icons),"Gen6 icons preserve every unrelated byte "+tag);
            check(service.Prepare(textures).Proposal is null,"Gen6 icons idempotent no second proposal "+tag);
            var row=Gen6MintIconService.Records(installedIcons);row[^1][^1]^=1;File.WriteAllBytes(iconPath,Gen6MintIconService.Pack(row));
            check(service.Inspect().State==Gen6MintNativeState.Incompatible,"Gen6 icon unknown texture rejected "+tag);File.WriteAllBytes(iconPath,installedIcons);
            installedCode[0]^=1;File.WriteAllBytes(codePath,installedCode);check(service.Inspect().State==Gen6MintNativeState.Incompatible,"Gen6 icon unrelated executable mutation rejected "+tag);File.WriteAllBytes(codePath,proposal.Files.Single(p=>p.Path==codePath).Proposed());
            var entry=f.Session.History.Read().Entries.Last();RestorePoints.Undo(f.Session.History,f.Root,entry.Id);
            check(File.ReadAllBytes(codePath).SequenceEqual(code)&&File.ReadAllBytes(iconPath).SequenceEqual(icons)&&native.Inspect().State==Gen6MintNativeState.Installed,"Gen6 icon undo exact preserves native installation "+tag);
            var stale=service.Prepare(textures);code[0]^=1;File.WriteAllBytes(codePath,code);reject(()=>service.Apply(stale),"Gen6 icon stale preview rejected "+tag);
        }
    }
}
