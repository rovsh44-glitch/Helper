import path from 'node:path';
import { fileURLToPath } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

const repoRoot = path.dirname(fileURLToPath(import.meta.url));
const sliceRoot = path.resolve(repoRoot, 'slice/runtime-review/web');
const outDir = path.resolve(repoRoot, 'slice/runtime-review/dist');

export default defineConfig({
  root: sliceRoot,
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 4174,
  },
  build: {
    outDir,
    emptyOutDir: true,
  },
  resolve: {
    alias: {
      '@slice': path.resolve(sliceRoot, 'src'),
    },
  },
});
