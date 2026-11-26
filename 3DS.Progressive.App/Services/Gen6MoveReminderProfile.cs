using pk3DS.Core;

namespace ThreeDS.Progressive.Services;

/// <summary>
/// Admission gate for the unfinished X/Y investigation. Identifying script 2261 in common member 42
/// does not prove its native context outside zone 034. There is deliberately no admitted production
/// profile: synthetic backend fixtures must never be sufficient authority to write a real project.
/// See MAPAS_GEN6_RECORDADOR.md before changing this gate.
/// </summary>
internal static class Gen6MoveReminderProfile
{
    internal sealed record Inspection(bool Allowed, string Reason, string Title,
        IReadOnlyList<(string Path, byte[] Fingerprint)> Dependencies);

    internal static Inspection Inspect(GameConfig config) => new(false,
        config.XY
            ? "El script 2261 está identificado, pero no se han acreditado sus dependencias nativas de contexto, textos y menú fuera de Pueblo Fresco. No se escribió nada; consulta MAPAS_GEN6_RECORDADOR.md."
            : "Añadir recordador se investiga exclusivamente para Pokémon X/Y; este juego no es compatible.",
        config.XY ? "XY" : config.Version.ToString(), []);
}
