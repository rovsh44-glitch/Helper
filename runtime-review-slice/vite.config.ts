import path from 'node:path';
import { fileURLToPath } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

const sliceRoot = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(sliceRoot, 'web');
const outDir = path.resolve(sliceRoot, 'dist');

export default defineConfig({
  root: webRoot,
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
      '@slice': path.resolve(webRoot, 'src'),
    },
  },
});
