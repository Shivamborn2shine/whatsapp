import { app, BrowserWindow, ipcMain, shell, dialog, Notification } from 'electron';
import path from 'node:path';
import fs from 'node:fs';
import { getLocalDb, getConfigValue, setConfigValue, logLocalAction } from '../common/db';
import { initStorageDirectories, getScanDropRootDir } from '../common/storage';
import { BackgroundSyncService } from '../service/syncService';
import { ConverterEngine } from '../converter/converterEngine';

let mainWindow: BrowserWindow | null = null;
let syncService: BackgroundSyncService | null = null;

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1280,
    height: 820,
    minWidth: 1024,
    minHeight: 700,
    title: 'Scan Drop — Shopkeeper Workspace',
    backgroundColor: '#0f172a',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      nodeIntegration: false,
      contextIsolation: true,
      webSecurity: false // Permits local preview of rendered PDFs and images
    }
  });

  const isDev = process.env.NODE_ENV === 'development';
  if (isDev) {
    mainWindow.loadURL('http://localhost:5173');
  } else {
    mainWindow.loadFile(path.join(__dirname, '../renderer/index.html'));
  }

  mainWindow.on('closed', () => {
    mainWindow = null;
  });
}

app.whenReady().then(() => {
  initStorageDirectories();

  // Start background receiver service
  syncService = new BackgroundSyncService({
    onNotification: (title, message) => {
      if (Notification.isSupported()) {
        new Notification({ title, body: message }).show();
      }
      if (mainWindow && !mainWindow.isDestroyed()) {
        mainWindow.webContents.send('notification:received', { title, message });
      }
    }
  });
  syncService.start();

  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    if (syncService) syncService.stop();
    app.quit();
  }
});

// IPC: Dashboard data
ipcMain.handle('api:get-dashboard-data', async () => {
  const db = getLocalDb();

  const submissions = db.prepare(`
    SELECT * FROM local_submissions
    WHERE status != 'expired'
    ORDER BY created_at DESC
  `).all() as any[];

  for (const s of submissions) {
    s.files = db.prepare('SELECT * FROM local_files WHERE submission_id = ?').all(s.id);
  }

  const derivedFiles = db.prepare('SELECT * FROM local_derived_files ORDER BY created_at DESC').all();
  const printerProfiles = db.prepare('SELECT * FROM local_printer_profiles').all();

  return {
    isPaired: !!getConfigValue('deviceToken'),
    shopName: getConfigValue('shopName') || 'Unpaired Shop',
    subscriptionStatus: getConfigValue('subscriptionStatus') || 'trial',
    subscriptionTier: getConfigValue('subscriptionTier') || 'free',
    subscriptionExpiry: getConfigValue('subscriptionExpiry'),
    entitlements: getConfigValue('entitlementsJson') ? JSON.parse(getConfigValue('entitlementsJson')!) : null,
    submissions,
    derivedFiles,
    printerProfiles
  };
});

// IPC: Device Pairing
ipcMain.handle('api:pair-device', async (_, { pairingCode, cloudUrl }) => {
  const targetCloud = cloudUrl || getConfigValue('cloudServerUrl') || 'http://localhost:3001';

  try {
    const res = await fetch(`${targetCloud}/api/v1/auth/pair`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        pairingCode,
        deviceName: 'Shop Counter PC 1',
        windowsVersion: 'Windows 11',
        appVersion: '3.0.0'
      })
    });

    const data = await res.json() as any;
    if (!res.ok) {
      throw new Error(data.error || 'Failed to pair device');
    }

    setConfigValue('cloudServerUrl', targetCloud);
    setConfigValue('deviceToken', data.deviceToken);
    setConfigValue('deviceId', data.deviceId);
    setConfigValue('shopId', data.shop.id);
    setConfigValue('shopName', data.shop.name);
    setConfigValue('shopSlug', data.shop.slug);
    setConfigValue('subscriptionStatus', data.subscription.status);
    setConfigValue('subscriptionTier', data.subscription.tier);
    setConfigValue('subscriptionExpiry', data.subscription.expiryDate);
    setConfigValue('entitlementsJson', JSON.stringify(data.subscription.entitlements));
    setConfigValue('deviceStatus', 'active');

    logLocalAction('DEVICE_PAIRED', `Paired with shop ${data.shop.name}`);

    // Trigger sync immediately
    if (syncService) {
      await syncService.performSync();
    }

    return { success: true, shop: data.shop };
  } catch (err: any) {
    return { success: false, error: err.message };
  }
});

// IPC: Change Submission Status (Incoming, Processing, Completed)
ipcMain.handle('api:update-status', async (_, { submissionId, status }) => {
  const db = getLocalDb();
  db.prepare('UPDATE local_submissions SET status = ? WHERE id = ?').run(status, submissionId);

  // Notify Cloud
  const deviceToken = getConfigValue('deviceToken');
  const cloudUrl = getConfigValue('cloudServerUrl') || 'http://localhost:3001';
  if (deviceToken) {
    fetch(`${cloudUrl}/api/v1/submissions/${submissionId}/status`, {
      method: 'PATCH',
      headers: {
        'Content-Type': 'application/json',
        'x-device-token': deviceToken
      },
      body: JSON.stringify({ status })
    }).catch(err => console.warn('Could not update status on cloud:', err.message));
  }

  logLocalAction('STATUS_CHANGED', `Submission ${submissionId} moved to ${status}`);
  return { success: true };
});

// IPC: Safe Open in External App (Section 4.5: Creates working copy to preserve original)
ipcMain.handle('api:open-file', async (_, { filePath, createWorkingCopy = true, submissionId }) => {
  try {
    if (!fs.existsSync(filePath)) {
      throw new Error(`File does not exist: ${filePath}`);
    }

    let targetToOpen = filePath;

    if (createWorkingCopy) {
      const dirs = initStorageDirectories();
      const ext = path.extname(filePath);
      const base = path.basename(filePath, ext);
      const copyName = `${base}_working_copy_${Date.now()}${ext}`;
      const workingCopyPath = path.join(dirs.derived, copyName);

      fs.copyFileSync(filePath, workingCopyPath);
      targetToOpen = workingCopyPath;

      // Register derived working copy in DB
      const db = getLocalDb();
      const sub = submissionId ? db.prepare('SELECT expires_at FROM local_submissions WHERE id = ?').get(submissionId) as any : null;
      const expiry = sub ? sub.expires_at : new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString();

      db.prepare(`
        INSERT INTO local_derived_files (id, submission_id, display_name, output_format, local_path, size_bytes, created_at, expires_at)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?)
      `).run(
        `der_${Date.now()}`,
        submissionId || null,
        copyName,
        ext.replace('.', ''),
        workingCopyPath,
        fs.statSync(workingCopyPath).size,
        new Date().toISOString(),
        expiry
      );

      logLocalAction('WORKING_COPY_OPENED', `Created working copy and opened: ${copyName}`);
    }

    await shell.openPath(targetToOpen);
    return { success: true, openedPath: targetToOpen };
  } catch (err: any) {
    return { success: false, error: err.message };
  }
});

// IPC: Show in Windows File Explorer
ipcMain.handle('api:show-in-folder', async (_, filePath) => {
  try {
    if (fs.existsSync(filePath)) {
      shell.showItemInFolder(filePath);
      return { success: true };
    }
    return { success: false, error: 'File not found' };
  } catch (err: any) {
    return { success: false, error: err.message };
  }
});

// IPC: Windows Printer Discovery
ipcMain.handle('api:get-printers', async () => {
  if (!mainWindow) return [];
  try {
    const printers = await mainWindow.webContents.getPrintersAsync();
    return printers;
  } catch (err) {
    console.error('Error fetching Windows printers:', err);
    return [];
  }
});

// IPC: Print Document
ipcMain.handle('api:print-file', async (_, { filePath, printerName, copies = 1, isColor = false, duplexMode = 'simplex', pageSize = 'A4' }) => {
  try {
    if (!fs.existsSync(filePath)) {
      throw new Error(`File does not exist: ${filePath}`);
    }

    // Create a hidden printing window
    const printWindow = new BrowserWindow({
      show: false,
      webPreferences: {
        nodeIntegration: false,
        contextIsolation: true
      }
    });

    await printWindow.loadURL(`file://${filePath}`);

    return new Promise((resolve) => {
      printWindow.webContents.print(
        {
          silent: !printerName ? false : true,
          deviceName: printerName || undefined,
          copies: copies || 1,
          color: isColor,
          pageSize: pageSize as any,
          duplexMode: duplexMode as any
        },
        (success, errorType) => {
          printWindow.close();
          if (success) {
            logLocalAction('DOCUMENT_PRINTED', `Printed document ${path.basename(filePath)} to ${printerName || 'Default Printer'}`);
            resolve({ success: true });
          } else {
            resolve({ success: false, error: errorType });
          }
        }
      );
    });
  } catch (err: any) {
    return { success: false, error: err.message };
  }
});

// IPC: Image to PDF conversion
ipcMain.handle('api:convert-images-to-pdf', async (_, { imagePaths, options, submissionId }) => {
  try {
    const dirs = initStorageDirectories();
    const outName = `converted_${Date.now()}.pdf`;
    const outputPath = path.join(dirs.derived, outName);

    const result = await ConverterEngine.convertImagesToPdf(imagePaths, outputPath, options);

    const db = getLocalDb();
    const sub = submissionId ? db.prepare('SELECT expires_at FROM local_submissions WHERE id = ?').get(submissionId) as any : null;
    const expiry = sub ? sub.expires_at : new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString();

    db.prepare(`
      INSERT INTO local_derived_files (id, submission_id, display_name, output_format, local_path, size_bytes, created_at, expires_at)
      VALUES (?, ?, ?, 'pdf', ?, ?, ?, ?)
    `).run(
      `der_${Date.now()}`,
      submissionId || null,
      outName,
      outputPath,
      fs.statSync(outputPath).size,
      new Date().toISOString(),
      expiry
    );

    logLocalAction('IMAGES_CONVERTED_TO_PDF', `Converted ${imagePaths.length} image(s) to ${outName}`);
    return { success: true, result };
  } catch (err: any) {
    return { success: false, error: err.message };
  }
});

// IPC: Merge PDFs
ipcMain.handle('api:merge-pdfs', async (_, { pdfPaths, submissionId }) => {
  try {
    const dirs = initStorageDirectories();
    const outName = `merged_${Date.now()}.pdf`;
    const outputPath = path.join(dirs.derived, outName);

    const result = await ConverterEngine.mergePdfs(pdfPaths, outputPath);

    const db = getLocalDb();
    const sub = submissionId ? db.prepare('SELECT expires_at FROM local_submissions WHERE id = ?').get(submissionId) as any : null;
    const expiry = sub ? sub.expires_at : new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString();

    db.prepare(`
      INSERT INTO local_derived_files (id, submission_id, display_name, output_format, local_path, size_bytes, created_at, expires_at)
      VALUES (?, ?, ?, 'pdf', ?, ?, ?, ?)
    `).run(
      `der_${Date.now()}`,
      submissionId || null,
      outName,
      outputPath,
      fs.statSync(outputPath).size,
      new Date().toISOString(),
      expiry
    );

    logLocalAction('PDFS_MERGED', `Merged ${pdfPaths.length} PDF(s) to ${outName}`);
    return { success: true, result };
  } catch (err: any) {
    return { success: false, error: err.message };
  }
});

// IPC: Export file outside managed directory
ipcMain.handle('api:export-file', async (_, { srcFilePath }) => {
  try {
    if (!fs.existsSync(srcFilePath)) {
      throw new Error('Source file not found');
    }
    const filename = path.basename(srcFilePath);
    const { filePath } = await dialog.showSaveDialog({
      title: 'Export File Outside Scan Drop',
      defaultPath: path.join(app.getPath('downloads'), filename)
    });

    if (filePath) {
      fs.copyFileSync(srcFilePath, filePath);
      logLocalAction('FILE_EXPORTED', `Exported ${filename} to ${filePath}`);
      return { success: true, exportedPath: filePath };
    }
    return { success: false, cancelled: true };
  } catch (err: any) {
    return { success: false, error: err.message };
  }
});

// IPC: Local Audit Logs
ipcMain.handle('api:get-history', async () => {
  const db = getLocalDb();
  const logs = db.prepare('SELECT * FROM local_history ORDER BY created_at DESC LIMIT 100').all();
  return logs;
});

