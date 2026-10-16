using System.IO;

namespace ThreeDS.Progressive.Services;

internal sealed partial class MoveTemplateApplier
{
    private void Validate(MoveTemplates.MovePatchRow patch)
    {
        foreach (var property in typeof(MoveTemplates.MovePatchRow).GetProperties())
        {
            if (property.PropertyType != typeof(int?) || property.GetValue(patch) is not int value) continue;
            (int min, int max) = property.Name switch
            {
                "Priority" => (-8, 8), "Recoil" or "UserStatChange" or "TargetStatChange" or
                    "Stat1Change" or "Stat2Change" or "Stat3Change" => (-128, 127),
                "HitMin" or "HitMax" => (0, 15), "Effect" or "Inflict" => (0, 65535), _ => (0, 255),
            };
            if (value < min || value > max)
                throw new InvalidDataException($"Movimiento {patch.Move}: {property.Name} debe estar entre {min} y {max}.");
        }
        if (_generation != 7 && patch.ZEffect.HasValue)
            throw new InvalidDataException($"Movimiento {patch.Move}: los efectos Z requieren Gen 7.");
        Token(patch.Type, ResolveType, types.Length - 1, "tipo");
        Token(patch.Category, ResolveCategory, 2, "categoría");
        Token(patch.Quality, ResolveQuality, 255, "clasificación");
        Token(patch.Targeting, ResolveTargeting, 255, "objetivo");
        Token(patch.InflictToken, ResolveInflict, 65535, "estado");
        foreach (string stat in new[] { patch.UserStat, patch.TargetStat, patch.Stat1, patch.Stat2, patch.Stat3 })
            Token(stat, ResolveStat, 8, "estadística");
        ResolveFlags(patch.SetFlags, _generation == 7 ? typeof(pk3DS.Core.Structures.MoveFlag7) : typeof(pk3DS.Core.Structures.MoveFlag6));
        ResolveFlags(patch.UnsetFlags, _generation == 7 ? typeof(pk3DS.Core.Structures.MoveFlag7) : typeof(pk3DS.Core.Structures.MoveFlag6));
        void Token(string text, Func<string, int> resolve, int max, string label)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            int value = resolve(text);
            if (value < 0 || value > max)
                throw new InvalidDataException($"Movimiento {patch.Move}: {label} desconocido o inválido: {text}.");
        }
    }
}
