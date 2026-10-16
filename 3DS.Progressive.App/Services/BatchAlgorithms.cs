using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using pk3DS.Core.Randomizers;
using ThreeDS.Progressive.Models;

namespace ThreeDS.Progressive.Services;

internal static class BatchAlgorithms
{
    internal static void Validate(BatchSettings s)
    {
        if (s.StatSlots.Length != 6 || s.Deviation is < 0 or > 100 ||
            s.SingleTypeChance is < 0 or > 100 || s.SameEggChance is < 0 or > 100 ||
            s.ExperiencePercent is < 0 or > 10000 || s.CatchValue is < 0 or > 255 ||
            s.CallValue is < 0 or > 255)
            throw new ArgumentException("Revisá los porcentajes y los valores de captura/SOS.");
        if (s.ClearCompatibility && (s.FullTms || s.FullHms || s.FullTypeTutors || s.FullSpecialTutors))
            throw new ArgumentException("Elegí quitar compatibilidades o hacerlas completas en una misma acción.");
    }

    internal static void ModifyPersonal(PersonalInfo p, BatchSettings s, int generation,
        int hmCount, int[] specialCounts)
    {
        Validate(s);
        if (s.RemoveEvs)
            p.EV_HP = p.EV_ATK = p.EV_DEF = p.EV_SPA = p.EV_SPD = p.EV_SPE = 0;
        if (s.SlowGrowth) p.EXPGrowth = 5;
        if (s.ScaleExperience)
        {
            decimal scaled = decimal.Round(p.BaseEXP * s.ExperiencePercent / 100m, 0, MidpointRounding.ToEven);
            if (scaled > ushort.MaxValue)
                throw new ArgumentException("La experiencia base resultante supera 65535. Reducí el porcentaje.");
            p.BaseEXP = (int)scaled;
        }
        if (s.ClearCompatibility || s.FullTms || s.FullHms)
        {
            var tms = p.TMHM;
            // Preserve reserved bits and Gen 6 HMs when clearing TMs for Metronome.
            if (s.ClearCompatibility || s.FullTms)
                Array.Fill(tms, s.FullTms, 0, Math.Min(100, tms.Length));
            if (s.FullHms && generation == 6)
                Array.Fill(tms, true, 100, Math.Min(hmCount, Math.Max(0, tms.Length - 100)));
            p.TMHM = tms;
        }
        if (s.ClearCompatibility || s.FullTypeTutors)
        {
            var tutors = p.TypeTutors;
            Array.Fill(tutors, s.FullTypeTutors, 0, Math.Min(generation == 6 && hmCount == 5 ? 7 : 8, tutors.Length));
            p.TypeTutors = tutors;
        }
        if (s.ClearCompatibility || s.FullSpecialTutors)
        {
            var tutors = p.SpecialTutors;
            for (int k = 0; k < Math.Min(tutors.Length, specialCounts.Length); k++)
                Array.Fill(tutors[k], s.FullSpecialTutors, 0, Math.Min(tutors[k].Length, specialCounts[k]));
            p.SpecialTutors = tutors;
        }
        if (s.QuickHatch) p.HatchCycles = 1;
        if (s.SetCatch) p.CatchRate = s.CatchValue;
        if (s.SetCall && generation == 7) p.EscapeRate = s.CallValue;
    }

    internal static void BanTraps(PersonalInfo p, int index, int maxAbility)
    {
        var abilities = p.Abilities;
        for (int slot = 0; slot < abilities.Length; slot++)
        {
            if (!PersonalRandomizer.IsTrappingAbility(abilities[slot])) continue;
            int replacement = abilities.FirstOrDefault(a => a > 0 && !PersonalRandomizer.IsTrappingAbility(a));
            if (replacement == 0)
            {
                replacement = 1 + (index * 3 + slot) % Math.Max(1, maxAbility);
                while (PersonalRandomizer.IsTrappingAbility(replacement))
                    replacement = replacement % Math.Max(1, maxAbility) + 1;
            }
            abilities[slot] = replacement;
        }
        p.Abilities = abilities;
    }

    internal static void ChangeMove(Move move, int id, BatchAction action, BatchSettings s, Random random, int typeCount)
    {
        if (action == BatchAction.Metronome)
        {
            // WinForms removes move 0: SelectedIndex 117/32 means real IDs 118/33.
            move.PP = id == 118 ? 40 : id == 33 ? 1 : 0;
            return;
        }
        if (action != BatchAction.MoveRandomize) throw new ArgumentException("Acción de movimientos no reconocida.");
        if (id is 165 or 174) return; // Protect real Struggle / Curse IDs, not selector offsets.
        if (s.MoveCategories && move.Category > 0) move.Category = random.Next(1, 3);
        if (s.MoveTypes) move.Type = random.Next(0, typeCount);
    }
}
