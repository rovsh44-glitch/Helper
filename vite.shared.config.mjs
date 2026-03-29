import path from 'node:path';
import { fileURLToPath } from 'node:url';
import react from '@vitejs/plugin-react';
import { loadEnv } from 'vite';

export const repoRoot = path.dirname(fileURLToPath(import.meta.url));

function sanitizeSignalRPureAnnotations() {
  const signalRUtilsPath = '/node_modules/@microsoft/signalr/dist/esm/Utils.js';

  return {
    name: 'sanitize-signalr-pure-annotations',
    enforce: 'pre',
    transform(code, id) {
      const normalizedId = id.replace(/\\/g, '/');
      if (!normalizedId.endsWith(signalRUtilsPath)) {
        return null;
      }

      return {
        code: code.replace(/\/\*#__PURE__\*\/\s+(?=function\s+(?:getOsName|getRuntimeVersion)\s*\()/g, ''),
        map: null,
      };
    },
  };
}

function resolveVendorChunk(id) {
  const normalizedId = id.replace(/\\/g, '/');
  if (!normalizedId.includes('/node_modules/')) {
    return undefined;
  }

  if (
    normalizedId.includes('/node_modules/react/') ||
    normalizedId.includes('/node_modules/react-dom/') ||
    normalizedId.includes('/node_modules/scheduler/')
  ) {
    return 'react-vendor';
  }

  if (normalizedId.includes('/node_modules/@microsoft/signalr/')) {
    return 'signalr-vendor';
  }

  if (normalizedId.includes('/node_modules/lucide-react/')) {
    return 'icons-vendor';
  }

  return 'vendor';
}

export function createHelperViteConfig(mode = 'production') {
  loadEnv(mode, repoRoot, '');

  return {
    root: repoRoot,
    server: {
      port: 5173,
      host: '0.0.0.0',
      watch: {
        ignored: [
          '**/library/**',
          '**/ocr_venv/**',
          '**/PROJECTS/**',
          '**/PROJECTS /**',
          '**/logs/**',
          '**/LOG/**',
          '**/bin/**',
          '**/obj/**',
          '**/.vs/**',
        ],
      },
    },
    plugins: [sanitizeSignalRPureAnnotations(), react()],
    resolve: {
      alias: {
        '@': path.resolve(repoRoot, '.'),
      },
    },
    build: {
      rollupOptions: {
        output: {
          manualChunks(id) {
            return resolveVendorChunk(id);
          },
        },
      },
    },
  };
}
