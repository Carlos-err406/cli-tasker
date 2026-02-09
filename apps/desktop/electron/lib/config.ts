import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { app } from 'electron';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

function getUserDataDir(): string {
  try {
    return app.getPath('userData');
  } catch {
    return path.join(
      process.env['HOME'] || process.env['APPDATA'] || '.',
      '.tasker-desktop',
    );
  }
}

function ensureUserDataDir(): string {
  const dir = getUserDataDir();
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  return dir;
}

export function getPreloadPath(): string {
  return path.join(__dirname, 'preload.mjs');
}

export function getPublicPath(): string {
  return (
    process.env['VITE_PUBLIC'] ||
    path.join(__dirname, '..', '..', 'public')
  );
}

export interface WindowPosition {
  x: number;
  y: number;
}

const POSITION_FILE = 'window-position.json';

export function getWindowPosition(): WindowPosition | null {
  try {
    const posPath = path.join(getUserDataDir(), POSITION_FILE);
    if (fs.existsSync(posPath)) {
      const data = JSON.parse(fs.readFileSync(posPath, 'utf8'));
      if (typeof data.x === 'number' && typeof data.y === 'number') {
        return { x: data.x, y: data.y };
      }
    }
  } catch {
    // ignore
  }
  return null;
}

export function saveWindowPosition(pos: WindowPosition): void {
  try {
    const dir = ensureUserDataDir();
    fs.writeFileSync(
      path.join(dir, POSITION_FILE),
      JSON.stringify(pos, null, 2),
    );
  } catch {
    // ignore
  }
}
