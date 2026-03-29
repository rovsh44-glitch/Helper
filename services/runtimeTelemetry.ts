import { ControlPlaneSnapshotDto, RouteTelemetryBucketDto, RouteTelemetryEventDto } from './generatedApiClient';

export type UiRuntimeScope =
  | 'boot'
  | 'control'
  | 'api'
  | 'model'
  | 'storage'
  | 'security'
  | 'bus'
  | 'network'
  | 'exception'
  | 'misc';

export type UiRuntimeDomain =
  | 'readiness'
  | 'gateway'
  | 'persistence'
  | 'auth'
  | 'generation'
  | 'telemetry'
  | 'transport'
  | 'runtime'
  | 'unknown';

export type RouteTelemetryOverview = {
  hasData: boolean;
  totalEvents: number;
  dominantChannel: string;
  dominantOperationKind: string;
  dominantRoute: string;
  dominantQuality: string;
  dominantModelRoute: string;
  degradedCount: number;
  failedCount: number;
  blockedCount: number;
  alerts: string[];
  recent: RouteTelemetryEventDto[];
  lastEvent: RouteTelemetryEventDto | null;
  lastDegradedEvent: RouteTelemetryEventDto | null;
};

const RUNTIME_SCOPES = new Set<UiRuntimeScope>([
  'boot',
  'control',
  'api',
  'model',
  'storage',
  'security',
  'bus',
  'network',
  'exception',
  'misc',
]);

const RUNTIME_DOMAINS = new Set<UiRuntimeDomain>([
  'readiness',
  'gateway',
  'persistence',
  'auth',
  'generation',
  'telemetry',
  'transport',
  'runtime',
  'unknown',
]);

export function normalizeRuntimeScope(scope?: string, fallback: UiRuntimeScope = 'misc'): UiRuntimeScope {
  const normalized = scope?.trim().toLowerCase();
  return normalized && RUNTIME_SCOPES.has(normalized as UiRuntimeScope)
    ? (normalized as UiRuntimeScope)
    : fallback;
}

export function normalizeRuntimeDomain(domain?: string, fallback: UiRuntimeDomain = 'unknown'): UiRuntimeDomain {
  const normalized = domain?.trim().toLowerCase();
  return normalized && RUNTIME_DOMAINS.has(normalized as UiRuntimeDomain)
    ? (normalized as UiRuntimeDomain)
    : fallback;
}

export function buildRouteTelemetryOverview(controlPlane?: ControlPlaneSnapshotDto | null): RouteTelemetryOverview {
  const telemetry = controlPlane?.routeTelemetry;
  const recent = telemetry?.recent ?? [];

  return {
    hasData: (telemetry?.totalEvents ?? 0) > 0,
    totalEvents: telemetry?.totalEvents ?? 0,
    dominantChannel: topBucketLabel(telemetry?.channels, 'No channel'),
    dominantOperationKind: topBucketLabel(telemetry?.operationKinds, 'No operation'),
    dominantRoute: topBucketLabel(telemetry?.routes, 'No route'),
    dominantQuality: topBucketLabel(telemetry?.qualities, 'No quality'),
    dominantModelRoute: topBucketLabel(telemetry?.modelRoutes, 'No model route'),
    degradedCount: bucketCount(telemetry?.qualities, 'degraded'),
    failedCount: bucketCount(telemetry?.qualities, 'failed'),
    blockedCount: bucketCount(telemetry?.qualities, 'blocked'),
    alerts: telemetry?.alerts ?? [],
    recent,
    lastEvent: recent[0] ?? null,
    lastDegradedEvent: recent.find(event => isDegradedQuality(event.quality)) ?? null,
  };
}

function bucketCount(buckets: RouteTelemetryBucketDto[] | undefined, key: string) {
  return buckets?.find(bucket => bucket.key.toLowerCase() === key.toLowerCase())?.count ?? 0;
}

function topBucketLabel(buckets: RouteTelemetryBucketDto[] | undefined, fallback: string) {
  const top = buckets?.[0]?.key?.trim();
  return top && top.length > 0 ? top : fallback;
}

function isDegradedQuality(quality?: string) {
  const normalized = quality?.trim().toLowerCase();
  return normalized === 'degraded' || normalized === 'failed' || normalized === 'blocked';
}
