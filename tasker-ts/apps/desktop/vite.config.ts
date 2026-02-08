import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';
import electron from 'vite-plugin-electron/simple';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  plugins: [
    tailwindcss(),
    react(),
    electron({
      main: {
        entry: 'electron/main.ts',
        vite: {
          build: {
            rollupOptions: {
              external: ['better-sqlite3', /\.node$/],
            },
          },
          resolve: {
            alias: {
              '@': path.resolve(__dirname, './src'),
              '@electron': path.resolve(__dirname, './electron'),
              '@utils': path.resolve(__dirname, './utils'),
            },
          },
        },
      },
      preload: {
        input: path.join(__dirname, 'electron/preload.ts'),
        vite: {
          build: {
            rollupOptions: {
              external: ['better-sqlite3', /\.node$/],
            },
          },
          resolve: {
            alias: {
              '@': path.resolve(__dirname, './src'),
              '@electron': path.resolve(__dirname, './electron'),
              '@utils': path.resolve(__dirname, './utils'),
            },
          },
        },
      },
      renderer: process.env.NODE_ENV === 'test' ? undefined : {},
    }),
  ],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
      '@electron': path.resolve(__dirname, './electron'),
      '@utils': path.resolve(__dirname, './utils'),
    },
  },
});
