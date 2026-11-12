import { contextBridge, ipcRenderer } from 'electron';

contextBridge.exposeInMainWorld('scanDropApi', {
  getDashboardData: () => ipcRenderer.invoke('api:get-dashboard-data'),
  pairDevice: (data: { pairingCode: string; cloudUrl?: string }) => ipcRenderer.invoke('api:pair-device', data),
  updateStatus: (data: { submissionId: string; status: string }) => ipcRenderer.invoke('api:update-status', data),
  openFile: (data: { filePath: string; createWorkingCopy?: boolean; submissionId?: string }) => ipcRenderer.invoke('api:open-file', data),
  showInFolder: (filePath: string) => ipcRenderer.invoke('api:show-in-folder', filePath),
  getPrinters: () => ipcRenderer.invoke('api:get-printers'),
  printFile: (options: any) => ipcRenderer.invoke('api:print-file', options),
  convertImagesToPdf: (options: any) => ipcRenderer.invoke('api:convert-images-to-pdf', options),
  mergePdfs: (options: any) => ipcRenderer.invoke('api:merge-pdfs', options),
  exportFile: (options: any) => ipcRenderer.invoke('api:export-file', options),
  getHistory: () => ipcRenderer.invoke('api:get-history'),
  onNotification: (callback: (data: any) => void) => {
    ipcRenderer.on('notification:received', (_, data) => callback(data));
  }
});

