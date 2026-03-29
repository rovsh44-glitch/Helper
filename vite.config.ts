import { defineConfig } from 'vite';
import { createHelperViteConfig } from './vite.shared.config.mjs';

export default defineConfig(({ mode }) => {
    return createHelperViteConfig(mode) as any;
});
