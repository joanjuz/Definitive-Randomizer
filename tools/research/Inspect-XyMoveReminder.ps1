param(
    [Parameter(Mandatory=$true)][string]$Project,
    [ValidateRange(0,7)][int]$Language = 2,
    [ValidateRange(0,1000)][int]$StoryBank = 96,
    [switch]$ShowText
)
# Read-only investigation. Output is metadata; do not commit private text output or game resources.
$ErrorActionPreference = 'Stop'
if (-not ('XyReminderInspection' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Text;
public static class XyReminderInspection {
 public static byte[] Slice(byte[] b,int at,int length) { if(at<0||length<0||at>b.Length-length)throw new FormatException("Slice outside resource"); byte[] r=new byte[length];Array.Copy(b,at,r,0,length);return r; }
 public static int Count(byte[] b) { if(Encoding.ASCII.GetString(b,0,4)!="CRAG")throw new FormatException("Not GARC");return BitConverter.ToUInt16(b,BitConverter.ToInt32(b,4)+8); }
 public static byte[] Member(byte[] b,int id) { if(id<0||id>=Count(b))throw new FormatException("Member outside GARC");int h=BitConverter.ToInt32(b,4),t=h+BitConverter.ToInt32(b,h+4),e=t+12+BitConverter.ToInt32(b,h+12+id*4);if(BitConverter.ToUInt32(b,e)!=1)throw new FormatException("Expected single-file member");return Slice(b,checked(BitConverter.ToInt32(b,16)+BitConverter.ToInt32(b,e+4)),BitConverter.ToInt32(b,e+12)); }
 public static byte[] Lz(byte[] b) { if(b[0]!=17)return b;int n=BitConverter.ToInt32(b,0)>>8,p=4,o=0;byte[] r=new byte[n];while(o<n){int f=b[p++];for(int k=7;k>=0&&o<n;k--){if((f&(1<<k))==0){r[o++]=b[p++];continue;}int a=b[p++],l=a>>4,d;if(l==0){int c=b[p++],e=b[p++];l=((a&15)<<4)+(c>>4)+17;d=((c&15)<<8)+e+1;}else if(l==1){int c=b[p++],e=b[p++],g=b[p++];l=((a&15)<<12)+(c<<4)+(e>>4)+273;d=((e&15)<<8)+g+1;}else{l++;d=((a&15)<<8)+b[p++]+1;}if(d>o||l>n-o)throw new FormatException("Invalid LZ11 reference");for(int j=0;j<l;j++){r[o]=r[o-d];o++;}}}return r; }
 public static int[] Cells(byte[] b) { if(BitConverter.ToUInt32(b,4)!=0x0A0AF1E0)throw new FormatException("Expected AMX script");int p=BitConverter.ToInt32(b,12),end=BitConverter.ToInt32(b,20);if(end<p||(end-p)%4!=0)throw new FormatException("Invalid AMX bounds");int[] r=new int[(end-p)/4];for(int i=0;i<r.Length;i++){int a=b[p++],v=(a&64)!=0?(a&127)-128:a&127,n=1;while((a&128)!=0){if(n++==5)throw new FormatException("Invalid AMX cell");a=b[p++];v=(v<<7)|(a&127);}r[i]=v;}return r; }
 public static byte[] CellBytes(int[] cells) { byte[] r=new byte[cells.Length*4];Buffer.BlockCopy(cells,0,r,0,r.Length);return r; }
 public static string[] Text(byte[] b) {int section=BitConverter.ToInt32(b,12),n=BitConverter.ToUInt16(b,2);string[] r=new string[n];for(int i=0;i<n;i++){int at=section+BitConverter.ToInt32(b,section+4+i*8),len=BitConverter.ToUInt16(b,section+8+i*8);ushort key=unchecked((ushort)(0x7C89+i*0x2983));var s=new StringBuilder();for(int j=0;j<len;j++){ushort v=(ushort)(BitConverter.ToUInt16(b,at+j*2)^key);key=(ushort)((key<<3)|(key>>13));if(v==0)break;if(v==0xBE00||v==0xBE01||v==0xBE02)s.Append(" ");else if(v==16){s.Append("[control]");}else s.Append((char)v);}r[i]=s.ToString();}return r; }
}
'@
}
function Read-Resource([string]$Relative) { return ,[IO.File]::ReadAllBytes((Join-Path $Project $Relative)) }
function Hash([byte[]]$Data) { $sha=[Security.Cryptography.SHA256]::Create();try { return ([BitConverter]::ToString($sha.ComputeHash($Data))).Replace('-','') } finally {$sha.Dispose()} }
$exheader=Read-Resource 'DecryptedExHeader.bin'
$title=[BitConverter]::ToUInt64($exheader,0x200).ToString('X16')
if($title -notin @('0004000000055D00','0004000000055E00')) { throw "Not a supported X/Y title: $title" }
$zones=Read-Resource 'ExtractedRomFS/a/0/1/2'
$zone=[XyReminderInspection]::Lz([XyReminderInspection]::Member($zones,34))
if([Text.Encoding]::ASCII.GetString($zone,0,2) -ne 'ZO') { throw 'Zone 34 is not ZO' }
$head=[BitConverter]::ToInt32($zone,4);$table=[BitConverter]::ToInt32($zone,8)
if($zone[$table+5] -lt 3) { throw 'Zone 34 has no NPC index 2' }
$at=$table+12+20*$zone[$table+4]+48*2
$record=[XyReminderInspection]::Slice($zone,$at,48)
$area=[BitConverter]::ToUInt16($zone,$head+2)
$models=Read-Resource 'ExtractedRomFS/a/1/4/9'
$entries=@();$present=@();$maximum=0
for($i=0;$i -lt [XyReminderInspection]::Count($models);$i++) {
 $list=[XyReminderInspection]::Member($models,$i)
 if($list.Length%24 -ne 0) { throw "Invalid model list $i" }
 $maximum=[Math]::Max($maximum,$list.Length/24)
 for($j=0;$j -lt $list.Length;$j+=24) {if([BitConverter]::ToUInt16($list,$j) -eq 54) {$entries+=Hash ([XyReminderInspection]::Slice($list,$j,24));$present+=$i}}
}
$common=Read-Resource 'ExtractedRomFS/a/0/3/1'
$script=[XyReminderInspection]::Lz([XyReminderInspection]::Member($common,42))
$cells=[XyReminderInspection]::Cells($script)
$languageStory=80+$Language;$storyPath='ExtractedRomFS/a/{0}/{1}/{2}' -f ([Math]::Floor($languageStory/100)),([Math]::Floor($languageStory/10)%10),($languageStory%10)
$story=Read-Resource $storyPath
$inspectedStory=[XyReminderInspection]::Lz([XyReminderInspection]::Member($story,$StoryBank))
$serviceTextCandidates=@()
for($bank=0;$bank -lt [XyReminderInspection]::Count($story);$bank++) {
 try {
  $text=[XyReminderInspection]::Text([XyReminderInspection]::Lz([XyReminderInspection]::Member($story,$bank)))
  $matches=@(for($line=0;$line -lt $text.Length;$line++){if($text[$line] -match 'Heart Scale|Move Reminder|Move Maniac|relearn|Escama Coraz|recordar movimientos'){$line}})
  if($matches.Count -gt 0){$serviceTextCandidates+=[pscustomobject]@{Bank=$bank; Lines=$matches}}
 } catch { }
}
$languageGame=72+$Language;$gamePath='ExtractedRomFS/a/{0}/{1}/{2}' -f ([Math]::Floor($languageGame/100)),([Math]::Floor($languageGame/10)%10),($languageGame%10)
$names=[XyReminderInspection]::Text([XyReminderInspection]::Lz([XyReminderInspection]::Member((Read-Resource $gamePath),72)))
$code=Read-Resource 'ExtractedExeFS/.code.bin'
$text96Hashes=@()
for($lang=0;$lang -lt 8;$lang++) {
 $number=80+$lang;$path='ExtractedRomFS/a/{0}/{1}/{2}' -f ([Math]::Floor($number/100)),([Math]::Floor($number/10)%10),($number%10)
 $archive=Read-Resource $path
 $text96Hashes+=[pscustomobject]@{Language=$lang;Path=$path;Member96Sha256=Hash ([XyReminderInspection]::Lz([XyReminderInspection]::Member($archive,96)))}
}
$menuResourceHashes=@()
foreach($name in @('DllField.cro','DllWazaOmoidashi.cro','DllPokeList.cro','DllWazaOboe.cro')) {
 $menuResourceHashes+=[pscustomobject]@{Resource=$name;Sha256=Hash (Read-Resource ('ExtractedRomFS/'+$name))}
}
# Discover a range row by all fields, rather than assuming an offset or build.
$dispatch=@()
for($i=0;$i -le $code.Length-16;$i+=4) {if([BitConverter]::ToUInt16($code,$i) -eq 2260 -and [BitConverter]::ToUInt16($code,$i+2) -eq 2299 -and [BitConverter]::ToUInt16($code,$i+4) -eq 42 -and [BitConverter]::ToUInt16($code,$i+6) -eq 1 -and [BitConverter]::ToUInt32($code,$i+8) -eq 94 -and [BitConverter]::ToUInt32($code,$i+12) -eq 0){$dispatch+=('0x{0:X}' -f $i)}}
[pscustomobject]@{
 TitleId=$title; Zone=34; Location=$names[([BitConverter]::ToUInt16($zone,$head+28)-band 1023)]; Area=$area
 NpcIndex=2; NpcId=[BitConverter]::ToUInt16($record,0); Model=[BitConverter]::ToUInt16($record,2); Movement=[BitConverter]::ToUInt16($record,4)
 AppearanceFlag=[BitConverter]::ToUInt16($record,8); ScriptId=[BitConverter]::ToUInt16($record,10); Facing=[BitConverter]::ToUInt16($record,12)
 X=[BitConverter]::ToUInt16($record,40);Y=[BitConverter]::ToUInt16($record,42)
 RecordSha256=Hash $record; ZoneSha256=Hash $zone; CodeSha256=Hash $code
 Common42Sha256=Hash $script; Common42CellsSha256=Hash ([XyReminderInspection]::CellBytes($cells)); InspectedStoryBank=$StoryBank; InspectedStoryBankSha256=Hash $inspectedStory
 Common42ScriptIdCellIndices=@(for($i=0;$i -lt $cells.Length;$i++){if($cells[$i] -eq 2261){$i}})
 DispatchFileOffsets=$dispatch; Model54Areas=$present; Model54DistinctEntryHashes=@($entries|Sort-Object -Unique); MaximumModels=$maximum
 ServiceTextCandidates=$serviceTextCandidates
 AllLanguageStory96Hashes=$text96Hashes; PotentialMenuResourceHashes=$menuResourceHashes
 PendingProof='Native text/context selection and portability outside the source zone; game validation is not performed by this probe.'
} | ConvertTo-Json -Depth 5
if($ShowText) {
 foreach($bank in @($StoryBank)+@($serviceTextCandidates|ForEach-Object Bank)|Select-Object -Unique) {
  $data=[XyReminderInspection]::Lz([XyReminderInspection]::Member($story,$bank))
  Write-Output ("Story{0} SHA256: {1}" -f $bank,(Hash $data))
  $lines=[XyReminderInspection]::Text($data)
  for($i=0;$i -lt $lines.Length;$i++){Write-Output ("Story{0}[{1}]: {2}" -f $bank,$i,$lines[$i])}
 }
}
