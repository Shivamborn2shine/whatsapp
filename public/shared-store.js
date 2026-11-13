
(function(global) {
  const STATE_KEY = 'scandrop_state_v3';
  const DB_NAME = 'scandrop_files_db';
  const STORE_NAME = 'files';

  const PLANS = [
    {
      id: 'plan_free_trial',
      name: 'Free / Trial',
      tier: 'free',
      priceText: '$0 / 30 days',
      maxDevices: 1,
      maxMonthlySubmissions: 50,
      batchConversion: false,
      pdfAdvanced: false,
      printerProfiles: false
    },
    {
      id: 'plan_pro_monthly',
      name: 'Pro Monthly',
      tier: 'pro',
      priceText: '$29 / month',
      maxDevices: 1,
      maxMonthlySubmissions: 1000,
      batchConversion: true,
      pdfAdvanced: true,
      printerProfiles: true
    },
    {
      id: 'plan_business_monthly',
      name: 'Business Multi-PC',
      tier: 'business',
      priceText: '$69 / month',
      maxDevices: 5,
      maxMonthlySubmissions: 10000,
      batchConversion: true,
      pdfAdvanced: true,
      printerProfiles: true
    }
  ];

  function getDefaultState() {
    const now = new Date();
    const expiry30 = new Date(now.getTime() + 30 * 24 * 60 * 60 * 1000).toISOString();
    const expiry24h = new Date(now.getTime() + 24 * 60 * 60 * 1000).toISOString();
    return {
      shops: [
        {
          id: 'shop_quickprint_01',
          slug: 'quickprint',
          name: 'QuickPrint Xerox & Stationery',
          tagline: 'Scan. Drop. Done.',
          ownerName: 'Ramesh Kumar',
          ownerEmail: 'ramesh@quickprint.local',
          ownerPhone: '+91 98765 43210',
          planId: 'plan_pro_monthly',
          subscriptionStatus: 'active',
          pairingCode: 'SD-PAIR-7890',
          expiryDate: expiry30,
          createdAt: now.toISOString()
        }
      ],
      devices: [
        {
          id: 'dev_counter_01',
          shopId: 'shop_quickprint_01',
          deviceName: 'Shop Counter PC 1',
          windowsVersion: 'Windows 11 Pro',
          appVersion: '3.0.0',
          status: 'active',
          lastHeartbeatAt: now.toISOString(),
          lastIp: '192.168.1.42'
        }
      ],
      submissions: [
        {
          id: 'SD-1042',
          shopId: 'shop_quickprint_01',
          customerName: 'Vikram Malhotra',
          serviceCategory: 'Color Print',
          instructions: '2 copies on A4 paper',
          status: 'incoming',
          files: [],
          totalSizeBytes: 0,
          createdAt: now.toISOString(),
          expiresAt: expiry24h
        }
      ],
      derivedFiles: [],
      printerProfiles: [
        { id: 'prof_1', name: 'A4 Black & White', paperSize: 'A4', color: false, orientation: 'portrait', duplex: 'simplex' },
        { id: 'prof_2', name: 'A4 Full Color', paperSize: 'A4', color: true, orientation: 'portrait', duplex: 'simplex' },
        { id: 'prof_3', name: 'Photo 4x6 Glossy', paperSize: 'Photo4x6', color: true, orientation: 'portrait', duplex: 'simplex' }
      ],
      auditEvents: [
        {
          id: 'evt_seed_1',
          shopId: 'shop_quickprint_01',
          actorType: 'founder',
          eventType: 'SHOP_CREATED',
          description: 'QuickPrint Xerox & Stationery initialized with Pro Monthly plan',
          createdAt: now.toISOString()
        }
      ],
      workspaceConfig: {
        pairedShopId: 'shop_quickprint_01',
        deviceId: 'dev_counter_01',
        deviceName: 'Shop Counter PC 1',
        storageRoot: 'C:\\Users\\Shop\\ScanDrop'
      }
    };
  }

  function loadState() {
    try {
      const raw = localStorage.getItem(STATE_KEY);
      if (!raw) {
        const def = getDefaultState();
        saveState(def);
        return def;
      }
      const state = JSON.parse(raw);
      // Run 24-hour retention cleanup automatically
      const nowIso = new Date().toISOString();
      let changed = false;
      state.submissions.forEach(sub => {
        if (sub.status !== 'expired' && sub.expiresAt <= nowIso) {
          sub.status = 'expired';
          (sub.files || []).forEach(f => deleteFileBlob(f.id));
          state.auditEvents.unshift({
            id: 'evt_' + Date.now() + '_' + sub.id,
            shopId: sub.shopId,
            actorType: 'system',
            eventType: 'RETENTION_PURGED',
            description: 'Submission ' + sub.id + ' reached 24-hour retention limit and was purged.',
            createdAt: nowIso
          });
          changed = true;
        }
      });
      if (changed) saveState(state);
      return state;
    } catch (e) {
      return getDefaultState();
    }
  }

  function saveState(state) {
    localStorage.setItem(STATE_KEY, JSON.stringify(state));
  }

  function addAudit(state, shopId, actorType, eventType, description) {
    state.auditEvents.unshift({
      id: 'evt_' + Date.now() + '_' + Math.floor(Math.random() * 1000),
      shopId: shopId || null,
      actorType,
      eventType,
      description,
      createdAt: new Date().toISOString()
    });
    if (state.auditEvents.length > 200) state.auditEvents.length = 200;
  }

  // IndexedDB helpers for real file storage
  function openFilesDb() {
    return new Promise((resolve, reject) => {
      const req = indexedDB.open(DB_NAME, 1);
      req.onupgradeneeded = () => {
        const db = req.result;
        if (!db.objectStoreNames.contains(STORE_NAME)) {
          db.createObjectStore(STORE_NAME, { keyPath: 'id' });
        }
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
  }

  async function saveFileBlob(id, blob, name, type) {
    const db = await openFilesDb();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(STORE_NAME, 'readwrite');
      tx.objectStore(STORE_NAME).put({ id, blob, name, type });
      tx.oncomplete = () => resolve(true);
      tx.onerror = () => reject(tx.error);
    });
  }

  async function getFileBlob(id) {
    const db = await openFilesDb();
    return new Promise((resolve, reject) => {
      const tx = db.transaction(STORE_NAME, 'readonly');
      const req = tx.objectStore(STORE_NAME).get(id);
      req.onsuccess = () => resolve(req.result || null);
      req.onerror = () => reject(req.error);
    });
  }

  async function deleteFileBlob(id) {
    try {
      const db = await openFilesDb();
      const tx = db.transaction(STORE_NAME, 'readwrite');
      tx.objectStore(STORE_NAME).delete(id);
    } catch (e) {}
  }

  async function computeSha256(fileOrBlob) {
    const buf = await fileOrBlob.arrayBuffer();
    const hashBuf = await crypto.subtle.digest('SHA-256', buf);
    return Array.from(new Uint8Array(hashBuf)).map(b => b.toString(16).padStart(2, '0')).join('');
  }

  global.ScanDropStore = {
    PLANS,
    loadState,
    saveState,
    addAudit,
    saveFileBlob,
    getFileBlob,
    deleteFileBlob,
    computeSha256
  };
})(window);
