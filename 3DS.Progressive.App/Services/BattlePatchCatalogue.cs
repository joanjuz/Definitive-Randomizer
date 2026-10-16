namespace ThreeDS.Progressive.Services;
internal sealed record TemplateChoice(string Value,string Label,string Description="");
internal static class BattlePatchCatalogue
{
    internal static IReadOnlyList<TemplateChoice> For(GameProjectSession p,int move)
    {
        var rows=new List<TemplateChoice>{new("","None","No instala una mecánica de combate especial. No desinstala parches que ya estén presentes en la ROM.")};
        void Add(int id,string token,string description){if(move==id)rows.Add(new(token,token,description+" Requiere una versión de Battle.cro compatible; se comprueba antes de guardar."));}
        if(move==588&&(p.Generation==6||p.GameConfiguration.USUM))rows.Add(new(p.Generation==6?"Gen6KingShieldMinusOne":"Gen7KingShieldMinusOne",p.Generation==6?"Gen6KingShieldMinusOne":"Gen7KingShieldMinusOne","Escudo Real reduce un nivel de Ataque al atacante que hace contacto, en lugar de dos. Requiere un módulo de combate compatible."));
        if(!p.GameConfiguration.USUM)return rows;
        Add(96,"Gen7MeditateV73","Meditación cura el problema de estado principal del objetivo y aumenta un nivel su Ataque.");
        Add(108,"Gen7SmokescreenV82","Pantalla de Humo protege al usuario y baja un nivel la Precisión del objetivo. Solo tiene éxito en el primer turno activo del usuario.");
        Add(587,"Gen7FairyLockAllFairyV11","Cerrojo Feérico convierte en Hada puro a todos los Pokémon activos.");
        Add(381,"Gen7LuckyChantCritBoost","Conjuro mantiene la protección contra golpes críticos y añade un nivel de probabilidad de crítico al bando protegido durante ocho turnos.");
        Add(54,"Gen7MistOwnerActiveV5762","Neblina mantiene la protección contra reducciones de estadísticas mientras su usuario original siga activo. Termina si sale o se debilita.");
        Add(693,"Gen7BrutalSwingSelfConfusionV1","Giro Vil aplica confusión al propio usuario y adapta la selección de golpes múltiples contra varios rivales. Potencia, golpes y objetivo se configuran en sus columnas.");
        Add(273,"Gen7WishPivot","Deseo conserva la curación diferida y retira al usuario para enviar un compañero. No transfiere cambios de estadísticas.");
        Add(171,"Gen7NightmareSleepV76","Pesadilla duerme al objetivo si está despierto; si ya está dormido, aplica el efecto original de Pesadilla.");
        Add(277,"Gen7MagicCoatThreeTurns","Capa Mágica prolonga su efecto a tres turnos. Este cambio se aplica a los datos del movimiento.");
        Add(300,"Gen7MudSportFullBlock","Chapoteolodo usa el bloqueo de tipo Eléctrico de Progressive. Requiere Param0x0B en el template.");
        Add(346,"Gen7WaterSportFullBlock","Hidrochorro usa el bloqueo de tipo Fuego de Progressive. Requiere Param0x0B en el template.");
        return rows;
    }
}
