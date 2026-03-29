export type RequestTimeoutProfile =
  | 'quick'
  | 'standard'
  | 'startup'
  | 'session'
  | 'stream_connect'
  | 'stream_read';

const PROFILE_TIMEOUTS_MS: Record<RequestTimeoutProfile, number> = {
  quick: 5_000,
  standard: 12_000,
  startup: 8_000,
  session: 8_000,
  stream_connect: 15_000,
  stream_read: 20_000,
};

function resolveTimeoutMs(profile: RequestTimeoutProfile, timeoutMs?: number): number {
  if (typeof timeoutMs === 'number' && Number.isFinite(timeoutMs) && timeoutMs > 0) {
    return timeoutMs;
  }

  return PROFILE_TIMEOUTS_MS[profile];
}

function timeoutErrorLabel(label: string, timeoutMs: number): Error {
  return new Error(`${label} timed out after ${timeoutMs}ms`);
}

function createLinkedAbortController(signal: AbortSignal | null | undefined): AbortController {
  const controller = new AbortController();

  if (!signal) {
    return controller;
  }

  if (signal.aborted) {
    controller.abort(signal.reason);
    return controller;
  }

  const abortForwarder = () => controller.abort(signal.reason);
  signal.addEventListener('abort', abortForwarder, { once: true });
  controller.signal.addEventListener('abort', () => {
    signal.removeEventListener('abort', abortForwarder);
  }, { once: true });

  return controller;
}

export async function fetchWithTimeout(
  input: RequestInfo | URL,
  init: RequestInit = {},
  options: {
    timeoutMs?: number;
    profile?: RequestTimeoutProfile;
    label?: string;
  } = {},
): Promise<Response> {
  const profile = options.profile ?? 'standard';
  const timeoutMs = resolveTimeoutMs(profile, options.timeoutMs);
  const controller = createLinkedAbortController(init.signal);
  const timer = window.setTimeout(() => controller.abort(timeoutErrorLabel(options.label ?? 'Request', timeoutMs)), timeoutMs);

  try {
    return await fetch(input, {
      ...init,
      signal: controller.signal,
    });
  } finally {
    window.clearTimeout(timer);
  }
}

export function normalizeNetworkError(error: unknown, fallback: string): Error {
  if (error instanceof Error) {
    return error;
  }

  return new Error(fallback);
}
