/// <reference types="vitest/config" />
import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  return {
    plugins: [react()],
    resolve: {
      alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
    },
    build: {
      chunkSizeWarningLimit: 700, // Fluent UI v9 alone is ~510 kB minified (154 kB gzip)
      rollupOptions: {
        output: {
          manualChunks: {
            react: ['react', 'react-dom', 'react-router-dom'],
            fluent: ['@fluentui/react-components'],
            msal: ['@azure/msal-browser', '@azure/msal-react'],
          },
        },
      },
    },
    server: {
      port: 5173,
      // In dev the SPA calls /api on its own origin; Vite forwards to the backend.
      proxy: {
        '/api': { target: env.VITE_DEV_API_PROXY ?? 'http://localhost:5080', changeOrigin: true },
      },
    },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: ['./src/test/setup.ts'],
      css: false,
    },
  };
});
