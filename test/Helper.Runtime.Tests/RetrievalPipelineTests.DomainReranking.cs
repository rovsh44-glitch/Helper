using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge.Retrieval;
using Moq;

namespace Helper.Runtime.Tests;

public partial class RetrievalPipelineTests
{
    [Fact]
    public void RerankingService_PrefersAnatomyOverComputerScienceForIntestinalWallQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "cs-intestinal-wall",
                "Слои архитектуры программ и границы модулей в больших приложениях.",
                "doc-cs",
                "Clean Architecture.pdf",
                0.9,
                routingScore: 0.5,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2"),
            CreateChunk(
                "anat-intestinal-wall",
                "Стенку тонкой кишки образуют слизистая оболочка, подслизистая основа, мышечная и серозная оболочки.",
                "doc-anat",
                "Gray's Anatomy.pdf",
                0.77,
                routingScore: 3.6,
                domain: "anatomy",
                collection: "knowledge_anatomy_v2")
        };

        var results = service.Rerank("Какие слои образуют стенку тонкой кишки?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("anatomy", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersNeuroOverPhysicsForActionPotentialQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "physics-action-potential",
                "Физический потенциал, уравнения поля и динамика системы.",
                "doc-phys",
                "Теоретическая_физика_2_Теория_поля_Ландау_Лифшиц.pdf",
                0.9,
                routingScore: 0.4,
                domain: "physics",
                collection: "knowledge_physics_v2"),
            CreateChunk(
                "neuro-action-potential",
                "Потенциал действия нейрона возникает из-за изменения проницаемости мембраны для ионов натрия и калия.",
                "doc-neuro",
                "Principles of Neural Science.pdf",
                0.77,
                routingScore: 3.6,
                domain: "neuro",
                collection: "knowledge_neuro_v2")
        };

        var results = service.Rerank("Как работает потенциал действия нейрона?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("neuro", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersNeuroOverPhysicsForBasalGangliaQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "physics-ganglia",
                "Физические взаимодействия частиц и симметрии в динамических системах.",
                "doc-phys",
                "Теоретическая_физика_3_Квантовая_механика_Ландау_Лифшиц.pdf",
                0.89,
                routingScore: 0.4,
                domain: "physics",
                collection: "knowledge_physics_v2"),
            CreateChunk(
                "neuro-ganglia",
                "Базальные ганглии участвуют в выборе и инициации движений через петли коры, таламуса и стриатума.",
                "doc-neuro",
                "Principles of Neural Science.pdf",
                0.76,
                routingScore: 3.5,
                domain: "neuro",
                collection: "knowledge_neuro_v2")
        };

        var results = service.Rerank("Как базальные ганглии участвуют в движении?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("neuro", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersEnglishLiteratureOverEncyclopediasForShakespeareQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "encyc-shakespeare",
                "Краткая энциклопедическая справка о драматурге и эпохе.",
                "doc-encyc",
                "Прохоров А.М. СЭС 1988.pdf",
                0.89,
                routingScore: 0.5,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2"),
            CreateChunk(
                "englit-shakespeare",
                "Комедии и трагедии Шекспира различаются по композиции, конфликту и развязке.",
                "doc-englit",
                "The Complete Tragedies and Comedies of William Shakespeare.pdf",
                0.76,
                routingScore: 3.4,
                domain: "english_lang_lit",
                collection: "knowledge_english_lang_lit_v2")
        };

        var results = service.Rerank("Что отличает комедии и трагедии Шекспира?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("english_lang_lit", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersHistoricalEncyclopediasOverHistoryForAngolaContextQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "history-angola",
                "История Анголы в колониальный и постколониальный периоды.",
                "doc-history",
                "Всемирная история. Энциклопедия. Т.2. - 2006.pdf",
                0.88,
                routingScore: 0.5,
                domain: "history",
                collection: "knowledge_history_v2"),
            CreateChunk(
                "archive-angola",
                "Ангола в историко-энциклопедическом контексте: география, колонизация и ключевые вехи.",
                "doc-archive",
                "Большая Советская Энциклопедия.pdf",
                0.76,
                routingScore: 3.4,
                domain: "historical_encyclopedias",
                collection: "knowledge_historical_encyclopedias_v2")
        };

        var results = service.Rerank("Что известно об Анголе в историко-энциклопедическом контексте?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("historical_encyclopedias", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersEncyclopediasOverPhysicsForDopamineDefinitionQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "physics-dopamine",
                "Физические свойства молекул и энергетические переходы в системе.",
                "doc-phys",
                "Теоретическая_физика_5_Статистическая_физика_Ландау_Лифшиц.pdf",
                0.88,
                routingScore: 0.4,
                domain: "physics",
                collection: "knowledge_physics_v2"),
            CreateChunk(
                "encyc-dopamine",
                "Дофамин: краткое энциклопедическое определение нейромедиатора и его функций.",
                "doc-encyc",
                "A Dictionary of Psychology.epub",
                0.76,
                routingScore: 3.3,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2")
        };

        var results = service.Rerank("Что такое дофамин в кратком энциклопедическом определении?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("encyclopedias", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersLinguisticsOverMathForAnalyticVsSyntheticLanguagesQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "math-analytic-synthetic",
                "Аналитические методы и синтетические конструкции в математике.",
                "doc-math",
                "Mathematical Analysis.pdf",
                0.89,
                routingScore: 0.5,
                domain: "math",
                collection: "knowledge_math_v2"),
            CreateChunk(
                "ling-analytic-synthetic",
                "Аналитические языки выражают грамматические отношения отдельными словами, а синтетические - формами слов.",
                "doc-ling",
                "The World Atlas of Language Structures.pdf",
                0.77,
                routingScore: 3.5,
                domain: "linguistics",
                collection: "knowledge_linguistics_v2")
        };

        var results = service.Rerank("Чем аналитические языки отличаются от синтетических?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("linguistics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersMathOverComputerScienceForEigenvaluesQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "cs-eigen",
                "Собственные значения могут использоваться в алгоритмах машинного обучения и анализа данных.",
                "doc-cs",
                "Hands-On Machine Learning.pdf",
                0.89,
                routingScore: 0.5,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2"),
            CreateChunk(
                "math-eigen",
                "Собственные значения матрицы определяются из характеристического многочлена линейного оператора.",
                "doc-math",
                "Linear Algebra.pdf",
                0.77,
                routingScore: 3.4,
                domain: "math",
                collection: "knowledge_math_v2")
        };

        var results = service.Rerank("Что такое собственные значения матрицы?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("math", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersPsychologyOverComputerScienceForIndividuationQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "cs-individuation",
                "Индивидуализация рекомендаций в адаптивных цифровых системах.",
                "doc-cs",
                "Recommender Systems.pdf",
                0.89,
                routingScore: 0.5,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2"),
            CreateChunk(
                "psych-individuation",
                "Индивидуация у Юнга описывает процесс становления целостной личности.",
                "doc-psych",
                "Psychological Types.epub",
                0.77,
                routingScore: 3.4,
                domain: "psychology",
                collection: "knowledge_psychology_v2")
        };

        var results = service.Rerank("Что такое индивидуация?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("psychology", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersPsychologyOverEncyclopediasForIntroversionQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "encyc-introversion",
                "Краткая словарная заметка о личностных качествах.",
                "doc-encyc",
                "A Dictionary of Psychology.epub",
                0.89,
                routingScore: 0.5,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2"),
            CreateChunk(
                "psych-introversion",
                "Интроверсия и экстраверсия описывают разные способы направленности психической энергии у Юнга.",
                "doc-psych",
                "Psychological Types.epub",
                0.77,
                routingScore: 3.4,
                domain: "psychology",
                collection: "knowledge_psychology_v2")
        };

        var results = service.Rerank("Чем отличаются интроверсия и экстраверсия?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("psychology", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersBiologyOverNeuroForSynapseSignalQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "neuro-synapse",
                "Синаптическая передача в нейронных цепях и пластичность синапсов.",
                "doc-neuro",
                "Principles of Neural Science.pdf",
                0.89,
                routingScore: 0.7,
                domain: "neuro",
                collection: "knowledge_neuro_v2"),
            CreateChunk(
                "bio-synapse",
                "Сигнал через синапс передается химическими медиаторами, выделяющимися в синаптическую щель.",
                "doc-bio",
                "Campbell Biology.pdf",
                0.77,
                routingScore: 3.4,
                domain: "biology",
                collection: "knowledge_biology_v2")
        };

        var results = service.Rerank("Как передается сигнал через синапс?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("biology", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersChemistryOverEncyclopediasForOrganicAcidityQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "encyc-acidity",
                "Краткая энциклопедическая заметка о кислотности как общем термине.",
                "doc-encyc",
                "Прохоров А.М. СЭС 1988.pdf",
                0.9,
                routingScore: 0.6,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2"),
            CreateChunk(
                "chem-acidity",
                "Кислотность и основность органических соединений определяются распределением электронной плотности и устойчивостью сопряженных форм.",
                "doc-chem",
                "Organic Chemistry.pdf",
                0.77,
                routingScore: 3.5,
                domain: "chemistry",
                collection: "knowledge_chemistry_v2")
        };

        var results = service.Rerank("Что определяет кислотность и основность органических соединений?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("chemistry", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersChemistryOverPhysicsForPhysicalChemistrySecondLawQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "physics-second-law",
                "Второй закон термодинамики в физике формулируется через рост энтропии изолированной системы.",
                "doc-phys",
                "Теоретическая_физика_5_Статистическая_физика_Ландау_Лифшиц.pdf",
                0.9,
                routingScore: 0.6,
                domain: "physics",
                collection: "knowledge_physics_v2"),
            CreateChunk(
                "chem-second-law",
                "В физической химии второй закон термодинамики связывает направление процессов с ростом энтропии и свободной энергией.",
                "doc-chem",
                "Physical Chemistry.pdf",
                0.77,
                routingScore: 3.4,
                domain: "chemistry",
                collection: "knowledge_chemistry_v2")
        };

        var results = service.Rerank("Как формулируется второй закон термодинамики в физической химии?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("chemistry", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersHistoryOverAnalysisStrategyForFirstWorldWarQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "analysis-ww1",
                "Стратегия войны, коалиции и непрямые действия в конфликтах.",
                "doc-analysis",
                "О ВОЙНЕ.pdf",
                0.9,
                routingScore: 0.7,
                domain: "analysis_strategy",
                collection: "knowledge_analysis_strategy_v2"),
            CreateChunk(
                "history-ww1",
                "Причины Первой мировой войны включают систему союзов, национализм и июльский кризис.",
                "doc-history",
                "World History Encyclopedia.pdf",
                0.77,
                routingScore: 3.4,
                domain: "history",
                collection: "knowledge_history_v2")
        };

        var results = service.Rerank("Какие причины привели к Первой мировой войне?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("history", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersHistoryOverPhilosophyForAncientEastQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "philosophy-east",
                "Философские представления о происхождении государства и общества.",
                "doc-philosophy",
                "History of Western Philosophy.epub",
                0.9,
                routingScore: 0.7,
                domain: "philosophy",
                collection: "knowledge_philosophy_v2"),
            CreateChunk(
                "history-east",
                "Первые государства Древнего Востока сформировались в долинах рек на основе ирригации и централизации власти.",
                "doc-history",
                "World History Encyclopedia.pdf",
                0.77,
                routingScore: 3.4,
                domain: "history",
                collection: "knowledge_history_v2")
        };

        var results = service.Rerank("Как возникли первые государства Древнего Востока?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("history", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersLinguisticsOverPhilosophyForGrammaticalNumberQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "philosophy-number",
                "Философские категории числа и множественности.",
                "doc-philosophy",
                "History of Western Philosophy.epub",
                0.89,
                routingScore: 0.7,
                domain: "philosophy",
                collection: "knowledge_philosophy_v2"),
            CreateChunk(
                "ling-number",
                "Грамматическое число в языках мира выражается морфологически, синтаксически или аналитически.",
                "doc-ling",
                "The World Atlas of Language Structures.pdf",
                0.77,
                routingScore: 3.5,
                domain: "linguistics",
                collection: "knowledge_linguistics_v2")
        };

        var results = service.Rerank("Как выражается грамматическое число в языках мира?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("linguistics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersPhilosophyOverChemistryForSocratesMethodQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "chem-method",
                "Методы химического анализа и лабораторные процедуры.",
                "doc-chem",
                "General Chemistry.pdf",
                0.89,
                routingScore: 0.7,
                domain: "chemistry",
                collection: "knowledge_chemistry_v2"),
            CreateChunk(
                "philosophy-socrates",
                "Метод Сократа строится на диалоге, вопросах и выявлении противоречий в убеждениях собеседника.",
                "doc-philosophy",
                "The History of Western Philosophy.epub",
                0.77,
                routingScore: 3.4,
                domain: "philosophy",
                collection: "knowledge_philosophy_v2")
        };

        var results = service.Rerank("В чем состоит метод Сократа?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("philosophy", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersPhysicsOverMathForMaxwellQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "math-maxwell",
                "Системы уравнений и методы математического анализа.",
                "doc-math",
                "Mathematical Analysis.pdf",
                0.89,
                routingScore: 0.7,
                domain: "math",
                collection: "knowledge_math_v2"),
            CreateChunk(
                "physics-maxwell",
                "Уравнения Максвелла связывают электрическое и магнитное поля с зарядами и токами.",
                "doc-physics",
                "Теоретическая_физика_2_Теория_поля_Ландау_Лифшиц.pdf",
                0.77,
                routingScore: 3.4,
                domain: "physics",
                collection: "knowledge_physics_v2")
        };

        var results = service.Rerank("Как записываются уравнения Максвелла?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("physics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersPhysicsOverMathForMaxwellQuestion_WhenMathAlsoMatchesEquations()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "math-maxwell-strong",
                "Уравнения и методы математического анализа для систем дифференциальных уравнений.",
                "doc-math",
                "Mathematical Analysis.pdf",
                0.91,
                routingScore: 1.2,
                domain: "math",
                collection: "knowledge_math_v2"),
            CreateChunk(
                "physics-maxwell-strong",
                "Уравнения Максвелла описывают динамику электрического и магнитного полей.",
                "doc-physics",
                "Теоретическая_физика_2_Теория_поля_Ландау_Лифшиц.pdf",
                0.77,
                routingScore: 3.4,
                domain: "physics",
                collection: "knowledge_physics_v2")
        };

        var results = service.Rerank("Как записываются уравнения Максвелла?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("physics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersRoboticsOverComputerScienceForDenavitHartenbergQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "cs-dh",
                "Структуры данных и параметры конфигурации программных систем.",
                "doc-cs",
                "Clean Architecture.pdf",
                0.89,
                routingScore: 0.7,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2"),
            CreateChunk(
                "robotics-dh",
                "Параметры Денавита-Хартенберга описывают геометрию звеньев и сочленений манипулятора.",
                "doc-robotics",
                "Introduction to Robotics.pdf",
                0.77,
                routingScore: 3.4,
                domain: "robotics",
                collection: "knowledge_robotics_v2")
        };

        var results = service.Rerank("Как записываются параметры Денавита-Хартенберга?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("robotics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersRussianLiteratureOverPhilosophyForWarAndPeaceQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "philosophy-war-peace",
                "Философские размышления о войне, мире и морали.",
                "doc-philosophy",
                "History of Western Philosophy.epub",
                0.89,
                routingScore: 0.7,
                domain: "philosophy",
                collection: "knowledge_philosophy_v2"),
            CreateChunk(
                "russian-war-peace",
                "У Толстого тема войны и мира раскрывается через судьбы героев и историческую панораму общества.",
                "doc-russian",
                "War and Peace.epub",
                0.77,
                routingScore: 3.4,
                domain: "russian_lang_lit",
                collection: "knowledge_russian_lang_lit_v2")
        };

        var results = service.Rerank("Как раскрывается тема войны и мира у Толстого?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("russian_lang_lit", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersSciFiOverNeuroForFutureColonizationQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "neuro-future",
                "Мозг прогнозирует будущее состояние среды и формирует ожидания.",
                "doc-neuro",
                "Principles of Neural Science.pdf",
                0.89,
                routingScore: 0.7,
                domain: "neuro",
                collection: "knowledge_neuro_v2"),
            CreateChunk(
                "scifi-future",
                "Тема колонизации будущего в science fiction показывает экспансию человечества и устройство новых миров.",
                "doc-scifi",
                "Science Fiction Concepts.epub",
                0.77,
                routingScore: 3.5,
                domain: "sci_fi_concepts",
                collection: "knowledge_sci_fi_concepts_v2")
        };

        var results = service.Rerank("Как тема колонизации будущего раскрывается в science fiction?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("sci_fi_concepts", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersEntomologyOverBiologyForInsectWingQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "bio-wings",
                "Эволюция структур и органов у животных рассматривается в общей биологии.",
                "doc-bio",
                "Campbell Biology.pdf",
                0.89,
                routingScore: 0.7,
                domain: "biology",
                collection: "knowledge_biology_v2"),
            CreateChunk(
                "ento-wings",
                "Эволюция крыльев у насекомых связана с преобразованием покровов тела и ранним полетом.",
                "doc-ento",
                "Entomology.pdf",
                0.77,
                routingScore: 3.4,
                domain: "entomology",
                collection: "knowledge_entomology_v2")
        };

        var results = service.Rerank("Как эволюционировали крылья у насекомых?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("entomology", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersEntomologyOverHistoryForAntCastesQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "history-castes",
                "Исторические кастовые системы и социальные страты общества.",
                "doc-history",
                "World History Encyclopedia.pdf",
                0.89,
                routingScore: 0.7,
                domain: "history",
                collection: "knowledge_history_v2"),
            CreateChunk(
                "ento-castes",
                "Касты и коммуникация у муравьев обеспечивают разделение труда и координацию колонии.",
                "doc-ento",
                "Entomology.pdf",
                0.77,
                routingScore: 3.4,
                domain: "entomology",
                collection: "knowledge_entomology_v2")
        };

        var results = service.Rerank("Что известно о кастах и коммуникации у муравьев?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("entomology", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersHistoricalArchiveOverEncyclopediasForIbsenQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "encyc-ibsen",
                "Краткая словарная справка об Ибсене.",
                "doc-encyc",
                "Прохоров А.М. СЭС 1988.pdf",
                0.89,
                routingScore: 0.7,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2"),
            CreateChunk(
                "archive-ibsen",
                "Ибсен в историко-энциклопедическом контексте: драматург, эпоха и культурное влияние.",
                "doc-archive",
                "Большая Советская Энциклопедия.pdf",
                0.77,
                routingScore: 3.4,
                domain: "historical_encyclopedias",
                collection: "knowledge_historical_encyclopedias_v2")
        };

        var results = service.Rerank("Кто такой Ибсен?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("historical_encyclopedias", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersAnalysisStrategyOverEconomicsForFastSlowThinkingQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "econ-fast-slow",
                "Экономическое поведение и когнитивные искажения на рынках.",
                "doc-econ",
                "Principles of Economics.pdf",
                0.89,
                routingScore: 0.7,
                domain: "economics",
                collection: "knowledge_economics_v2"),
            CreateChunk(
                "analysis-fast-slow",
                "Эвристика быстрого и медленного мышления описывает два режима принятия решений.",
                "doc-analysis",
                "Thinking, Fast and Slow.pdf",
                0.77,
                routingScore: 3.4,
                domain: "analysis_strategy",
                collection: "knowledge_analysis_strategy_v2")
        };

        var results = service.Rerank("Как работает эвристика быстрого и медленного мышления?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("analysis_strategy", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersMedicineOverComputerScienceForTypeTwoDiabetesQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "cs-type-two",
                "Системы второго типа и типы данных в программировании.",
                "doc-cs",
                "Code Complete.pdf",
                0.89,
                routingScore: 0.7,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2"),
            CreateChunk(
                "med-diabetes",
                "Диабет второго типа связан с инсулинорезистентностью и нарушением регуляции уровня глюкозы.",
                "doc-med",
                "The Merck Manual Home Health Handbook",
                0.77,
                routingScore: 3.4,
                domain: "medicine",
                collection: "knowledge_medicine_v2")
        };

        var results = service.Rerank("Что такое диабет второго типа?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("medicine", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersSocialSciencesOverHistoryForPrincipledNegotiationsQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "history-negotiations",
                "История международных переговоров и дипломатии.",
                "doc-history",
                "World History Encyclopedia.pdf",
                0.89,
                routingScore: 0.7,
                domain: "history",
                collection: "knowledge_history_v2"),
            CreateChunk(
                "social-negotiations",
                "Принципиальные переговоры строятся на интересах сторон, критериях и отделении людей от проблемы.",
                "doc-social",
                "Getting to Yes.pdf",
                0.77,
                routingScore: 3.4,
                domain: "social_sciences",
                collection: "knowledge_social_sciences_v2")
        };

        var results = service.Rerank("Как вести принципиальные переговоры?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("social_sciences", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersRoboticsOverEncyclopediasForCyberneticsQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "encyc-cybernetics",
                "Краткая словарная заметка о кибернетике.",
                "doc-encyc",
                "Прохоров А.М. СЭС 1988.pdf",
                0.89,
                routingScore: 0.7,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2"),
            CreateChunk(
                "robotics-cybernetics",
                "Кибернетика изучает управление, обратную связь и поведение сложных систем и машин.",
                "doc-robotics",
                "Introduction to Robotics.pdf",
                0.77,
                routingScore: 3.4,
                domain: "robotics",
                collection: "knowledge_robotics_v2")
        };

        var results = service.Rerank("Что изучает кибернетика?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("robotics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_AnnotatesTopicalFitAndPenalizesGenericEncyclopediaForNarrowRoboticsQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "encyc-dh",
                "Краткая энциклопедическая заметка о параметрах и системах.",
                "doc-encyc",
                "СЭС.pdf",
                0.92,
                routingScore: 0.6,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2"),
            CreateChunk(
                "robotics-dh-topical",
                "Параметры Денавита-Хартенберга описывают геометрию звеньев, оси сочленений и преобразования манипулятора.",
                "doc-robotics",
                "Параметры Денавита-Хартенберга и кинематика роботов.pdf",
                0.77,
                routingScore: 3.4,
                domain: "robotics",
                collection: "knowledge_robotics_v2")
        };

        var results = service.Rerank(
            "Как записываются параметры Денавита-Хартенберга?",
            candidates,
            limit: 2,
            options: new RetrievalRequestOptions(
                Purpose: RetrievalPurpose.FactualLookup,
                PreferTraceableChunks: true));

        Assert.Equal(2, results.Count);
        Assert.Equal("robotics", results[0].Metadata["domain"]);
        Assert.NotEqual("low", results[0].Metadata["topical_fit_label"]);
        Assert.Equal("low", results[1].Metadata["topical_fit_label"]);
        Assert.Equal("FactualLookup", results[0].Metadata["retrieval_purpose"]);
        Assert.Equal("true", results[1].Metadata["topical_fit_generic_domain"]);
    }

    [Fact]
    public void RerankingService_SourceDiversityGuard_PullsInAlternativeSource_WhenTopResultsAreOverConcentrated()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "robotics-a",
                "Параметры Денавита-Хартенберга задают геометрию первого звена манипулятора.",
                "doc-robotics-a",
                "Robotics Handbook A.pdf",
                0.93,
                routingScore: 3.9,
                domain: "robotics",
                sourcePath: "https://docs.example.org/robotics/denavit-hartenberg"),
            CreateChunk(
                "robotics-b",
                "Матрицы преобразований в нотации Денавита-Хартенберга связывают соседние звенья.",
                "doc-robotics-b",
                "Robotics Handbook B.pdf",
                0.91,
                routingScore: 3.7,
                domain: "robotics",
                sourcePath: "https://docs.example.org/robotics/denavit-hartenberg"),
            CreateChunk(
                "robotics-c",
                "Для каждого сустава задаются alpha, a, d и theta.",
                "doc-robotics-c",
                "Robotics Handbook C.pdf",
                0.89,
                routingScore: 3.6,
                domain: "robotics",
                sourcePath: "https://docs.example.org/robotics/denavit-hartenberg"),
            CreateChunk(
                "robotics-alt",
                "Обзор кинематики манипуляторов и систем координат для сравнения разных нотаций.",
                "doc-robotics-alt",
                "Manipulator Frames.pdf",
                0.83,
                routingScore: 3.2,
                domain: "robotics",
                sourcePath: "https://alt.example.org/robotics/reference-frames",
                collection: "knowledge_robotics_alt_v2")
        };

        var results = service.Rerank(
            "Как записываются параметры Денавита-Хартенберга?",
            candidates,
            limit: 3,
            options: new RetrievalRequestOptions(
                Purpose: RetrievalPurpose.FactualLookup,
                PreferTraceableChunks: true));

        Assert.Equal(3, results.Count);
        Assert.True(results.Select(chunk => chunk.Metadata["source_diversity_source_key"]).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2);
        Assert.All(results, chunk => Assert.True(double.Parse(chunk.Metadata["source_diversity_dominance"], System.Globalization.CultureInfo.InvariantCulture) <= 0.67d));
        Assert.Contains(results, chunk => string.Equals(chunk.Collection, "knowledge_robotics_alt_v2", StringComparison.OrdinalIgnoreCase));
    }
}
