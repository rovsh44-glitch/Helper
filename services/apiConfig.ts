import { fetchWithTimeout } from './httpClient';

const DEFAULT_PROTOCOL = 'http';
const DEFAULT_HOST = 'localhost';
const DEFAULT_PORT = '5000';

const protocol = import.meta.env.VITE_HELPER_API_PROTOCOL || DEFAULT_PROTOCOL;
const host = import.meta.env.VITE_HELPER_API_HOST || DEFAULT_HOST;
const port = import.meta.env.VITE_HELPER_API_PORT || DEFAULT_PORT;

export type SessionSurface = 'conversation' | 'runtime_console' | 'builder' | 'evolution';

const DEFAULT_SURFACE: SessionSurface = 'conversation';

const DEFAULT_SURFACE_SCOPES: Record<SessionSurface, string[]> = {
  conversation: ['chat:read', 'chat:write', 'feedback:write'],
  runtime_console: ['metrics:read'],
  builder: ['chat:read', 'chat:write', 'tools:execute', 'build:run', 'fs:write'],
  evolution: ['evolution:control', 'metrics:read'],
};

type TokenCacheEntry = {
  accessToken: string | null;
  expiresAtMs: number;
  inflight: Promise<string> | null;
};

export const API_BASE = import.meta.env.VITE_HELPER_API_BASE || `${protocol}://${host}:${port}`;
export const API_ROOT = `${API_BASE}/api`;
export const HUB_URL = `${API_BASE}/hubs/helper`;

type SessionTokenResponse = {
  accessToken: string;
  expiresAtUtc: string;
  expiresInSeconds: number;
  tokenType: string;
  principalType: string;
  role: string;
  keyId: string;
  scopes: string[];
  surface: SessionSurface;
};

const SESSION_REFRESH_SKEW_MS = 30_000;

const surfaceTokenCache = new Map<SessionSurface, TokenCacheEntry>();

function getConfiguredSurfaceScopes(surface: SessionSurface): string[] {
  const envKey = `VITE_HELPER_SESSION_SCOPES_${surface.toUpperCase()}`;
  const configured = import.meta.env[envKey as keyof ImportMetaEnv] as string | undefined;
  const scopes = configured
    ?.split(',')
    .map(scope => scope.trim())
    .filter(scope => scope.length > 0);

  return scopes && scopes.length > 0
    ? scopes
    : DEFAULT_SURFACE_SCOPES[surface];
}

function getSurfaceCache(surface: SessionSurface): TokenCacheEntry {
  let cache = surfaceTokenCache.get(surface);
  if (!cache) {
    cache = { accessToken: null, expiresAtMs: 0, inflight: null };
    surfaceTokenCache.set(surface, cache);
  }

  return cache;
}

async function requestSessionToken(forceRefresh: boolean, surface: SessionSurface): Promise<string> {
  const requestBody: Record<string, unknown> = {
    surface,
    ttlMinutes: forceRefresh ? 20 : undefined,
    requestedScopes: getConfiguredSurfaceScopes(surface),
  };

  const response = await fetchWithTimeout(`${API_ROOT}/auth/session`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(requestBody),
  }, {
    profile: 'session',
    label: 'Session bootstrap'
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(`Session bootstrap failed (${response.status}): ${message || 'unknown error'}`);
  }

  const tokenPayload = await response.json() as SessionTokenResponse;
  const cache = getSurfaceCache(surface);
  cache.accessToken = tokenPayload.accessToken;
  const expiresAt = Date.parse(tokenPayload.expiresAtUtc);
  cache.expiresAtMs = Number.isFinite(expiresAt)
    ? expiresAt
    : (Date.now() + Math.max(5, tokenPayload.expiresInSeconds || 60) * 1000);
  return tokenPayload.accessToken;
}

export async function getAccessToken(forceRefresh = false, surface: SessionSurface = DEFAULT_SURFACE): Promise<string> {
  const cache = getSurfaceCache(surface);
  const now = Date.now();
  if (!forceRefresh && cache.accessToken && (cache.expiresAtMs - SESSION_REFRESH_SKEW_MS) > now) {
    return cache.accessToken;
  }

  if (cache.inflight) {
    return cache.inflight;
  }

  cache.inflight = requestSessionToken(forceRefresh, surface);
  try {
    return await cache.inflight;
  } finally {
    cache.inflight = null;
  }
}

export function clearAccessTokenCache(surface?: SessionSurface): void {
  if (surface) {
    const cache = getSurfaceCache(surface);
    cache.accessToken = null;
    cache.expiresAtMs = 0;
    cache.inflight = null;
    return;
  }

  surfaceTokenCache.forEach(cache => {
    cache.accessToken = null;
    cache.expiresAtMs = 0;
    cache.inflight = null;
  });
}

export async function withApiHeaders(
  headers: Record<string, string> = {},
  options: { forceRefresh?: boolean; surface?: SessionSurface } = {},
): Promise<Record<string, string>> {
  const token = await getAccessToken(options.forceRefresh ?? false, options.surface ?? DEFAULT_SURFACE);
  return {
    ...headers,
    Authorization: `Bearer ${token}`,
  };
}
