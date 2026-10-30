using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using pk3DS.Core.Modding.Research;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

/// <summary>Editable Shop.cro slot. BP price is ignored for regular marts.</summary>
public sealed class MartSlotRow : INotifyPropertyChanged
{
    private int _itemId, _price;
    public int Number { get; set; }
    public int ItemId { get => _itemId; set { if (_itemId == value) return; _itemId = value; Changed(); } }
    public int Price { get => _price; set { if (_price == value) return; _price = value; Changed(); } }
    public MartSlotRow(int number, int itemId, int price = 0)
    { Number = number; _itemId = itemId; _price = price; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class MartShop
{
    public int Index { get; }
    public bool IsBP { get; }
    public string Name { get; }
    public ObservableCollection<MartSlotRow> Rows { get; }
    public MartShop(int index, bool isBP, string name, IEnumerable<MartSlotRow> rows)
    { Index = index; IsBP = isBP; Name = name; Rows = new ObservableCollection<MartSlotRow>(rows); }
    public override string ToString() => Name;
}

public sealed record MartShopPreview(byte[] Original, byte[] Output,
    IReadOnlyList<BatchChange> Changes, Gen7ExpandedMartBuildReport Rebuild,
    byte[]? ItemOriginal, byte[]? ItemOutput)
{
    internal IReadOnlyList<FileChange> ExtraFiles {get;init;}=[];
}

/// <summary>
/// USUM shop editing on detached bytes. Uses the same relocation-aware Core
/// readers/builders as Progressive, never hard-coded stock Shop.cro offsets.
/// The BP tuple is (ushort item, ushort price); counts remain fixed.
/// </summary>
public sealed partial class GameMartService
{
    private static readonly string[] RegularNames =
    [
        "Sin pruebas", "1 prueba", "2 pruebas", "3 pruebas", "4 pruebas",
        "5 pruebas", "6 pruebas", "7 pruebas", "Konikoni · inciensos",
        "Konikoni · hierbas", "Hau'oli · objetos X", "Ruta 2 · varios",
        "Heahea · MT", "Avenida Royale · MT", "Ruta 8 · varios",
        "Paniola · Poké Balls", "Malie · MT", "Monte Hokulani · vitaminas",
        "Aldea Marina · MT", "Konikoni · MT", "Konikoni · piedras",
        "Supermercado · izquierda", "Supermercado · centro",
        "Supermercado · derecha", "Ruta 3 · objetos X", "Konikoni · objetos X",
        "Aldea Tapu · objetos X", "Monte Lanakila · objetos X"
    ];
    private static readonly string[] BpNames =
    [
        "Battle Royal · medicina", "Battle Royal · EV",
        "Battle Royal · equipamiento", "Battle Tree · evolución",
        "Battle Tree · equipamiento", "Battle Tree · megapiedras",
        "Playas · medicina"
    ];
    private bool _singlePurchase;
    internal bool HasSinglePurchase=>_singlePurchase;
    internal const string SinglePurchaseAction="marts.single-purchase-specials";
    public void SinglePurchaseSpecials()
    {
        if(!_project.GameConfiguration.USUM)throw new NotSupportedException("Solo un objeto por tienda especial está disponible únicamente en USUM.");
        _singlePurchase=true;if(!_presets.Contains(SinglePurchaseAction))_presets.Add(SinglePurchaseAction);
    }
    private readonly GameProjectSession _project;
    private readonly string _path;
    private readonly byte[] _original;
    private readonly ushort[][] _regularBefore;
    private readonly (ushort Item, ushort Price)[][] _bpBefore;
    private readonly byte[] _tutorsBefore;
    public IReadOnlyList<MartShop> RegularShops { get; }
    public IReadOnlyList<MartShop> BpShops { get; }

    public GameMartService(GameProjectSession project)
    {
        if (project.Generation!=7)
            throw new NotSupportedException("El editor de tiendas necesita SM o USUM.");
        _project = project;
        _path = Path.Combine(project.GameConfiguration.RomFS, "Shop.cro");
        _original = File.ReadAllBytes(_path);
        _singlePurchase=project.GameConfiguration.USUM&&Gen7SinglePurchaseStock.Read(_original) is not null;
        if(!project.GameConfiguration.USUM)
        {
            if(_original.Length<0x180||!_original.AsSpan(0x80,4).SequenceEqual("CRO0"u8))throw new InvalidDataException("Shop.cro SM no tiene cabecera CRO0.");
            _smRegularOffset=FindSmTable(_original,SmRegularSignature,SmRegularCounts.Sum()*2);
            _smBpOffset=FindSmTable(_original,SmBpSignature,SmBpCounts.Sum()*4);
            if(_smRegularOffset<_smBpOffset+SmBpCounts.Sum()*4&&_smBpOffset<_smRegularOffset+SmRegularCounts.Sum()*2)throw new InvalidDataException("Las tablas SM se superponen.");
            int cursor=_smRegularOffset;
            _regularBefore=SmRegularCounts.Select(count=>{var items=new ushort[count];for(int i=0;i<count;i++,cursor+=2)items[i]=BitConverter.ToUInt16(_original,cursor);return items;}).ToArray();
            cursor=_smBpOffset;
            _bpBefore=SmBpCounts.Select(count=>{var items=new (ushort Item,ushort Price)[count];for(int i=0;i<count;i++,cursor+=4)items[i]=(BitConverter.ToUInt16(_original,cursor),BitConverter.ToUInt16(_original,cursor+2));return items;}).ToArray();
            _tutorsBefore=[];
            RegularShops=_regularBefore.Select((items,i)=>new MartShop(i,false,RegularNames[i],items.Select((item,slot)=>new MartSlotRow(slot+1,item)))).ToArray();
            BpShops=_bpBefore.Select((items,i)=>new MartShop(i,true,BpNames[i],items.Select((item,slot)=>new MartSlotRow(slot+1,item.Item,item.Price)))).ToArray();
            Validate();return;
        }
        if (!Gen7ShopCroLayout.TryRead(_original, out var layout, out string error))
            throw new InvalidDataException("Shop.cro no tiene el layout USUM reconocido: " + error);
        if (layout.RegularMarts.Inventories.Count != RegularNames.Length ||
            layout.BPItems.Counts.Count != BpNames.Length)
            throw new InvalidDataException("Shop.cro contiene una cantidad de tiendas desconocida.");

        _regularBefore = layout.RegularMarts.Inventories.Select(x => (ushort[])x.Clone()).ToArray();
        _bpBefore = ReadBP(_original, layout.BPItems);
        _tutorsBefore = _original.AsSpan(checked((int)layout.BPTutors.DataStart),
            layout.BPTutors.DataLength).ToArray();
        RegularShops = _regularBefore.Select((items, index) =>
            new MartShop(index, false, RegularNames[index],
                items.Select((item, i) => new MartSlotRow(i + 1, item)))).ToArray();
        BpShops = _bpBefore.Select((items, index) =>
            new MartShop(index, true, BpNames[index],
                items.Select((item, i) => new MartSlotRow(i + 1, item.Item, item.Price)))).ToArray();
    }

    private static (ushort Item, ushort Price)[][] ReadBP(byte[] bytes, Gen7ShopGroupedTableSnapshot table)
    {
        var result = new (ushort Item, ushort Price)[table.ShopCount][];
        int cursor = checked((int)table.DataStart);
        for (int shop = 0; shop < result.Length; shop++)
        {
            result[shop] = new (ushort, ushort)[table.Counts[shop]];
            for (int slot = 0; slot < result[shop].Length; slot++, cursor += 4)
                result[shop][slot] = (BitConverter.ToUInt16(bytes, cursor),
                                      BitConverter.ToUInt16(bytes, cursor + 2));
        }
        if (cursor != checked((int)table.DataStart + table.DataLength))
            throw new InvalidDataException("La longitud de la tabla BP es inconsistente.");
        return result;
    }

    private void Validate()
    {
        foreach (MartShop shop in RegularShops.Concat(BpShops))
        {
            if (shop.Rows.Count == 0 || shop.Rows.Count > (shop.IsBP
                    ? _bpBefore[shop.Index].Length : Gen7ExpandedMartTable.MaximumSlotsPerMart))
                throw new ArgumentException("Cantidad de ranuras inválida en " + shop.Name);
            if (shop.IsBP && shop.Rows.Count != _bpBefore[shop.Index].Length)
                throw new InvalidOperationException("Las tiendas BP tienen cantidad de ranuras fija.");
            foreach (MartSlotRow row in shop.Rows)
            {
                if (row.ItemId < 0 || row.ItemId > ushort.MaxValue ||
                    row.ItemId >= _project.ItemNames.Count)
                    throw new ArgumentException($"Objeto inválido en {shop.Name}, ranura {row.Number}: {row.ItemId}.");
                if (shop.IsBP && row.Price is < 0 or > ushort.MaxValue)
                    throw new ArgumentException($"Precio BP inválido en {shop.Name}, ranura {row.Number}.");
            }
        }
    }

    public MartShopPreview Prepare()
    {
        Validate();
        if(!SupportsExpansion)return PrepareSm();
        ushort[][] inventories = RegularShops
            .Select(shop => shop.Rows.Select(row => checked((ushort)row.ItemId)).ToArray())
            .ToArray();
        if (!Gen7ExpandedMartTable.TryRebuild(_original, inventories,
                out byte[] updated, out var report, out string error))
            throw new InvalidDataException("No se pudo reconstruir la tabla regular: " + error);
        if (!Gen7ShopCroLayout.TryRead(updated, out var layout, out error))
            throw new InvalidDataException("Shop.cro reconstruido no puede volver a leerse: " + error);
        if (layout.BPItems.Counts.Count != _bpBefore.Length ||
            layout.BPItems.Counts.Where((count, i) => count != _bpBefore[i].Length).Any())
            throw new InvalidDataException("Las tiendas BP cambiaron de estructura al reconstruir.");

        int cursor = checked((int)layout.BPItems.DataStart);
        foreach (var shop in BpShops)
            foreach (var row in shop.Rows)
            {
                BitConverter.GetBytes(checked((ushort)row.ItemId)).CopyTo(updated, cursor);
                BitConverter.GetBytes(checked((ushort)row.Price)).CopyTo(updated, cursor + 2);
                cursor += 4;
            }
        if (!Gen7ShopCroLayout.TryRead(updated, out var verified, out error))
            throw new InvalidDataException("La tabla final no pasó la validación CRO: " + error);
        if (verified.BPTutors.DataLength != _tutorsBefore.Length ||
            !_tutorsBefore.AsSpan().SequenceEqual(updated.AsSpan(
                checked((int)verified.BPTutors.DataStart), verified.BPTutors.DataLength)))
            throw new InvalidDataException("La tabla de tutores BP cambió durante la reconstrucción. No se guardará.");
        if (!verified.RegularMarts.Inventories.Select((items, index) =>
                items.SequenceEqual(inventories[index])).All(x => x))
            throw new InvalidDataException("La comprobación de inventarios regulares no coincide.");
        var finalBp = ReadBP(updated, verified.BPItems);
        for (int shop = 0; shop < finalBp.Length; shop++)
            for (int slot = 0; slot < finalBp[shop].Length; slot++)
                if (finalBp[shop][slot].Item != BpShops[shop].Rows[slot].ItemId ||
                    finalBp[shop][slot].Price != BpShops[shop].Rows[slot].Price)
                    throw new InvalidDataException("La comprobación de tiendas BP no coincide.");

        var changes = new List<BatchChange>();
        for (int shop = 0; shop < RegularShops.Count; shop++)
        {
            var before = _regularBefore[shop];
            var after = inventories[shop];
            for (int slot = 0; slot < Math.Max(before.Length, after.Length); slot++)
            {
                string from = slot < before.Length ? ItemLabel(before[slot]) : "(sin ranura)";
                string to = slot < after.Length ? ItemLabel(after[slot]) : "(ranura eliminada)";
                if (from != to || slot >= before.Length || slot >= after.Length)
                    changes.Add(new BatchChange(shop, RegularNames[shop], $"Ranura {slot+1}", from, to));
            }
        }
        for (int shop = 0; shop < BpShops.Count; shop++)
            for (int slot = 0; slot < _bpBefore[shop].Length; slot++)
            {
                var before = _bpBefore[shop][slot];
                var after = finalBp[shop][slot];
                if (before.Item != after.Item)
                    changes.Add(new BatchChange(shop, BpNames[shop], $"Objeto {slot+1}",
                        ItemLabel(before.Item), ItemLabel(after.Item)));
                if (before.Price != after.Price)
                    changes.Add(new BatchChange(shop, BpNames[shop], $"BP ranura {slot+1}",
                        before.Price.ToString(), after.Price.ToString()));
            }
        if (changes.Count == 0 && !_original.AsSpan().SequenceEqual(updated))
            throw new InvalidDataException("El CRO cambió sin cambios de tiendas detectables.");
        var items = PrepareItemPatches();
        changes.AddRange(items.Changes);
        var extra=new List<FileChange>();
        if(_project.GameConfiguration.USUM&&(_singlePurchase||Gen7SinglePurchaseStock.Read(_original) is not null))
        {
            var installed=Gen7SinglePurchaseStock.Read(_original);
            var message=SinglePurchaseShopText.Prepare(_project,installed?.MessageId);
            byte[] patched=Gen7SinglePurchaseStock.Apply(updated,message.Id);
            if(!patched.AsSpan().SequenceEqual(updated))changes.Add(new(-1,"Tiendas especiales","Una compra por objeto y tienda",installed is null?"Sin límite":"Registro anterior",$"{Gen7SinglePurchaseStock.CurrentOffers(patched).Count} ofertas · guardar y reiniciar conserva el stock"));
            updated=patched;extra.AddRange(message.Files);
            if(message.Files.Any(f=>f.Changed))changes.Add(new(-1,"Tiendas especiales","Mensaje de agotado","Sin mensaje propio","Ya compraste este objeto. · todos los idiomas"));
            var croFiles=new List<byte[]>();
            foreach(string path in Directory.EnumerateFiles(_project.GameConfiguration.RomFS).Where(p=>Path.GetExtension(p).Equals(".cro",StringComparison.OrdinalIgnoreCase)))
            {
                if(string.Equals(path,_path,StringComparison.OrdinalIgnoreCase)){croFiles.Add(updated);continue;}
                byte[] bytes=File.ReadAllBytes(path);croFiles.Add(bytes);extra.Add(new(path,bytes,bytes));
            }
            string crrPath=Path.Combine(_project.GameConfiguration.RomFS,".crr","static.crr");byte[] crr=File.ReadAllBytes(crrPath);
            extra.Add(new(crrPath,crr,CroIntegrity.RebuildCrr(crr,croFiles)));
            report=report with{FinalFileSize=updated.Length,CodeBytesAdded=report.CodeBytesAdded+Math.Max(0,updated.Length-report.FinalFileSize)};
        }
        return new MartShopPreview((byte[])_original.Clone(), updated, changes, report,
            items.Original, items.Output){ExtraFiles=extra};
    }

    private readonly Dictionary<(bool,int),string> _presetResults=[];
    private static string ShopValues(MartShop shop)=>string.Join(";",shop.Rows.Select(r=>r.ItemId+":"+r.Price));
    private sealed class EndPreset(Action finish):IDisposable {public void Dispose()=>finish();}
    private IDisposable TrackPreset()
    {
        var before=RegularShops.Concat(BpShops).ToDictionary(s=>(s.IsBP,s.Index),ShopValues);
        return new EndPreset(()=>{foreach(var shop in RegularShops.Concat(BpShops)){string after=ShopValues(shop);if(before[(shop.IsBP,shop.Index)]!=after)_presetResults[(shop.IsBP,shop.Index)]=after;}});
    }
    private bool ManualShop(MartShop shop,MartShopPreview preview)=>preview.Changes.Any(c=>c.Id==shop.Index&&c.Name==shop.Name)&&(!_presetResults.TryGetValue((shop.IsBP,shop.Index),out var preset)||preset!=ShopValues(shop));
    internal void RestoreManualItemEffects(System.Text.Json.Nodes.JsonObject payload)
    {
        if(payload["SinglePurchase"]?.GetValue<bool>()==true)SinglePurchaseSpecials();
        _setCandyPrice=payload["CandyPrice"]?.GetValue<bool>()??false;_setKeyPrice=payload["KeyPrice"]?.GetValue<bool>()??false;_enableVitamin252=payload["Vitamin252"]?.GetValue<bool>()??false;
        _vitaminIds=payload["VitaminIds"]?.AsArray().Select(n=>n!.GetValue<int>()).ToArray()??[];
    }
    public void Apply(MartShopPreview preview)
    {
        using var manual=ManualOperationScope.Current is not null?null:(Environment.GetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY")=="1"&&!_singlePurchase)||(!_singlePurchase&&!RegularShops.Concat(BpShops).Any(s=>ManualShop(s,preview)))?null:ManualOperationScope.Begin("shops7","shops7",new{Shops=RegularShops.Concat(BpShops).Where(s=>Environment.GetEnvironmentVariable("DEFINITIVE_BATCH_REPLAY")!="1"&&ManualShop(s,preview)).Select(s=>new{s.Index,s.IsBP,Rows=s.Rows.Select(r=>new{r.ItemId,r.Price}).ToArray()}).ToArray(),CandyPrice=_setCandyPrice,KeyPrice=_setKeyPrice,Vitamin252=_enableVitamin252,VitaminIds=_vitaminIds,SinglePurchase=_singlePurchase});
        if (preview.Changes.Count == 0)
            throw new InvalidOperationException("No hay cambios para guardar.");
        var fresh = Prepare();
        if (!fresh.Output.AsSpan().SequenceEqual(preview.Output) ||
            !(fresh.ItemOutput??[]).AsSpan().SequenceEqual(preview.ItemOutput??[]) ||
            !(fresh.ExtraFiles??[]).Select(f=>f.Path+":"+Convert.ToHexString(SHA256.HashData(f.Proposed()))).SequenceEqual((preview.ExtraFiles??[]).Select(f=>f.Path+":"+Convert.ToHexString(SHA256.HashData(f.Proposed())))))
            throw new InvalidOperationException("La propuesta cambió desde la vista previa. Generá otra.");
        byte[] onDisk = GarcTransaction.ReadUnchanged(_path, SHA256.HashData(_original));
        var files = new List<FileChange> { new(_path, onDisk, preview.Output) };
        if (preview.ItemOriginal is not null && preview.ItemOutput is not null)
        {
            var reference = _project.GameConfiguration.GetGARCReference("item");
            string itemPath = Path.Combine(_project.GameConfiguration.RomFS, reference.Reference);
            byte[] current = GarcTransaction.ReadUnchanged(itemPath,
                SHA256.HashData(preview.ItemOriginal));
            files.Add(new FileChange(itemPath, current, preview.ItemOutput));
        }
        foreach(var file in preview.ExtraFiles??[]){GarcTransaction.ReadUnchanged(file.Path,file.Fingerprint);files.Add(file);}
        _project.History.Commit(files, "Tiendas y BP",
            _presets.Count == 0 ? "Editar tiendas" : "Aplicar presets de tiendas",
            preview.Changes, new Dictionary<string,string>
            {
                ["Origen"] = "Shop.cro / relocaciones verificadas",
                ["Acciones"] = string.Join(";", _presets),
                ["Scope"] = _project.GameConfiguration.USUM?"USUM tiendas normales y BP":"SM tiendas normales y BP"
            });
        _project.RefreshMoveAssignments();
    }

    // Shared by the row action and toolbar, so existing slots and newly added
    // slots follow exactly the same rules. BP slots are intentionally fixed.
    public static bool TryRemoveRegularSlot(MartShop shop, MartSlotRow row)
    {
        if (shop.IsBP || shop.Rows.Count <= 1 || !shop.Rows.Contains(row))
            return false;
        shop.Rows.Remove(row);
        for (int i = 0; i < shop.Rows.Count; i++)
            shop.Rows[i].Number = i + 1;
        return true;
    }

    public void FreeMegaStoneBP()
    {
        using var preset=TrackPreset();
        if (BpShops.Count <= 5)
            throw new InvalidDataException("No existe la tienda BP de megapiedras.");
        foreach (var row in BpShops[5].Rows)
            row.Price = 0;
        _presets.Add("marts.free-mega-stones");
    }

    private string ItemLabel(int id) =>
        id >= 0 && id < _project.ItemNames.Count &&
        !string.IsNullOrWhiteSpace(_project.ItemNames[id])
            ? $"{_project.ItemNames[id]} (#{id})" : $"Objeto #{id}";
}
