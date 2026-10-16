using System.Globalization;
namespace ThreeDS.Progressive.Services;

internal sealed record KnownTextPurpose(string Title,string Category);
/// <summary>Version-specific bank purposes reviewed against local vanilla Game Text.</summary>
internal static class KnownTextCatalogue
{
    internal static readonly string[] Categories=["Pokédex","Movimientos y habilidades","Objetos y mochila","Entrenadores y combate","Pokémon y equipo","PC y cajas","Conexión e intercambios","Regalo Misterioso","Menús y opciones","Guardado y errores","Personalización","Minijuegos y actividades","Instalaciones de combate","Lugares y mapas","Créditos","Bases Secretas","Concursos","Poké Recreo","Superentrenamiento","Festi Plaza","Pokémonturas","Poderes O","Recursos técnicos"];
    private static readonly Dictionary<string,Dictionary<int,KnownTextPurpose>> Tables=Build();
    internal static KnownTextPurpose? Find(string game,int bankCount,int id)
    {
        int expected=game switch{"XY"=>151,"ORAS"=>175,"SM"=>122,"USUM"=>127,_=>-1};
        return bankCount==expected && Tables.TryGetValue(game,out var table)?table.GetValueOrDefault(id):null;
    }
    private static Dictionary<string,Dictionary<int,KnownTextPurpose>> Build()
    {
        var all=new Dictionary<string,Dictionary<int,KnownTextPurpose>>();
        foreach(string game in new[]{"XY","ORAS","SM","USUM"})all[game]=[];
        Add("XY","Pokédex","Categorías, formas, medidas y entradas Pokédex","1 2 3 4 5 6 7 64 80");
        Add("XY","Movimientos y habilidades","Movimientos, habilidades y naturalezas","12 13 14 15 17 27 33 34 47");
        Add("XY","Objetos y mochila","Objetos, bayas y mochila","11 77 95 96 97 98 99 139 140");
        Add("XY","Entrenadores y combate","Entrenadores y mensajes de combate","20 21 22 28 29 32 107 118 122 124 125 126 127");
        Add("XY","Pokémon y equipo","Ficha, evolución y recuerdos de Pokémon","25 26 30 36 40 45 51 65 93 121 129");
        Add("XY","PC y cajas","Almacenamiento y fondos de cajas","74 91 92 128");
        Add("XY","Conexión e intercambios","PSS, comunicación e intercambios","54 55 56 57 58 59 61 68 78 81 84 104 106 111 117 119 134 136 137 138 148");
        Add("XY","Regalo Misterioso","Regalos y eventos de distribución","108 109 149");
        Add("XY","Menús y opciones","Menús, opciones y presentación","8 9 18 19 35 44 52 71 75 85 101 114 133 135");
        Add("XY","Guardado y errores","Descargas y almacenamiento externo","82");
        Add("XY","Personalización","Ropa y personalización","143 144 145 146");
        Add("XY","Minijuegos y actividades","Actividades, fotografías y televisión","60 73 83 86 87 88 89 102 103 105 110 113 150");
        Add("XY","Instalaciones de combate","Bastión y Mansión Batalla","115 116 130 131");
        Add("XY","Lugares y mapas","Nombres y referencias de lugares","0 31 69 70 72");
        Add("XY","Créditos","Créditos y canción final","37 38");
        Add("XY","Poké Recreo","Poké Recreo y Pokélitos","66 67");
        Add("XY","Superentrenamiento","Superentrenamiento y sacos","23 24 41 42");
        Add("XY","Poderes O","Poderes O","141");
        Add("XY","Recursos técnicos","Identificadores internos y recursos heredados","76 79 94 112 132");

        Add("ORAS","Pokédex","Categorías, formas, medidas y entradas Pokédex","1 2 3 4 5 6 7 78 98");
        Add("ORAS","Movimientos y habilidades","Movimientos, habilidades y naturalezas","12 14 15 16 18 28 36 37 51");
        Add("ORAS","Objetos y mochila","Objetos, bayas y mochila","11 95 113 114 115 116 117 163 164");
        Add("ORAS","Entrenadores y combate","Entrenadores y mensajes de combate","21 22 23 29 30 34 35 123 126 137 145 147 148 149 150");
        Add("ORAS","Pokémon y equipo","Ficha, evolución y recuerdos de Pokémon","26 27 31 39 43 49 64 79 111 144 152");
        Add("ORAS","PC y cajas","Almacenamiento y fondos de cajas","92 109 110 151");
        Add("ORAS","Conexión e intercambios","PSS, comunicación e intercambios","67 68 69 70 71 72 85 96 99 102 122 125 130 136 142 158 160 161 162 172");
        Add("ORAS","Regalo Misterioso","Regalos y eventos de distribución","127 128 173");
        Add("ORAS","Menús y opciones","Menús, opciones y presentación","8 9 19 20 38 48 65 93 103 119 133 156 159");
        Add("ORAS","Guardado y errores","Descargas y almacenamiento externo","100");
        Add("ORAS","Minijuegos y actividades","Televisión y actividades","47 75 80 81 82 83 101 104 105 106 107 124");
        Add("ORAS","Instalaciones de combate","Entrenadores de instalaciones","153 154");
        Add("ORAS","Lugares y mapas","Nombres y referencias de lugares","0 33 84 86 87 90");
        Add("ORAS","Créditos","Créditos","41");
        Add("ORAS","Bases Secretas","Bases Secretas y decoración","32 52 53 54 55 56 57 58 59 60 88 138 157");
        Add("ORAS","Concursos","Concursos y exhibiciones","13 139 140 141 143");
        Add("ORAS","Poké Recreo","Poké Recreo y Pokélitos","80 81");
        Add("ORAS","Superentrenamiento","Superentrenamiento y sacos","24 25 44 45");
        Add("ORAS","Poderes O","Poderes O","165");
        Add("ORAS","Recursos técnicos","Identificadores internos y recursos heredados","94 112 131 155");

        foreach(string game in new[]{"SM","USUM"})
        {
            bool ultra=game=="USUM";
            string Ids(string sm,string usum)=>ultra?usum:sm;
            Add(game,"Objetos y mochila","Objetos, bayas y mochila",Ids("1 2 35 36 37 38 65 95","1 2 39 40 41 42 70 100"));
            Add(game,"Pokédex","Categorías, formas, medidas y entradas Pokédex",Ids("55 80 114 115 116 117 118 119 120 121","60 85 119 120 121 122 123 124 125 126"));
            Add(game,"Movimientos y habilidades","Movimientos, habilidades y naturalezas",Ids("19 87 96 97 103 107 111 112 113","19 92 101 102 108 112 116 117 118"));
            Add(game,"Entrenadores y combate","Entrenadores y mensajes de combate",Ids("3 5 6 7 13 14 15 17 18 20 21 26 28 98 104 105 106","3 5 6 7 13 14 15 17 18 20 21 30 32 103 109 110 111"));
            Add(game,"Pokémon y equipo","Ficha, evolución y nombres de Pokémon",Ids("10 12 73 74 76 81 89 90 92 93 102 110","10 12 78 79 81 86 94 95 97 98 107 115"));
            Add(game,"PC y cajas","Almacenamiento de Pokémon y cajas","11");
            Add(game,"Conexión e intercambios","Comunicación, torneos e intercambios",Ids("8 9 25 27 29 39 40 43 52 54 59 60 62 64 70 77 78 79 101","8 9 25 31 33 43 44 47 56 59 64 65 67 69 75 82 83 84 106"));
            Add(game,"Regalo Misterioso","Regalos y eventos de distribución",Ids("56 57 58","61 62 63"));
            Add(game,"Menús y opciones","Menús, opciones y presentación",Ids("4 22 24 32 33 34 51 63 66 72 94","4 22 24 36 37 38 55 68 71 77 99"));
            Add(game,"Guardado y errores","Guardado, descargas y almacenamiento",Ids("53 61","26 27 28 29 58 66"));
            Add(game,"Personalización","Ropa y personalización",Ids("49 50","53 54"));
            Add(game,"Festi Plaza","Festi Plaza, negocios y saludos",Ids("41 42 44 45 46 47 48 75 88","45 46 48 49 50 51 52 80 93"));
            Add(game,"Instalaciones de combate","Estadio Royale y Árbol de Combate",Ids("85 86 99","90 91 104"));
            Add(game,"Lugares y mapas","Nombres y referencias de lugares",Ids("67 68 69 100","72 73 74 105"));
            Add(game,"Pokémonturas","Pokémonturas",Ids("82 83","87 88"));
            Add(game,"Créditos","Créditos",Ids("91","96"));
            Add(game,"Recursos técnicos","Recursos heredados y etiquetas internas",Ids("109","113 114"));
        }
        Add("USUM","Minijuegos y actividades","Surfeo Mantine","57");
        Add("XY","Pokémon y equipo","Selección y datos de Pokémon","16 63");
        Add("XY","Recursos técnicos","Separadores y etiquetas internas","100 120 142");
        Add("ORAS","Pokémon y equipo","Selección y datos de Pokémon","17 77");
        Add("ORAS","Recursos técnicos","Etiquetas heredadas","132");
        Add("SM","Entrenadores y combate","Climas y efectos de campo","16");
        Add("USUM","Entrenadores y combate","Climas y efectos de campo","16");
        Add("SM","Minijuegos y actividades","Fotografías","30");
        Add("USUM","Minijuegos y actividades","Fotografías","34");
        Add("SM","Recursos técnicos","Marcadores de interfaz","31");
        Add("USUM","Recursos técnicos","Marcadores de interfaz","35");
        Add("USUM","Recursos técnicos","Marcadores y cadena numérica de interfaz","0");
        return all;
        void Add(string game,string category,string title,string ids)
        {
            foreach(string id in ids.Split(' ',StringSplitOptions.RemoveEmptyEntries)) all[game][int.Parse(id,CultureInfo.InvariantCulture)]=new(title,category);
        }
    }
}
