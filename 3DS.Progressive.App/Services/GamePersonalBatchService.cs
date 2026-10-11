using System.IO;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Randomizers;
using pk3DS.Core.Structures.PersonalInfo;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

public sealed partial class GameProjectSession
{
    private readonly Guid _batchOwner = Guid.NewGuid();
    private static readonly object RandomGate = new();
    public bool TrappingAbilitiesBanned { get; private set; }
    public bool SupportsHms => _config.Generation == 6;
    public bool SupportsSpecialTutors => _config.ORAS || _config.USUM;

    public BatchPreview PreviewPersonal(BatchAction action, BatchSettings s, IReadOnlyList<int> scope)
    {
        BatchAlgorithms.Validate(s);
        if (action is not (BatchAction.PersonalRandomize or BatchAction.PersonalModify or BatchAction.BanTrappingAbilities))
            throw new ArgumentException("Acción de Pokémon no reconocida.");
        int[] ids = scope.Distinct().Order().ToArray();
        if (ids.Length == 0 || ids.Any(i => i < 1 || i >= EntryCount))
            throw new ArgumentException("El alcance no contiene entradas válidas.");
        byte[][] files = new GARC.MemGARC(GarcTransaction.ReadUnchanged(_personalPath, _diskFingerprint)).Files;
        // Reject inconsistent or partial tables before letting Core see their records.
        int size = _config.XY ? PersonalInfoXY.SIZE : _config.ORAS ? PersonalInfoORAS.SIZE : PersonalInfoSM.SIZE;
        if (files.Length != EntryCount + 1 || files.Take(files.Length - 1).Any(f => f.Length != size) ||
            !files.Take(files.Length - 1).SelectMany(f => f).SequenceEqual(files[^1]))
            throw new InvalidDataException("La tabla Personal está incompleta o no coincide con su tabla maestra.");
        var table = new PersonalTable((byte[])files[^1].Clone(), _config.Version).Table;
        var changes = new List<BatchChange>();
        int[] specialCounts = _config.ORAS ? [15, 17, 16, 15]
            : _config.USUM ? [BeachTutorMoveIds.Count > 0 ? Math.Min(80, BeachTutorMoveIds.Count) : 67] : [];
        if (!SupportsHms && (s.Hms || s.FullHms) || !SupportsSpecialTutors && (s.SpecialTutors || s.FullSpecialTutors) ||
            Generation != 7 && s.SetCall)
            throw new ArgumentException("Las opciones seleccionadas no corresponden a este juego.");
        lock (RandomGate)
        {
            // Core captures Util.Rand in its constructor, so reseed BEFORE constructing it.
            Util.ReseedRand(s.Seed);
            var randomizer = new PersonalRandomizer(table, _config)
            {
                TypeCount = Types.Count, ModifyStats = s.Stats, ShuffleStats = s.Shuffle,
                StatsToRandomize = (bool[])s.StatSlots.Clone(), StatDeviation = s.Deviation,
                ModifyTypes = s.Types, SameTypeChance = s.SingleTypeChance,
                ModifyAbilities = s.Abilities, AllowWonderGuard = s.WonderGuard,
                BanTrappingAbilities = s.BanTraps || TrappingAbilitiesBanned,
                ModifyHeldItems = s.Items, ModifyCatchRate = s.Catch,
                ModifyEggGroup = s.Eggs, SameEggGroupChance = s.SameEggChance,
                ModifyLearnsetTM = s.Tms, ModifyLearnsetHM = SupportsHms && s.Hms,
                ModifyLearnsetTypeTutors = s.TypeTutors,
                ModifyLearnsetMoveTutors = SupportsSpecialTutors && s.SpecialTutors,
            };
            foreach (int id in ids)
            {
                var before = new PersonalTable((byte[])files[id].Clone(), _config.Version).Table[0];
                var after = table[id];
                if (action == BatchAction.PersonalRandomize) randomizer.Randomize(after, id);
                if (action == BatchAction.PersonalModify)
                    BatchAlgorithms.ModifyPersonal(after, s, Generation, _config.XY ? 5 : 7, specialCounts);
                if (action == BatchAction.BanTrappingAbilities || s.BanTraps || TrappingAbilitiesBanned)
                    BatchAlgorithms.BanTraps(after, id, _config.Info.MaxAbilityID);
                files[id] = (byte[])after.Write().Clone();
                string name = Entries.FirstOrDefault(e => e.Index == id)?.Name ?? $"Entrada {id}";
                changes.AddRange(BatchDifferences.Personal(id, name, before, after, Types, AbilityNames, ItemNames, Generation));
            }
        }
        GarcTransaction.RebuildPersonalMaster(files);
        return new BatchPreview(_batchOwner, _diskFingerprint, files,
            action == BatchAction.PersonalRandomize ? "Randomizar Pokémon" :
            action == BatchAction.PersonalModify ? "Modificar Pokémon" : "Excluir Shadow Tag y Arena Trap",
            s.Seed, ids.Length, changes, s.BanTraps || action == BatchAction.BanTrappingAbilities);
    }

    public void ApplyPersonalPreview(BatchPreview preview)
    {
        if (preview.Owner != _batchOwner || !preview.Fingerprint.AsSpan().SequenceEqual(_diskFingerprint))
            throw new InvalidOperationException("La vista previa pertenece a otra sesión o quedó desactualizada.");
        _diskFingerprint = GarcTransaction.Commit(_personalPath, _config.GetGARCReference("personal"),
            preview.Fingerprint, preview.CopyRecords());
        _config.InitializePersonal();
        if (preview.BanTraps) TrappingAbilitiesBanned = true;
    }
}
