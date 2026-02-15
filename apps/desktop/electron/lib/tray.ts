import { BrowserWindow, Menu, Notification, Tray, app, screen } from 'electron';
import path from 'node:path';
import { getPublicPath } from './config.js';
import { createPopupWindow } from './window.js';
import { getSettings, updateSettings } from './reminder-sync/index.js';
import { getSettings as getDueDateSettings, setEnabled as setDueDateEnabled } from './due-date-notifier.js';

let tray: Tray | null = null;
let popup: BrowserWindow | null = null;
let lastHideTime = 0;
let dbRef: import('@tasker/core').TaskerDb | null = null;

export function createTray(db?: import('@tasker/core').TaskerDb): Tray {
  if (db) dbRef = db;
  const iconPath = path.join(getPublicPath(), 'trayTemplate.png');
  tray = new Tray(iconPath);

  tray.setToolTip('Tasker');
  tray.on('click', () => togglePopup());
  tray.on('right-click', () => {
    const reminderSettings = getSettings();
    const dueDateSettings = getDueDateSettings();
    const contextMenu = Menu.buildFromTemplate([
      { label: 'Open', click: () => togglePopup() },
      { type: 'separator' },
      {
        label: 'Reminder Sync',
        type: 'checkbox',
        checked: reminderSettings.enabled,
        click: (menuItem) => {
          updateSettings({
            ...getSettings(),
            enabled: menuItem.checked,
          }).then((status) => {
            if (menuItem.checked) {
              if (status.lastError) {
                new Notification({
                  title: 'Tasker Reminder Sync',
                  body: `Sync failed: ${status.lastError}`,
                }).show();
              } else {
                new Notification({
                  title: 'Tasker Reminder Sync',
                  body: `Enabled — ${status.eventCount} reminder${status.eventCount === 1 ? '' : 's'} synced`,
                }).show();
              }
            }
          }).catch((err) => {
            console.error('[REMINDER-SYNC]: tray toggle error:', err);
          });
        },
      },
      {
        label: 'Due Date Alerts',
        type: 'checkbox',
        checked: dueDateSettings.enabled,
        click: (menuItem) => {
          if (dbRef) {
            setDueDateEnabled(dbRef, menuItem.checked);
          }
        },
      },
      { type: 'separator' },
      { label: 'Quit', click: () => app.quit() },
    ]);
    tray?.popUpContextMenu(contextMenu);
  });

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
  ensurePopup(() => showPopup());
}

/** Ensure popup window exists, then call the callback once ready. */
function ensurePopup(onReady: () => void): void {
  if (popup && !popup.isDestroyed()) {
    onReady();
    return;
  }

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
    onReady();
  });
}

export function showPopup(): void {
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
  popup.webContents.send('popup:shown');

  // On macOS, ensure the window becomes key
  if (process.platform === 'darwin') {
    app.dock.hide();
  }
}

/**
 * Open the popup and send a search query to the renderer.
 * Creates the popup if it doesn't exist yet.
 */
export function openPopupWithSearch(query: string): void {
  ensurePopup(() => {
    showPopup();
    // Send the search query after a short delay so the renderer has time to mount
    if (popup && !popup.isDestroyed()) {
      popup.webContents.send('set-search', query);
    }
  });
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
