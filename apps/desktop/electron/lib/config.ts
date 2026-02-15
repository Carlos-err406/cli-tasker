import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export function getPreloadPath(): string {
  return path.join(__dirname, 'preload.mjs');
}

export function getPublicPath(): string {
  return (
    process.env['VITE_PUBLIC'] ||
    path.join(__dirname, '..', '..', 'public')
  );
}
