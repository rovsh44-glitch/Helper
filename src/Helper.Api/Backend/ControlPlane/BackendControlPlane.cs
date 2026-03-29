using Helper.Api.Backend.Configuration;
using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Persistence;
using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Api.Backend.ControlPlane;

public sealed record BackendControlPlaneSnapshot(
    StartupReadinessSnapshot Readiness,
    BackendConfigValidationSnapshot Configuration,
    BackendRuntimePolicies Policies,
    ModelGatewaySnapshot ModelGateway,
    ConversationPersistenceQueueSnapshot PersistenceQueue,
    ConversationPersistenceHealthSnapshot Persistence,
    PostTurnAuditSnapshot AuditQueue,
    RouteTelemetrySnapshot RouteTelemetry,
    IReadOnlyList<string> Alerts);

public interface IBackendControlPlane
{
    BackendControlPlaneSnapshot GetSnapshot();
}

public sealed class BackendControlPlane : IBackendControlPlane
{
    private readonly IStartupReadinessService _readiness;
    private readonly IBackendConfigValidator _configValidator;
    private readonly IBackendOptionsCatalog _options;
    private readonly IModelGateway _modelGateway;
    private readonly IConversationWriteBehindQueue _persistenceQueue;
    private readonly IConversationPersistenceHealth _persistenceHealth;
    private readonly IPostTurnAuditQueue _auditQueue;
    private readonly IRouteTelemetryService _routeTelemetry;

    public BackendControlPlane(
        IStartupReadinessService readiness,
        IBackendConfigValidator configValidator,
        IBackendOptionsCatalog options,
        IModelGateway modelGateway,
        IConversationWriteBehindQueue persistenceQueue,
        IConversationPersistenceHealth persistenceHealth,
        IPostTurnAuditQueue auditQueue,
        IRouteTelemetryService routeTelemetry)
    {
        _readiness = readiness;
        _configValidator = configValidator;
        _options = options;
        _modelGateway = modelGateway;
        _persistenceQueue = persistenceQueue;
        _persistenceHealth = persistenceHealth;
        _auditQueue = auditQueue;
        _routeTelemetry = routeTelemetry;
    }

    public BackendControlPlaneSnapshot GetSnapshot()
    {
        var readiness = _readiness.GetSnapshot();
        var config = _configValidator.Validate();
        var modelGateway = _modelGateway.GetSnapshot();
        var persistenceQueue = _persistenceQueue.GetSnapshot();
        var persistence = _persistenceHealth.GetSnapshot(persistenceQueue.Pending);
        var audit = _auditQueue.GetSnapshot();
        var routeTelemetry = _routeTelemetry.GetSnapshot();

        var alerts = readiness.Alerts
            .Concat(config.Alerts)
            .Concat(modelGateway.Alerts)
            .Concat(persistenceQueue.Alerts)
            .Concat(persistence.Alerts)
            .Concat(audit.Alerts)
            .Concat(routeTelemetry.Alerts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new BackendControlPlaneSnapshot(
            readiness,
            config,
            _options.Policies,
            modelGateway,
            persistenceQueue,
            persistence,
            audit,
            routeTelemetry,
            alerts);
    }
}

