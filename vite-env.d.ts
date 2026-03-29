/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_HELPER_API_PROTOCOL?: string;
  readonly VITE_HELPER_API_HOST?: string;
  readonly VITE_HELPER_API_PORT?: string;
  readonly VITE_HELPER_API_BASE?: string;
  readonly VITE_HELPER_SESSION_SCOPES_CONVERSATION?: string;
  readonly VITE_HELPER_SESSION_SCOPES_RUNTIME_CONSOLE?: string;
  readonly VITE_HELPER_SESSION_SCOPES_BUILDER?: string;
  readonly VITE_HELPER_SESSION_SCOPES_EVOLUTION?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
