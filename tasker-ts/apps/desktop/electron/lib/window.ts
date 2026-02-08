import { BrowserWindow, screen } from 'electron';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { getPreloadPath } from './config.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const VITE_DEV_SERVER_URL = process.env['VITE_DEV_SERVER_URL'];

function getRendererDist(): string {
  const appRoot =
    process.env['APP_ROOT'] || path.join(__dirname, '..', '..');
  return path.join(appRoot, 'dist');
}

const POPUP_WIDTH = 400;
const POPUP_HEIGHT = 600;

export function createPopupWindow(
  trayBounds?: Electron.Rectangle,
): BrowserWindow {
  const position = calculatePopupPosition(trayBounds);

  const popup = new BrowserWindow({
    width: POPUP_WIDTH,
    height: POPUP_HEIGHT,
    x: position.x,
    y: position.y,
    frame: false,
    transparent: true,
    alwaysOnTop: true,
    skipTaskbar: true,
    resizable: false,
    minimizable: false,
    maximizable: false,
    show: false,
    webPreferences: {
      preload: getPreloadPath(),
      nodeIntegration: false,
      contextIsolation: true,
    },
  });

  if (VITE_DEV_SERVER_URL) {
    popup.loadURL(VITE_DEV_SERVER_URL);
  } else {
    popup.loadFile(path.join(getRendererDist(), 'index.html'));
  }

  return popup;
}

function calculatePopupPosition(trayBounds?: Electron.Rectangle): {
  x: number;
  y: number;
} {
  const primaryDisplay = screen.getPrimaryDisplay();
  const workArea = primaryDisplay.workArea;

  if (!trayBounds) {
    return {
      x: workArea.x + workArea.width - POPUP_WIDTH - 10,
      y: workArea.y + 5,
    };
  }

  // Center popup below tray icon
  let x = Math.round(trayBounds.x + trayBounds.width / 2 - POPUP_WIDTH / 2);
  let y = trayBounds.y + trayBounds.height + 5;

  // Clamp to screen bounds
  if (x + POPUP_WIDTH > workArea.x + workArea.width) {
    x = workArea.x + workArea.width - POPUP_WIDTH - 10;
  }
  if (x < workArea.x) {
    x = workArea.x + 10;
  }
  if (y + POPUP_HEIGHT > workArea.y + workArea.height) {
    y = trayBounds.y - POPUP_HEIGHT - 5; // above tray
  }

  return { x, y };
}
