using System.Collections.Concurrent;

namespace Helper.Runtime.Knowledge.Retrieval;

internal sealed record RetrievalDomainProfile(string Domain, string[] Keywords);

internal static class RetrievalDomainProfileCatalog
{
    private static readonly IReadOnlyDictionary<string, RetrievalDomainProfile> Profiles =
        new Dictionary<string, RetrievalDomainProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["analysis_strategy"] = new("analysis_strategy", new[] { "стратег", "стратегия", "непрям", "эврист", "быстрого", "медленного", "мышлен", "решений", "антихрупкость", "черные", "лебеди", "taleb", "kahneman", "clausewitz", "rumelt" }),
            ["anatomy"] = new("anatomy", new[] { "анатом", "кость", "череп", "черепа", "артер", "вен", "нерв", "сплет", "сплетение", "плечевое", "виллизия", "кишки", "кишка", "кишечн", "тонкой", "стенку", "стенка", "слои", "большеберцовая", "малоберцовая", "отверстия", "organ", "bone", "nerve", "plexus", "netter", "gray" }),
            ["art_culture"] = new("art_culture", new[] { "искус", "живоп", "живописи", "эпох", "эпохи", "европ", "европейской", "культ", "культуре", "единобожия", "религ", "священном", "нехудожественный", "текст", "ясным", "сильным", "стилю", "псевдонауку", "суеверное", "writing", "art", "paint", "relig", "sacred", "monotheism", "gombrich", "armstrong", "zinsser", "sagan", "druyan", "smith" }),
            ["biology"] = new("biology", new[] { "клетк", "мембр", "мембрана", "трансп", "ген", "генов", "экспрессия", "транскрипция", "эволюц", "отбор", "митоз", "мейоз", "синапс", "сигнал", "нейромедиатор", "биолог", "campbell", "alberts", "sapolsky", "biolog", "cell", "gene", "membrane" }),
            ["chemistry"] = new("chemistry", new[] { "хими", "реакци", "кислот", "кислотность", "основан", "основность", "органич", "органических", "соединен", "соединений", "молекул", "термодинами", "равновес", "шателье", "chem", "reaction", "sn1", "sn2" }),
            ["computer_science"] = new("computer_science", new[] { "програм", "алгори", "архит", "прило", "модул", "грани", "код", "поддер", "проек", "завис", "csharp", "dotnet", "softw", "depen", "gener", "maint", "clean", "solid", "injec" }),
            ["economics"] = new("economics", new[] { "эконом", "экономике", "рынок", "спрос", "предлож", "инфляц", "эластичность", "богате", "нации", "сравнительное", "преимущество", "торговле", "системное", "мышление", "искажения", "тренды", "mankiw", "acemoglu", "robinson", "rosling", "meadows", "trade", "demand", "supply" }),
            ["encyclopedias"] = new("encyclopedias", new[] { "где", "кто", "что", "кратко", "кратком", "определении", "термин", "when", "where", "atlas", "dictionary", "энциклопед", "словар" }),
            ["english_lang_lit"] = new("english_lang_lit", new[] { "shakespeare", "шекспир", "комедии", "трагедии", "двоемыслие", "английск", "английской", "литерат", "литературе", "литературы", "orwell", "орвелл", "austen", "роман", "антиутоп", "doublethink", "literature", "1984", "animal", "farm", "pride", "prejudice", "hamlet", "macbeth" }),
            ["entomology"] = new("entomology", new[] { "насеком", "мурав", "муравьев", "энтомолог", "энтомол", "крыл", "крылья", "каст", "коммуникац", "коммуникации", "колони", "колонии", "insect", "ant", "metamorph", "членистоног" }),
            ["geology"] = new("geology", new[] { "геолог", "минералы", "минерал", "пород", "местор", "магмат", "золоторудные", "золото", "рудн", "ударный", "кратер", "хромитовые", "платиновые", "микроскопом", "gold", "ore", "crater", "mineral", "klein" }),
            ["historical_encyclopedias"] = new("historical_encyclopedias", new[] { "ссср", "экслибрис", "ибсен", "анголе", "италии", "историко", "контексте", "энциклопедическом", "культурной", "теме", "кратко", "бсэ", "советск" }),
            ["history"] = new("history", new[] { "истор", "войн", "войне", "первой", "мировой", "революц", "импер", "древн", "древнего", "государств", "востока", "неолит", "римск", "индустриаль", "civilization", "war" }),
            ["linguistics"] = new("linguistics", new[] { "язык", "языках", "лингв", "граммат", "грамматическое", "число", "аналитические", "синтетических", "порядок", "слов", "sov", "тональный", "фонет", "морфолог", "категор", "syntax", "language", "ergative", "эргативность" }),
            ["math"] = new("math", new[] { "матем", "теорем", "матриц", "собственные", "значения", "полнота", "вещественной", "прямой", "ряд", "уравнен", "analysis", "eigen", "integral" }),
            ["medicine"] = new("medicine", new[] { "болез", "симпт", "призн", "диагн", "лечен", "терап", "серде", "недос", "клини", "пневм", "бакте", "вирусн", "диабе", "диабет", "второго", "типа", "анеми", "гипер", "медиц", "patie", "clini", "heart", "failu", "diabe", "anemi", "pneum", "sympt" }),
            ["mythology_religion"] = new("mythology_religion", new[] { "миф", "мифе", "мифологии", "мифол", "бог", "боги", "ритуал", "религи", "греческой", "скандинавской", "олимпийские", "олимп", "зевс", "героя", "hamilton", "zeus", "olymp", "hero", "mythology" }),
            ["neuro"] = new("neuro", new[] { "нейрон", "нейрона", "синапс", "памят", "мозг", "мозгу", "потенциал", "действия", "потенциация", "долговременная", "зрительная", "ганглии", "базальные", "движении", "kandel", "neural", "brain", "synapse" }),
            ["philosophy"] = new("philosophy", new[] { "философ", "кант", "сократ", "сократа", "ницше", "императив", "диалог", "майевт", "reason", "ethics", "эмпиризм", "рационализм", "вещь" }),
            ["physics"] = new("physics", new[] { "физик", "физич", "наименьшего", "действия", "максвелл", "максвелла", "уравнен", "уравнения", "лагранж", "тензор", "квант", "boltzmann", "maxwell" }),
            ["psychology"] = new("psychology", new[] { "психолог", "фрейд", "юнг", "эго", "ид", "суперэго", "индивидуация", "интроверсия", "экстраверсия", "psyche", "psycholog" }),
            ["robotics"] = new("robotics", new[] { "робот", "робототех", "кинемат", "манипулятор", "кибернет", "кибернетика", "обратн", "денавита", "хартенберга", "dh", "robot", "control" }),
            ["russian_lang_lit"] = new("russian_lang_lit", new[] { "русск", "толст", "толстого", "чехов", "пушкин", "проз", "поэз", "литератур", "войны", "мира", "серебряного", "реалистической", "роман" }),
            ["sci_fi_concepts"] = new("sci_fi_concepts", new[] { "психоистория", "селдона", "кризис", "основание", "галактической", "империи", "азимова", "мул", "колонизации", "будущего", "foundation", "galactic", "asimov", "science fiction" }),
            ["social_sciences"] = new("social_sciences", new[] { "социаль", "морал", "переговор", "переговоры", "принципиаль", "принципиальные", "влияние", "эмоциональный", "интеллект", "когнитивное", "искажение", "убеждение", "haidt", "society", "social" }),
            ["virology"] = new("virology", new[] { "вирус", "вирион", "капсид", "репликац", "rna", "virus", "viral" })
        };

    private static readonly ConcurrentDictionary<string, HashSet<string>> IntentRoots = new(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyDictionary<string, string[]> RoutingHintsByDomain { get; } =
        Profiles.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Keywords,
            StringComparer.OrdinalIgnoreCase);

    internal static IEnumerable<string> Domains => Profiles.Keys;

    internal static IReadOnlyList<string> GetRoutingHints(string domain)
        => RoutingHintsByDomain.GetValueOrDefault(domain) ?? Array.Empty<string>();

    internal static HashSet<string> GetIntentRoots(string domain)
    {
        return IntentRoots.GetOrAdd(
            domain,
            static key => RetrievalTextProcessing.BuildIntentRootSet(GetRoutingHints(key)));
    }
}

