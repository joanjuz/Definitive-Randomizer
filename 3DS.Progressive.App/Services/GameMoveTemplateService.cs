using System.IO;
using System.Security.Cryptography;
using pk3DS.Core;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using ThreeDS.Progressive.Models;
using Legacy = ThreeDS.Progressive.Services.ProgressiveLegacy;

namespace ThreeDS.Progressive.Services;

public sealed partial class GameMoveService
{
    public bool SupportsProgressiveBalance=>_config.Generation==6||_config.USUM;
    public string DefaultTemplatePath => Path.Combine(AppContext.BaseDirectory, "custom_balance_templates", $"moves_gen{_config.Generation}.csv");

    private BatchPreview PreviewMoveTemplate(BatchSettings settings, IReadOnlyList<int> scope)
    {
        if(!SupportsProgressiveBalance)throw new InvalidOperationException("El balance de movimientos Progressive está disponible en XY, ORAS y USUM; no en Sol/Luna.");
        int[] ids = scope.Distinct().Order().ToArray();
        byte[] moveSource = GarcTransaction.ReadUnchanged(_path, _sha);
        byte[][] outer = new GARC.MemGARC(moveSource).Files;
        if (!_config.XY && outer.Length != 1) throw new InvalidDataException("GARC WD no reconocido.");
        byte[][] records = _config.XY ? outer : Mini.UnpackMini(outer[0], "WD");
        if (records is null || ids.Length == 0 || ids.Any(i => i < 1 || i >= records.Length))
            throw new ArgumentException("El alcance no contiene movimientos válidos.");
        string templatePath = Path.GetFullPath(settings.TemplatePath ?? DefaultTemplatePath);
        byte[] templateHash = SHA256.HashData(File.ReadAllBytes(templatePath));
        var rows = MoveTemplates.LoadMovePatches(templatePath, _config.GetText(TextName.MoveNames));
        if (rows.Length == 0 && settings.TemplatePath is null) rows = BuiltInMoveBalance.ForGeneration(_config.Generation);
        if (rows.Length == 0) throw new InvalidDataException("El CSV no contiene movimientos.");
        if (rows.Any(p => p.Move >= records.Length))
            throw new InvalidDataException("El template contiene un movimiento que no existe en este juego.");
        var wanted = ids.ToHashSet();
        var selected = rows.Where(p => wanted.Contains(p.Move)).ToArray();
        if (selected.Length == 0) throw new InvalidDataException("No hay filas del template dentro del alcance elegido.");
        var originals = selected.Select(p => p.Move).Distinct().ToDictionary(id => id, id => (byte[])records[id].Clone());
        var dependencies = new List<(string Path, byte[] Fingerprint)> { (templatePath, templateHash) };
        var files = new List<FileChange>();
        var changes = new List<BatchChange>();
        string work = Path.Combine(Path.GetTempPath(), "3ds-progressive-balance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(work, "RomFS"));
        Directory.CreateDirectory(Path.Combine(work, "ExeFS"));
        try
        {
            using var context = Legacy.Main.Enter(work);
            var applier = new MoveTemplateApplier(_config.Generation, Types.ToArray());
            var requests = new List<Legacy.CustomBattleEffectPatcher.BattlePatchRequest>();
            foreach (var row in selected)
            {
                if (records[row.Move].Length < (_config.Generation == 7 ? 0x28 : 0x22))
                    throw new InvalidDataException($"Movimiento {row.Move} incompleto.");
                applier.Apply(records[row.Move], row);
                ValidateBattleRequest(row);
                if (settings.BattlePatches)
                    Legacy.CustomBattleEffectPatcher.ApplyMoveDataSpecials(_config.Generation, row.Move, records[row.Move],
                        row.KingShieldAttackMinusOne, row.BattlePatch);
                if (NeedsExternalBattle(row))
                    requests.Add(new() { Move = row.Move, KingShieldAttackMinusOne = row.KingShieldAttackMinusOne,
                        BattlePatch = row.BattlePatch, Param0x0B = row.Param0x0B });
            }
            if (requests.Count > 0 && !settings.BattlePatches && settings.MoveDescriptions)
                throw new InvalidDataException("Las descripciones de este template requieren aplicar también sus mecánicas de combate.");
            if (requests.Count > 0 && settings.BattlePatches)
            {
                string croName = _config.Generation == 7 ? "Battle.cro" : "DllBattle.cro";
                string croPath = Path.Combine(_config.RomFS, croName);
                if (!File.Exists(croPath)) throw new FileNotFoundException($"El template requiere {croName}. No se modificó el proyecto.", croPath);
                byte[] before = File.ReadAllBytes(croPath);
                if (_config.Generation == 6) Gen6KingShieldSignature.Validate(before);
                dependencies.Add((croPath, SHA256.HashData(before)));
                string staged = Path.Combine(Legacy.Main.RomFSPath, croName);
                File.WriteAllBytes(staged, before);
                var errors = new List<string>();
                int result = Legacy.CustomBattleEffectPatcher.ApplyExternalPatches(_config.Generation, requests,
                    (_, message) => errors.Add(message));
                if (result < 0) throw new InvalidDataException("El archivo de combate no coincide con las versiones compatibles del template. " + string.Join(" ", errors));
                byte[] after = File.ReadAllBytes(staged);
                // Match MoveEditor7: install the requested effects once. Giro Vil relocates
                // the move registry; running stock-address installers again is not a validator.
                // Every installer checks its signatures; FileTransaction verifies frozen output.
                var change = new FileChange(croPath, before, after);
                if (change.Changed)
                {
                    files.Add(change);
                    changes.Add(new(0, croName, "Mecánicas de combate", $"{before.Length:N0} bytes · SHA256 {ShortHash(before)}",
                        $"{after.Length:N0} bytes · SHA256 {ShortHash(after)} · {requests.Count} solicitudes verificadas"));
                }
            }
            foreach (var pair in originals)
            {
                string name = Entries.FirstOrDefault(e => e.Id == pair.Key)?.Name ?? $"Movimiento {pair.Key}";
                Move before = _config.Generation == 7 ? new Move7(pair.Value) : new Move6(pair.Value);
                Move after = _config.Generation == 7 ? new Move7(records[pair.Key]) : new Move6(records[pair.Key]);
                changes.AddRange(BatchDifferences.AllMoveFields(pair.Key, name, before, after, Types));
            }
            if (settings.MoveDescriptions) PreviewDescriptions(selected, files, dependencies, changes);
            GarcTransaction.ReadUnchanged(templatePath, templateHash);
            return new BatchPreview(_batchOwner, _sha, records, "Balance y templates", 0, ids.Length, changes,
                additionalFiles: files, dependencies: dependencies,parameters:ProjectHistory.Settings(settings,ids,dependencies));
        }
        finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
    }

    private void PreviewDescriptions(MoveTemplates.MovePatchRow[] rows, List<FileChange> files,
        List<(string Path, byte[] Fingerprint)> dependencies, List<BatchChange> changes)
    {
        var descriptions = rows.Where(p => !string.IsNullOrWhiteSpace(p.InGameDescription))
            .GroupBy(p => p.Move).Select(g => g.Last()).ToArray();
        if (descriptions.Length == 0) return;
        var reference = _config.GetGARCReference("gametext").GetRelativeGARC(6);
        string path = Path.Combine(_config.RomFS, reference.Reference);
        if (!File.Exists(path)) throw new FileNotFoundException("Falta el banco de textos en español requerido por el template.", path);
        byte[] source = File.ReadAllBytes(path);
        dependencies.Add((path, SHA256.HashData(source)));
        var archive = new GARC.MemGARC(source);
        var textRef = _config.GameText.FirstOrDefault(t => t.Name == TextName.MoveFlavor)
            ?? throw new InvalidDataException("No se encontró el índice de descripciones de movimientos.");
        byte[][] records = archive.Files;
        if (textRef.Index >= records.Length) throw new InvalidDataException("El banco de textos está incompleto.");
        string[] lines = new TextFile(_config, records[textRef.Index], false).Lines;
        string[] originalLines = (string[])lines.Clone();
        foreach (var row in descriptions)
        {
            if (row.Move >= lines.Length) throw new InvalidDataException($"El movimiento {row.Move} no tiene una entrada de descripción.");
            string text = row.InGameDescription.Trim().Replace("\\r\\n", "\\n").Replace("\r\n", "\\n").Replace("\n", "\\n");
            if (lines[row.Move] == text) continue;
            changes.Add(new(row.Move, Entries.FirstOrDefault(e => e.Id == row.Move)?.Name ?? $"Movimiento {row.Move}",
                "Descripción en español", lines[row.Move].Replace("\\n", "\n"), text.Replace("\\n", "\n")));
            lines[row.Move] = text;
        }
        byte[] encoded = TextFile.GetBytes(_config, lines);
        // Preserve original encrypted lines that the template does not edit. The vanilla
        // writer trims strings, which otherwise changes whitespace in real text banks.
        byte[][] encryptedLines = new byte[lines.Length][];
        for (int i = 0; i < lines.Length; i++)
        {
            byte[] bank = lines[i] == originalLines[i] ? records[textRef.Index] : encoded;
            int section = checked((int)BitConverter.ToUInt32(bank, 12));
            int table = checked(section + 4 + i * 8);
            int offset = checked(section + BitConverter.ToInt32(bank, table));
            int length = checked(BitConverter.ToUInt16(bank, table + 4) * 2);
            if (offset < 0 || offset > bank.Length - length) throw new InvalidDataException("Línea de descripción fuera del banco.");
            encryptedLines[i] = bank.AsSpan(offset, length).ToArray();
        }
        var rebuilt = new TextFile(_config, records[textRef.Index], false) { LineData = encryptedLines };
        byte[] serialized = rebuilt.Data;
        if (!new TextFile(_config, serialized, false).Lines.SequenceEqual(lines))
            throw new InvalidDataException("Las descripciones no superaron la relectura.");
        // Rebuild only the modified text entry. Every other bank entry stays byte-for-byte intact.
        records[textRef.Index] = serialized;
        byte[] proposed = GarcTransaction.Build(source, reference, records, preserveRecordSize: false);
        var change = new FileChange(path, source, proposed);
        if (change.Changed) files.Add(change);
    }

    private static string ShortHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..12];

    private bool NeedsExternalBattle(MoveTemplates.MovePatchRow row)
    {
        if (row.KingShieldAttackMinusOne || (_config.Generation == 7 && row.Move is 300 or 346 && row.Param0x0B.HasValue)) return true;
        return MoveTemplates.SplitTokens(row.BattlePatch).Any(t => !IsDataOnlyToken(t));
    }

    private static bool IsDataOnlyToken(string token) => MoveTemplates.NormalizeToken(token) is
        "magiccoatthreeturns" or "gen7magiccoatthreeturns" or "capamagica3turnos";

    private void ValidateBattleRequest(MoveTemplates.MovePatchRow row)
    {
        foreach (string token in MoveTemplates.SplitTokens(row.BattlePatch))
        {
            string name = MoveTemplates.NormalizeToken(token);
            var request = new Legacy.CustomBattleEffectPatcher.BattlePatchRequest { Move = row.Move, BattlePatch = token };
            bool known = name is "kingshieldminusone" or "escudorealminusone" ||
                (_config.Generation == 6 && name == "gen6kingshieldminusone") ||
                (_config.Generation == 7 && (name == "gen7kingshieldminusone" || IsDataOnlyToken(token) ||
                    (row.Move is 300 or 346 && row.Param0x0B.HasValue && name is "gen7watersportfullblock" or "gen7mudsportfullblock") ||
                    (row.Move == 273 && name is "gen7wishpivot" or "wishpivot" or "deseopivot") ||
                    (row.Move == 171 && name is "gen7nightmaresleepv76" or "nightmaresleepv76" or "pesadillasuenov76") ||
                    Legacy.Gen7MeditateV73Patcher.IsRequested(request) || Legacy.Gen7FairyLockV11Patcher.IsRequested(request) ||
                    Legacy.Gen7LuckyChantCritPatcher.IsRequested(request) || Legacy.Gen7MistOwnerActivePatcher.IsRequested(request) ||
                    Legacy.Gen7BrutalSwingSelfConfusionPatcher.IsRequested(request)));
            if (!known) throw new InvalidDataException($"Movimiento {row.Move}: parche de combate desconocido o incompatible: {token}.");
        }
    }
}
