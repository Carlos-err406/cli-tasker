import { BrowserWindow, Menu, Tray, app, screen } from 'electron';
import path from 'node:path';
import { getPublicPath } from './config.js';
import { createPopupWindow } from './window.js';

let tray: Tray | null = null;
let popup: BrowserWindow | null = null;
let lastHideTime = 0;

export function createTray(): Tray {
  const iconPath = path.join(getPublicPath(), 'trayTemplate.png');
  tray = new Tray(iconPath);

  const contextMenu = Menu.buildFromTemplate([
    { label: 'Open', click: () => togglePopup() },
    { type: 'separator' },
    { label: 'Quit', click: () => app.quit() },
  ]);

  tray.setToolTip('Tasker');
  tray.on('click', () => togglePopup());
  tray.on('right-click', () => tray?.popUpContextMenu(contextMenu));

  return tray;
}

function togglePopup(): void {
  // Debounce: prevent double-toggle when clicking tray to close
  if (Date.now() - lastHideTime < 300) return;

  if (popup && !popup.isDestroyed()) {
    if (popup.isVisible()) {
      hidePopup();
    } else {
      showPopup();
    }
    return;
  }

  // Create a new popup
  const trayBounds = tray?.getBounds();
  popup = createPopupWindow(trayBounds);

  popup.on('closed', () => {
    popup = null;
  });

  popup.on('blur', () => {
    if (popup && !popup.isDestroyed() && popup.isVisible()) {
      hidePopup();
    }
  });

  popup.once('ready-to-show', () => {
    showPopup();
  });
}

function showPopup(): void {
  if (!popup || popup.isDestroyed()) return;

  // Reposition relative to tray
  if (tray) {
    const trayBounds = tray.getBounds();
    const popupBounds = popup.getBounds();
    let x = Math.round(
      trayBounds.x + trayBounds.width / 2 - popupBounds.width / 2,
    );
    const y = trayBounds.y + trayBounds.height + 5;
    // Clamp X
    const { workArea } = screen.getPrimaryDisplay();
    if (x + popupBounds.width > workArea.x + workArea.width) {
      x = workArea.x + workArea.width - popupBounds.width - 10;
    }
    if (x < workArea.x) x = workArea.x + 10;
    popup.setPosition(x, y);
  }

  popup.show();
  popup.focus();

  // On macOS, ensure the window becomes key
  if (process.platform === 'darwin') {
    app.dock.hide();
  }
}

function hidePopup(): void {
  if (!popup || popup.isDestroyed()) return;
  popup.hide();
  popup.webContents.send('popup:hidden');
  lastHideTime = Date.now();
}

export function getPopupWindow(): BrowserWindow | null {
  return popup && !popup.isDestroyed() ? popup : null;
}
