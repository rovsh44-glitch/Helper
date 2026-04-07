import type { AppTabKey } from '../types';

export const APP_SHELL_NAVIGATION_EVENT = 'helper:app-shell-navigation';

export const APP_TAB_ROUTE_MAP: Record<AppTabKey, string> = {
  orchestrator: '/',
  runtime: '/runtime',
  strategy: '/strategy',
  objectives: '/objectives',
  planner: '/architecture',
  evolution: '/evolution',
  indexing: '/indexing',
  builder: '/builder',
  settings: '/settings',
};

const APP_ROUTE_TAB_MAP = Object.entries(APP_TAB_ROUTE_MAP).reduce<Record<string, AppTabKey>>((acc, [tab, path]) => {
  acc[normalizePath(path)] = tab as AppTabKey;
  return acc;
}, {});

export function getTabForPath(pathname: string): AppTabKey {
  const normalized = normalizePath(pathname);
  return APP_ROUTE_TAB_MAP[normalized] ?? 'orchestrator';
}

export function getPathForTab(tab: AppTabKey): string {
  return APP_TAB_ROUTE_MAP[tab];
}

export function readRouteQueryParam(key: string, locationLike: Pick<Location, 'search'> = window.location): string | null {
  const params = new URLSearchParams(locationLike.search);
  const value = params.get(key);
  return value && value.trim().length > 0 ? value : null;
}

export function navigateToTab(tab: AppTabKey, options?: { replace?: boolean; preserveQuery?: boolean }) {
  const target = new URL(window.location.href);
  target.pathname = getPathForTab(tab);
  if (!options?.preserveQuery) {
    target.search = '';
  }

  writeHistory(target, options?.replace ?? false);
}

export function updateRouteQueryParams(
  patch: Record<string, string | null | undefined>,
  options?: { replace?: boolean },
) {
  const target = new URL(window.location.href);
  for (const [key, value] of Object.entries(patch)) {
    if (value === null || value === undefined || value === '') {
      target.searchParams.delete(key);
      continue;
    }

    target.searchParams.set(key, value);
  }

  writeHistory(target, options?.replace ?? true);
}

function writeHistory(target: URL, replace: boolean) {
  const next = `${target.pathname}${target.search}${target.hash}`;
  const current = `${window.location.pathname}${window.location.search}${window.location.hash}`;
  if (next === current) {
    return;
  }

  if (replace) {
    window.history.replaceState({}, '', next);
    window.dispatchEvent(new CustomEvent(APP_SHELL_NAVIGATION_EVENT));
    return;
  }

  window.history.pushState({}, '', next);
  window.dispatchEvent(new CustomEvent(APP_SHELL_NAVIGATION_EVENT));
}

function normalizePath(pathname: string) {
  if (!pathname || pathname === '/') {
    return '/';
  }

  return pathname.replace(/\/+$/, '') || '/';
}
