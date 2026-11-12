import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
// @ts-ignore
import { ConverterEngine } from '../../desktop/dist/main/converter/converterEngine';

const SERVER_URL = 'http://localhost:3001';

async function runTests() {
  console.log('====================================================');
  console.log('🚀 Running Scan Drop End-to-End Verification Suite');
  console.log('====================================================\n');

  let passed = 0;
  let failed = 0;

  function assert(condition: boolean, testName: string) {
    if (condition) {
      console.log(`✅ [PASS] ${testName}`);
      passed++;
    } else {
      console.error(`❌ [FAIL] ${testName}`);
      failed++;
    }
  }

  try {
    // 1. Check Server Health & Overview
    console.log('--- 1. Founder Superadmin & System Overview ---');
    const overviewRes = await fetch(`${SERVER_URL}/api/v1/superadmin/overview`);
    const overview = await overviewRes.json() as any;
    assert(overview.success === true, 'Founder API responds with success: true');
    assert(overview.stats.totalShops >= 1, 'At least 1 shop seeded (QuickPrint)');

    // 2. Fetch Shops & Pairing Code
    const shopsRes = await fetch(`${SERVER_URL}/api/v1/superadmin/shops`);
    const shopsData = await shopsRes.json() as any;
    const demoShop = shopsData.shops.find((s: any) => s.slug === 'quickprint');
    assert(!!demoShop, 'Found seeded demo shop "QuickPrint"');
    assert(demoShop.pairing_code === 'SD-PAIR-7890', 'Pairing code is SD-PAIR-7890');
    assert(demoShop.plan_name === 'Pro Monthly', 'Shop has Pro Monthly plan');

    // 3. Test Windows Device Pairing
    console.log('\n--- 2. Windows Desktop Device Pairing ---');
    const pairRes = await fetch(`${SERVER_URL}/api/v1/auth/pair`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        pairingCode: demoShop.pairing_code,
        deviceName: 'Shop Counter PC 1 (Test)',
        windowsVersion: 'Windows 11 Pro 64-bit',
        appVersion: '3.0.0'
      })
    });
    const pairData = await pairRes.json() as any;
    assert(pairRes.ok && pairData.success === true, 'Device pairing succeeds');
    assert(!!pairData.deviceToken, 'Device received scoped credential token');
    assert(pairData.subscription.entitlements.batch_conversion_enabled === true, 'Batch conversion entitlement verified');

    const deviceToken = pairData.deviceToken;

    // 4. Test Device Heartbeat
    console.log('\n--- 3. Device Heartbeat & Health Check ---');
    const hbRes = await fetch(`${SERVER_URL}/api/v1/auth/heartbeat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'x-device-token': deviceToken
      },
      body: JSON.stringify({
        appVersion: '3.0.0',
        windowsVersion: 'Windows 11'
      })
    });
    const hbData = await hbRes.json() as any;
    assert(hbRes.ok && hbData.success === true, 'Heartbeat recorded by SaaS server');
    assert(hbData.subscription.status === 'active', 'Subscription verified active');

    // 5. Test Customer File Upload (Multipart)
    console.log('\n--- 4. Customer File Upload Simulation ---');
    // Create temporary test files (1 PNG and 1 text file renamed as .pdf)
    const testDir = path.resolve(process.cwd(), 'data', 'test_files');
    if (!fs.existsSync(testDir)) fs.mkdirSync(testDir, { recursive: true });

    // Generate a minimal valid 1x1 transparent PNG
    const pngPath = path.join(testDir, 'customer_id_card.png');
    const minimalPng = Buffer.from('89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c4890000000a49444154789c63000100000500010d0a2d400000000049454e44ae426082', 'hex');
    fs.writeFileSync(pngPath, minimalPng);

    // Prepare multipart upload
    const formData = new FormData();
    formData.append('shopSlug', 'quickprint');
    formData.append('customerName', 'Vikram Malhotra');
    formData.append('serviceCategory', 'Color Print');
    formData.append('instructions', '2 copies on A4 paper glossy finish');

    const fileBlob = new Blob([minimalPng], { type: 'image/png' });
    formData.append('files', fileBlob, 'customer_id_card.png');

    const uploadRes = await fetch(`${SERVER_URL}/api/v1/submissions/upload`, {
      method: 'POST',
      body: formData
    });
    const uploadData = await uploadRes.json() as any;
    assert(uploadRes.ok && uploadData.success === true, 'Customer upload succeeds without WhatsApp');
    assert(uploadData.receiptId && uploadData.receiptId.startsWith('SD-'), `Generated Receipt ID: ${uploadData.receiptId}`);
    assert(uploadData.fileCount === 1, '1 file registered');

    const receiptId = uploadData.receiptId;

    // 6. Test Customer Receipt Status Lookup
    console.log('\n--- 5. Customer Receipt Tracking ---');
    const statusRes = await fetch(`${SERVER_URL}/api/v1/submissions/status/${receiptId}`);
    const statusData = await statusRes.json() as any;
    assert(statusRes.ok && statusData.success === true, 'Receipt lookup succeeds');
    assert(parseFloat(statusData.receipt.remainingHours) > 23.5, '24-hour retention countdown verified (> 23.5h)');

    // 7. Test Background Sync Engine Poll
    console.log('\n--- 6. Windows Receiver Background Sync & Download ---');
    const syncRes = await fetch(`${SERVER_URL}/api/v1/submissions/sync`, {
      headers: { 'x-device-token': deviceToken }
    });
    const syncData = await syncRes.json() as any;
    assert(syncRes.ok && syncData.success === true, 'Sync request authenticated');
    const syncedSub = syncData.submissions.find((s: any) => s.id === receiptId);
    assert(!!syncedSub, 'Target submission retrieved in sync queue');
    assert(syncedSub.files.length === 1, 'File metadata present with SHA-256');

    const fileToDownload = syncedSub.files[0];
    const expectedHash = fileToDownload.sha256_hash;

    // 8. Download File & Verify SHA-256
    const dlRes = await fetch(`${SERVER_URL}/api/v1/submissions/files/${fileToDownload.id}/download`, {
      headers: { 'x-device-token': deviceToken }
    });
    assert(dlRes.ok, 'File stream downloaded successfully');
    const downloadedBuffer = Buffer.from(await dlRes.arrayBuffer());
    const downloadedHash = crypto.createHash('sha256').update(downloadedBuffer).digest('hex');
    assert(downloadedHash.toLowerCase() === expectedHash.toLowerCase(), 'SHA-256 integrity verification PASSED');

    // 9. Test Converter Engine (Image-to-PDF & Manipulations)
    console.log('\n--- 7. Document & PDF Converter Engine (Isolated Worker) ---');
    const outputPdfPath = path.join(testDir, 'output_generated.pdf');
    const convResult = await ConverterEngine.convertImagesToPdf(
      [pngPath],
      outputPdfPath,
      { paperSize: 'A4', orientation: 'portrait', marginPt: 20 }
    );
    assert(fs.existsSync(outputPdfPath), 'PDF file generated on disk');
    assert(convResult.pageCount === 1, 'A4 PDF page count verified');

    // 10. Test PDF Split
    const splitDir = path.join(testDir, 'split_output');
    const splitFiles = await ConverterEngine.splitPdf(outputPdfPath, splitDir, 'page');
    assert(splitFiles.length === 1 && fs.existsSync(splitFiles[0]), 'PDF Split tool verified');

    // 11. Test PDF Rotate
    const rotatedPdfPath = path.join(testDir, 'output_rotated.pdf');
    const rotResult = await ConverterEngine.rotatePdfPages(outputPdfPath, rotatedPdfPath, 90);
    assert(fs.existsSync(rotatedPdfPath) && rotResult.pageCount === 1, 'PDF Rotate 90deg tool verified');

    console.log('\n====================================================');
    console.log(`🎉 TEST SUMMARY: ${passed} PASSED, ${failed} FAILED`);
    console.log('====================================================\n');

    if (failed > 0) {
      process.exit(1);
    }
  } catch (err: any) {
    console.error('Test execution error:', err);
    process.exit(1);
  }
}

runTests();
