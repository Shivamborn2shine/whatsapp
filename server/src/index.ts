import express from 'express';
import cors from 'cors';
import qrcode from 'qrcode';
import { initDatabase, db } from './db/database';
import { startRetentionWorker } from './services/retention';
import { authRouter } from './routes/api/auth';
import { submissionsRouter } from './routes/api/submissions';
import { superadminRouter } from './routes/api/superadmin';
import { renderCustomerUploadHtml } from './views/customerUpload';
import { renderSuperadminHtml } from './views/superadminHtml';

const app = express();
const PORT = process.env.PORT || 3001;

app.use(cors());
app.use(express.json());
app.use(express.urlencoded({ extended: true }));

// Initialize SQLite database and retention worker
initDatabase();
startRetentionWorker(5); // Run every 5 minutes

// API Routes
app.use('/api/v1/auth', authRouter);
app.use('/api/v1/submissions', submissionsRouter);
app.use('/api/v1/superadmin', superadminRouter);

// Customer QR Landing & Upload Page
app.get('/shop/:slug', async (req, res) => {
  try {
    const { slug } = req.params;
    const shop = db.prepare('SELECT * FROM shops WHERE slug = ?').get(slug) as any;

    if (!shop) {
      return res.status(404).send('<h2>Shop not found. Please check your QR code or URL.</h2>');
    }

    const host = req.get('host') || `localhost:${PORT}`;
    const protocol = req.protocol;
    const uploadUrl = `${protocol}://${host}/shop/${slug}`;
    const qrDataUrl = await qrcode.toDataURL(uploadUrl, { margin: 1, width: 250 });

    const html = renderCustomerUploadHtml(shop, qrDataUrl);
    res.setHeader('Content-Type', 'text/html');
    res.send(html);
  } catch (err: any) {
    console.error('Error rendering shop upload page:', err);
    res.status(500).send('Internal Server Error');
  }
});

// Founder Super Admin Dashboard
app.get('/superadmin', (req, res) => {
  const html = renderSuperadminHtml();
  res.setHeader('Content-Type', 'text/html');
  res.send(html);
});

// Root route
app.get('/', (req, res) => {
  res.send(`
    <!DOCTYPE html>
    <html>
    <head><title>Scan Drop</title></head>
    <body style="font-family:sans-serif;padding:40px;text-align:center;background:#f8fafc;">
      <h1 style="color:#2563eb;">Scan Drop Cloud SaaS Platform</h1>
      <p style="color:#64748b;margin:16px 0;">Scan. Drop. Done.</p>
      <div style="margin-top:24px;display:flex;gap:16px;justify-content:center;">
        <a href="/shop/quickprint" style="background:#2563eb;color:#fff;padding:12px 20px;border-radius:8px;text-decoration:none;font-weight:600;">Customer Upload Demo (QuickPrint)</a>
        <a href="/superadmin" style="background:#0f172a;color:#fff;padding:12px 20px;border-radius:8px;text-decoration:none;font-weight:600;">Founder Super Admin Dashboard</a>
      </div>
    </body>
    </html>
  `);
});

const server = app.listen(PORT, () => {
  console.log(`=========================================`);
  console.log(`⚡ Scan Drop Server running on http://localhost:${PORT}`);
  console.log(`📱 Customer Portal: http://localhost:${PORT}/shop/quickprint`);
  console.log(`👑 Founder Super Admin: http://localhost:${PORT}/superadmin`);
  console.log(`=========================================`);
});

