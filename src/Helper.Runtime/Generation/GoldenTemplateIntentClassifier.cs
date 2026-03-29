namespace Helper.Runtime.Generation;

public static class GoldenTemplateIntentClassifier
{
    public const string EngineeringCalculatorTemplateId = "Template_EngineeringCalculator";
    public const string ChessTemplateId = "Golden_Chess_v2";
    public const string PdfEpubTemplateId = "Template_PdfEpubConverter";

    // New 17 templates
    public const string EnterpriseCRMLite = "Template_EnterpriseCRMLite";
    public const string SystemHealthMonitor = "Template_SystemHealthMonitor";
    public const string SecureVault = "Template_SecureVault";
    public const string IdePluginStarter = "Template_IdePluginStarter";
    public const string AiAnalyticsDashboard = "Template_AiAnalyticsDashboard";
    public const string DistributedTaskScheduler = "Template_DistributedTaskScheduler";
    public const string MicroservicesGateway = "Template_MicroservicesGateway";
    public const string EcommerceStorefront = "Template_EcommerceStorefront";
    public const string AiCodeReviewer = "Template_AiCodeReviewer";
    public const string VectorSearchEngine = "Template_VectorSearchEngine";
    public const string PersonalKnowledgeWiki = "Template_PersonalKnowledgeWiki";
    public const string VoiceIntentAssistant = "Template_VoiceIntentAssistant";
    public const string AdvancedWebScraper = "Template_AdvancedWebScraper";
    public const string ImageProcessingSuite = "Template_ImageProcessingSuite";
    public const string FinancialPortfolioTracker = "Template_FinancialPortfolioTracker";
    public const string LogAggregator = "Template_LogAggregator";
    public const string MultiProtocolBridge = "Template_MultiProtocolBridge";
    private static readonly HashSet<string> GoldenTemplateIds = new(StringComparer.OrdinalIgnoreCase)
    {
        EngineeringCalculatorTemplateId,
        ChessTemplateId,
        PdfEpubTemplateId,
        EnterpriseCRMLite,
        SystemHealthMonitor,
        SecureVault,
        IdePluginStarter,
        AiAnalyticsDashboard,
        DistributedTaskScheduler,
        MicroservicesGateway,
        EcommerceStorefront,
        AiCodeReviewer,
        VectorSearchEngine,
        PersonalKnowledgeWiki,
        VoiceIntentAssistant,
        AdvancedWebScraper,
        ImageProcessingSuite,
        FinancialPortfolioTracker,
        LogAggregator,
        MultiProtocolBridge
    };

    public static bool HasExplicitGoldenTemplateRequest(string? text)
        => ResolveExplicitTemplateId(text) is not null;

    public static bool IsGoldenTemplateId(string? templateId)
    {
        return !string.IsNullOrWhiteSpace(templateId) &&
               GoldenTemplateIds.Contains(templateId.Trim());
    }

    public static string? ResolveExplicitTemplateId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lower = text.ToLowerInvariant();

        if (ContainsAny(lower, "engineering calculator", "инженерный калькулятор")) return EngineeringCalculatorTemplateId;
        if (ContainsAny(lower, "chess", "шахматы", "шахмат")) return ChessTemplateId;
        if (lower.Contains("pdf", StringComparison.Ordinal) &&
            lower.Contains("epub", StringComparison.Ordinal) &&
            ContainsAny(lower, "convert", "converter", "конверт"))
        {
            return PdfEpubTemplateId;
        }

        if (ContainsAny(lower, "crm", "client management")) return EnterpriseCRMLite;
        if (ContainsAny(lower, "health monitor", "performance monitor", "system health", "системный монитор")) return SystemHealthMonitor;
        if (ContainsAny(lower, "secure vault", "password manager", "паролей", "сейф")) return SecureVault;
        if (ContainsAny(lower, "ide plugin", "ide extension", "plugin starter", "плагин ide", "плагина", "расширения ide", "расширение ide")) return IdePluginStarter;
        if (ContainsAny(lower, "analytics dashboard", "ai-analytics", "ai analytics", "дашборд аналитики") ||
            ((lower.Contains("recharts", StringComparison.OrdinalIgnoreCase) || lower.Contains("график", StringComparison.OrdinalIgnoreCase)) &&
             lower.Contains("react", StringComparison.OrdinalIgnoreCase)) ||
            (lower.Contains("дашборд", StringComparison.OrdinalIgnoreCase) &&
             lower.Contains("react", StringComparison.OrdinalIgnoreCase)))
        {
            return AiAnalyticsDashboard;
        }
        if (ContainsAny(lower, "task scheduler", "job scheduler", "worker monitoring", "планировщик задач")) return DistributedTaskScheduler;
        if (ContainsAny(lower, "microservices gateway", "api gateway", "шлюз микросервисов")) return MicroservicesGateway;
        if (ContainsAny(lower, "ecommerce", "e-commerce", "storefront", "интернет-магазин")) return EcommerceStorefront;
        if (ContainsAny(lower, "code reviewer", "code review", "automated code review", "code audit", "ревью кода", "код-ревью")) return AiCodeReviewer;
        if (ContainsAny(lower, "vector search", "векторный поиск") ||
            (ContainsAny(lower, "search engine", "semantic search", "поисковый движок", "семантического поиска") &&
             ContainsAny(lower, "vector database", "векторной базы", "векторной базы данных", "embeddings")))
        {
            return VectorSearchEngine;
        }

        if (ContainsAny(lower, "knowledge wiki", "knowledge base", "personal knowledge", "база знаний", "базу знаний", "персональную базу знаний")) return PersonalKnowledgeWiki;
        if (ContainsAny(lower, "voice intent", "intent recognition", "voice assistant", "голосовой ассистент")) return VoiceIntentAssistant;
        if (ContainsAny(lower, "web scraper", "data scraping", "веб-скрапер")) return AdvancedWebScraper;
        if (ContainsAny(lower, "image processing", "opencv", "tesseract", "обработка изображений")) return ImageProcessingSuite;
        if (ContainsAny(lower, "portfolio tracker", "financial portfolio", "stock parsing", "портфельный трекер", "финансового портфеля", "трекер финансового портфеля")) return FinancialPortfolioTracker;
        if (ContainsAny(lower, "log aggregator", "log aggregation service", "log collection service", "audit service", "сбор логов", "сбора логов", "агрегац", "ротации и фильтрации")) return LogAggregator;
        if (ContainsAny(lower, "protocol bridge", "network bridge", "сетевой мост") ||
            (lower.Contains("mqtt", StringComparison.Ordinal) &&
             (lower.Contains("http", StringComparison.Ordinal) || lower.Contains("websocket", StringComparison.Ordinal))))
        {
            return MultiProtocolBridge;
        }

        return null;
    }

    private static bool ContainsAny(string source, params string[] markers)
    {
        foreach (var marker in markers)
        {
            if (source.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

