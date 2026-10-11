// Default balance rows ported exactly from the Gen6/Gen7 Progressive move editors (f459e40).
namespace ThreeDS.Progressive.Services;
internal static class BuiltInMoveBalance
{
    internal static MoveTemplates.MovePatchRow[] ForGeneration(int generation) => generation == 7 ? Gen7() : Gen6();
    private static MoveTemplates.MovePatchRow[] Gen6()
    {
        const int Confusion = 6;

        return
        [
            new MoveTemplates.MovePatchRow { Move = 15, Power = 70, Accuracy = 100, PP = 15, CriticalStage = 1 }, // Cut
        new MoveTemplates.MovePatchRow { Move = 249, Power = 60, Accuracy = 100 }, // Rock Smash
        new MoveTemplates.MovePatchRow { Move = 19, Power = 100, Accuracy = 100 }, // Fly
        new MoveTemplates.MovePatchRow { Move = 147, Accuracy = 85 }, // Spore
        new MoveTemplates.MovePatchRow { Move = 137, Accuracy = 90, PP = 15 }, // Glare
        new MoveTemplates.MovePatchRow { Move = 591, Power = 95, Accuracy = 95, PP = 10, ClearStatEffects = true }, // Diamond Storm
        new MoveTemplates.MovePatchRow { Move = 613, Power = 80, Accuracy = 100, Heal = 50 }, // Oblivion Wing
        new MoveTemplates.MovePatchRow { Move = 448, Power = 80, Accuracy = 100, PP = 15, Inflict = Confusion, InflictChance = 15 }, // Chatter
        new MoveTemplates.MovePatchRow { Move = 141, Power = 70, Accuracy = 100, PP = 15 }, // Leech Life
        new MoveTemplates.MovePatchRow { Move = 188, Power = 60, Accuracy = 100 }, // Sludge Bomb
        new MoveTemplates.MovePatchRow { Move = 552, Power = 70, Accuracy = 100 }, // Fiery Dance
        new MoveTemplates.MovePatchRow { Move = 594, Power = 20, Accuracy = 100 }, // Water Shuriken

        new MoveTemplates.MovePatchRow { Move = 182, PP = 7 }, // Protect
        new MoveTemplates.MovePatchRow { Move = 197, PP = 7 }, // Detect
        new MoveTemplates.MovePatchRow { Move = 596, PP = 7 }, // Spiky Shield
        new MoveTemplates.MovePatchRow { Move = 588, PP = 7, KingShieldAttackMinusOne = true }, // King's Shield
        new MoveTemplates.MovePatchRow { Move = 476, PP = 7 }, // Rage Powder
        new MoveTemplates.MovePatchRow { Move = 266, PP = 7 }, // Follow Me
        new MoveTemplates.MovePatchRow { Move = 502, PP = 7 }, // Ally Switch
        new MoveTemplates.MovePatchRow { Move = 73, PP = 7 }, // Leech Seed
        new MoveTemplates.MovePatchRow { Move = 281, PP = 7 }, // Yawn

        new MoveTemplates.MovePatchRow { Move = 270, PP = 5 }, // Helping Hand
        new MoveTemplates.MovePatchRow { Move = 105, PP = 5 }, // Recover
        new MoveTemplates.MovePatchRow { Move = 234, PP = 5 }, // Morning Sun
        new MoveTemplates.MovePatchRow { Move = 236, PP = 5 }, // Moonlight
        new MoveTemplates.MovePatchRow { Move = 208, PP = 5 }, // Milk Drink
        new MoveTemplates.MovePatchRow { Move = 303, PP = 5 }, // Slack Off
        new MoveTemplates.MovePatchRow { Move = 235, PP = 5 }, // Synthesis
        new MoveTemplates.MovePatchRow { Move = 456, PP = 5 }, // Heal Order
        new MoveTemplates.MovePatchRow { Move = 135, PP = 5 }, // Soft-Boiled
        new MoveTemplates.MovePatchRow { Move = 355, PP = 5 }, // Roost
        new MoveTemplates.MovePatchRow { Move = 505, PP = 5 }, // Heal Pulse
    ];
    }
    private static MoveTemplates.MovePatchRow[] Gen7()
    {
        const int Confusion = 6;

        return
        [
            // Página 1 del PDF
            // Corte: 70 power, 100 accuracy, 15 PP, critical stage 1.
            new MoveTemplates.MovePatchRow { Move = 15, Power = 70, Accuracy = 100, PP = 15, CriticalStage = 1 },

        // Golpe Roca
        new MoveTemplates.MovePatchRow { Move = 249, Power = 60, Accuracy = 100 },

        // Vuelo
        new MoveTemplates.MovePatchRow { Move = 19, Power = 100, Accuracy = 100 },

        // Espora
        new MoveTemplates.MovePatchRow { Move = 147, Accuracy = 85 },

        // Deslumbrar
        new MoveTemplates.MovePatchRow { Move = 137, Accuracy = 90, PP = 15 },

        // Torm. Diamantes / Diamond Storm: remover boost secundario.
        new MoveTemplates.MovePatchRow { Move = 591, Power = 95, Accuracy = 95, PP = 10, ClearStatEffects = true },

        // Ala Mortífera / Oblivion Wing
        new MoveTemplates.MovePatchRow { Move = 613, Power = 80, Accuracy = 100, Heal = 50 },

        // Cháchara / Chatter
        new MoveTemplates.MovePatchRow { Move = 448, Power = 80, Accuracy = 100, PP = 15, Inflict = Confusion, InflictChance = 15 },

        // Chupavidas / Leech Life
        new MoveTemplates.MovePatchRow { Move = 141, Power = 70, Accuracy = 100, PP = 15 },

        // Bomba Fango / Sludge Bomb
        new MoveTemplates.MovePatchRow { Move = 188, Power = 60, Accuracy = 100 },

        // Danza Llama / Fire Spin
        new MoveTemplates.MovePatchRow { Move = 552, Power = 70, Accuracy = 100 },

        // Shuriken de Agua / Water Shuriken: 20 por golpe.
        new MoveTemplates.MovePatchRow { Move = 594, Power = 20, Accuracy = 100 },

        // PP 7
        new MoveTemplates.MovePatchRow { Move = 182, PP = 7 }, // Protección / Protect
        new MoveTemplates.MovePatchRow { Move = 197, PP = 7 }, // Detección / Detect
        new MoveTemplates.MovePatchRow { Move = 596, PP = 7 }, // Barrera Espinosa / Spiky Shield
        new MoveTemplates.MovePatchRow { Move = 588, PP = 7, KingShieldAttackMinusOne = true }, // Escudo Real / King's Shield
        new MoveTemplates.MovePatchRow { Move = 476, PP = 7 }, // Polvo Ira / Rage Powder
        new MoveTemplates.MovePatchRow { Move = 266, PP = 7 }, // Señuelo / Follow Me
        new MoveTemplates.MovePatchRow { Move = 502, PP = 7 }, // Cambio Banda / Ally Switch
        new MoveTemplates.MovePatchRow { Move = 73,  PP = 7 }, // Drenadoras / Leech Seed

        // Página 2 del PDF
        new MoveTemplates.MovePatchRow { Move = 281, PP = 7 }, // Bostezo / Yawn

        // PP 5
        new MoveTemplates.MovePatchRow { Move = 270, PP = 5 }, // Refuerzo / Helping Hand
        new MoveTemplates.MovePatchRow { Move = 105, PP = 5 }, // Recuperación / Recover
        new MoveTemplates.MovePatchRow { Move = 234, PP = 5 }, // Sol Matinal / Morning Sun
        new MoveTemplates.MovePatchRow { Move = 236, PP = 5 }, // Luz Lunar / Moonlight
        new MoveTemplates.MovePatchRow { Move = 208, PP = 5 }, // Batido / Milk Drink
        new MoveTemplates.MovePatchRow { Move = 303, PP = 5 }, // Relajo / Slack Off
        new MoveTemplates.MovePatchRow { Move = 235, PP = 5 }, // Síntesis / Synthesis
        new MoveTemplates.MovePatchRow { Move = 456, PP = 5 }, // Auxilio / Heal Order
        new MoveTemplates.MovePatchRow { Move = 135, PP = 5 }, // Amortiguador / Soft-Boiled
        new MoveTemplates.MovePatchRow { Move = 355, PP = 5 }, // Respiro / Roost
        new MoveTemplates.MovePatchRow { Move = 505, PP = 5 }, // Pulso Cura / Heal Pulse
    ];
    }
}
