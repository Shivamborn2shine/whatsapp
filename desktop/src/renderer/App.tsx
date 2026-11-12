import React, { useState, useEffect, useRef } from 'react';
import {
  Inbox,
  PlayCircle,
  CheckCircle2,
  FolderOpen,
  FileText,
  Printer,
  History as HistoryIcon,
  Settings as SettingsIcon,
  CreditCard,
  Search,
  RefreshCw,
  ExternalLink,
  Eye,
  FileCheck,
  ChevronRight,
  ShieldCheck,
  Clock,
  Trash2,
  Download,
  Copy,
  Layers,
  Sliders,
  AlertTriangle
} from 'lucide-react';

declare global {
  interface Window {
    scanDropApi: any;
  }
}

export default function App() {
  const [activeTab, setActiveTab] = useState<'inbox' | 'processing' | 'completed' | 'fileManager' | 'pdfTools' | 'printers' | 'history' | 'settings' | 'subscription'>('inbox');
  const [dashboardData, setDashboardData] = useState<any>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedSubmission, setSelectedSubmission] = useState<any>(null);

  // Modals state
  const [showPairModal, setShowPairModal] = useState(false);
  const [pairingCodeInput, setPairingCodeInput] = useState('');
  const [previewFile, setPreviewFile] = useState<any>(null);
  const [printModalFile, setPrintModalFile] = useState<any>(null);
  const [showPdfConvertModal, setShowPdfConvertModal] = useState(false);
  const [selectedImagesForPdf, setSelectedImagesForPdf] = useState<string[]>([]);
  const [pdfPaperSize, setPdfPaperSize] = useState<'A4' | 'A5' | 'Letter' | 'Legal' | 'Photo4x6'>('A4');
  const [pdfOrientation, setPdfOrientation] = useState<'portrait' | 'landscape'>('portrait');
  const [printersList, setPrintersList] = useState<any[]>([]);
  const [selectedPrinter, setSelectedPrinter] = useState<string>('');
  const [printCopies, setPrintCopies] = useState<number>(1);
  const [printIsColor, setPrintIsColor] = useState<boolean>(false);
  const [historyLogs, setHistoryLogs] = useState<any[]>([]);

  const searchInputRef = useRef<HTMLInputElement>(null);

  const loadData = async () => {
    if (!window.scanDropApi) return;
    try {
      const data = await window.scanDropApi.getDashboardData();
      setDashboardData(data);
      if (!data.isPaired) {
        setShowPairModal(true);
      }
    } catch (e) {
      console.error('Error loading dashboard data:', e);
    }
  };

  const loadPrinters = async () => {
    if (!window.scanDropApi) return;
    const list = await window.scanDropApi.getPrinters();
    setPrintersList(list);
    if (list.length > 0 && !selectedPrinter) {
      setSelectedPrinter(list[0].name);
    }
  };

  const loadHistory = async () => {
    if (!window.scanDropApi) return;
    const logs = await window.scanDropApi.getHistory();
    setHistoryLogs(logs);
  };

  useEffect(() => {
    loadData();
    loadPrinters();

    const interval = setInterval(() => {
      loadData();
    }, 4000);

    // Keyboard Shortcuts (PRD Section 7.4)
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.key === 'f') {
        e.preventDefault();
        searchInputRef.current?.focus();
      } else if (e.key === 'F5') {
        e.preventDefault();
        loadData();
      } else if (e.key === 'Escape') {
        setPreviewFile(null);
        setPrintModalFile(null);
        setShowPdfConvertModal(false);
        setShowPairModal(false);
      } else if (e.ctrlKey && e.key === 'p') {
        if (previewFile) {
          e.preventDefault();
          setPrintModalFile(previewFile);
        }
      } else if (e.ctrlKey && e.shiftKey && (e.key === 'C' || e.key === 'c')) {
        e.preventDefault();
        setShowPdfConvertModal(true);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => {
      clearInterval(interval);
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, []);

  const handlePairSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!pairingCodeInput.trim()) return;

    try {
      const res = await window.scanDropApi.pairDevice({
        pairingCode: pairingCodeInput.trim()
      });
      if (res.success) {
        alert(`Successfully paired to ${res.shop.name}!`);
        setShowPairModal(false);
        loadData();
      } else {
        alert('Pairing failed: ' + res.error);
      }
    } catch (err: any) {
      alert('Error: ' + err.message);
    }
  };

  const handleUpdateStatus = async (submissionId: string, status: string) => {
    await window.scanDropApi.updateStatus({ submissionId, status });
    loadData();
  };

  const handleOpenFile = async (file: any, createWorkingCopy: boolean = true) => {
    const res = await window.scanDropApi.openFile({
      filePath: file.local_path,
      createWorkingCopy,
      submissionId: file.submission_id
    });
    if (!res.success) {
      alert('Could not open file: ' + res.error);
    }
  };

  const handleShowInFolder = async (filePath: string) => {
    await window.scanDropApi.showInFolder(filePath);
  };

  const handleExecutePrint = async () => {
    if (!printModalFile) return;
    const res = await window.scanDropApi.printFile({
      filePath: printModalFile.local_path,
      printerName: selectedPrinter,
      copies: printCopies,
      isColor: printIsColor
    });
    if (res.success) {
      alert('Print job sent to ' + (selectedPrinter || 'Default Printer'));
      setPrintModalFile(null);
    } else {
      alert('Print error: ' + (res.error || 'Failed to print'));
    }
  };

  const handleExecuteImageToPdf = async () => {
    if (selectedImagesForPdf.length === 0) {
      alert('Please select at least one image to convert.');
      return;
    }
    const res = await window.scanDropApi.convertImagesToPdf({
      imagePaths: selectedImagesForPdf,
      options: {
        paperSize: pdfPaperSize,
        orientation: pdfOrientation,
        marginPt: 20
      }
    });

    if (res.success) {
      alert('PDF created successfully! Saved in Derived files.');
      setShowPdfConvertModal(false);
      setSelectedImagesForPdf([]);
      loadData();
    } else {
      alert('Conversion failed: ' + res.error);
    }
  };

  // Submissions filtered by tab & search query
  const submissions = (dashboardData?.submissions || []).filter((s: any) => {
    const matchesSearch = !searchQuery ||
      s.id.toLowerCase().includes(searchQuery.toLowerCase()) ||
      (s.customer_name && s.customer_name.toLowerCase().includes(searchQuery.toLowerCase())) ||
      (s.service_category && s.service_category.toLowerCase().includes(searchQuery.toLowerCase()));

    if (!matchesSearch) return false;

    if (activeTab === 'inbox') return s.status === 'incoming';
    if (activeTab === 'processing') return s.status === 'processing';
    if (activeTab === 'completed') return s.status === 'completed';
    return true;
  });

  const inboxCount = (dashboardData?.submissions || []).filter((s: any) => s.status === 'incoming').length;
  const processingCount = (dashboardData?.submissions || []).filter((s: any) => s.status === 'processing').length;

  return (
    <div style={{ display: 'flex', height: '100vh', width: '100vw', background: '#0f172a' }}>
      {/* Sidebar */}
      <div style={{ width: '240px', background: '#1e293b', borderRight: '1px solid #334155', display: 'flex', flexDirection: 'column' }}>
        <div style={{ padding: '20px 16px', display: 'flex', alignItems: 'center', gap: '10px', borderBottom: '1px solid #334155' }}>
          <div style={{ width: '36px', height: '36px', background: '#2563eb', borderRadius: '10px', display: 'flex', alignItems: 'center', justifyContent: 'center', fontWeight: '900', fontSize: '18px', color: '#fff' }}>
            ⚡
          </div>
          <div>
            <div style={{ fontWeight: '800', fontSize: '16px', color: '#fff', letterSpacing: '0.5px' }}>Scan Drop</div>
            <div style={{ fontSize: '11px', color: '#94a3b8' }}>v3.0 Desktop Edition</div>
          </div>
        </div>

        {/* Navigation */}
        <nav style={{ padding: '16px 8px', flex: 1, display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <button
            onClick={() => setActiveTab('inbox')}
            style={{
              display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '10px 12px', borderRadius: '8px',
              background: activeTab === 'inbox' ? '#2563eb' : 'transparent', color: activeTab === 'inbox' ? '#fff' : '#cbd5e1',
              border: 'none', cursor: 'pointer', fontWeight: 600, fontSize: '13px'
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
              <Inbox size={18} />
              <span>Inbox</span>
            </div>
            {inboxCount > 0 && (
              <span style={{ background: '#ef4444', color: '#fff', fontSize: '11px', padding: '2px 8px', borderRadius: '9999px', fontWeight: 700 }}>
                {inboxCount}
              </span>
            )}
          </button>

          <button
            onClick={() => setActiveTab('processing')}
            style={{
              display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '10px 12px', borderRadius: '8px',
              background: activeTab === 'processing' ? '#2563eb' : 'transparent', color: activeTab === 'processing' ? '#fff' : '#cbd5e1',
              border: 'none', cursor: 'pointer', fontWeight: 600, fontSize: '13px'
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
              <PlayCircle size={18} />
              <span>Processing</span>
            </div>
            {processingCount > 0 && (
              <span style={{ background: '#f59e0b', color: '#fff', fontSize: '11px', padding: '2px 8px', borderRadius: '9999px', fontWeight: 700 }}>
                {processingCount}
              </span>
            )}
          </button>

          <button
            onClick={() => setActiveTab('completed')}
            style={{
              display: 'flex', alignItems: 'center', gap: '10px', padding: '10px 12px', borderRadius: '8px',
              background: activeTab === 'completed' ? '#2563eb' : 'transparent', color: activeTab === 'completed' ? '#fff' : '#cbd5e1',
              border: 'none', cursor: 'pointer', fontWeight: 600, fontSize: '13px'
            }}
          >
            <CheckCircle2 size={18} />
            <span>Completed</span>
          </button>

          <div style={{ height: '1px', background: '#334155', margin: '8px 0' }} />

          <button
            onClick={() => setActiveTab('fileManager')}
            style={{
              display: 'flex', alignItems: 'center', gap: '10px', padding: '10px 12px', borderRadius: '8px',
              background: activeTab === 'fileManager' ? '#2563eb' : 'transparent', color: activeTab === 'fileManager' ? '#fff' : '#cbd5e1',
              border: 'none', cursor: 'pointer', fontWeight: 600, fontSize: '13px'
            }}
          >
            <FolderOpen size={18} />
            <span>File Manager</span>
          </button>

          <button
            onClick={() => setActiveTab('pdfTools')}
            style={{
              display: 'flex', alignItems: 'center', gap: '10px', padding: '10px 12px', borderRadius: '8px',
              background: activeTab === 'pdfTools' ? '#2563eb' : 'transparent', color: activeTab === 'pdfTools' ? '#fff' : '#cbd5e1',
              border: 'none', cursor: 'pointer', fontWeight: 600, fontSize: '13px'
            }}
          >
            <FileText size={18} />
            <span>PDF Tools</span>
          </button>

          <button
            onClick={() => { setActiveTab('printers'); loadPrinters(); }}
            style={{
              display: 'flex', alignItems: 'center', gap: '10px', padding: '10px 12px', borderRadius: '8px',
              background: activeTab === 'printers' ? '#2563eb' : 'transparent', color: activeTab === 'printers' ? '#fff' : '#cbd5e1',
              border: 'none', cursor: 'pointer', fontWeight: 600, fontSize: '13px'
            }}
          >
            <Printer size={18} />
            <span>Printers</span>
          </button>

          <button
            onClick={() => { setActiveTab('history'); loadHistory(); }}
            style={{
              display: 'flex', alignItems: 'center', gap: '10px', padding: '10px 12px', borderRadius: '8px',
              background: activeTab === 'history' ? '#2563eb' : 'transparent', color: activeTab === 'history' ? '#fff' : '#cbd5e1',
              border: 'none', cursor: 'pointer', fontWeight: 600, fontSize: '13px'
            }}
          >
            <HistoryIcon size={18} />
            <span>History</span>
          </button>

          <button
            onClick={() => setActiveTab('settings')}
            style={{
              display: 'flex', alignItems: 'center', gap: '10px', padding: '10px 12px', borderRadius: '8px',
              background: activeTab === 'settings' ? '#2563eb' : 'transparent', color: activeTab === 'settings' ? '#fff' : '#cbd5e1',
              border: 'none', cursor: 'pointer', fontWeight: 600, fontSize: '13px'
            }}
          >
            <SettingsIcon size={18} />
            <span>Settings</span>
          </button>
        </nav>

        {/* Subscription status card in sidebar bottom */}
        <div style={{ padding: '14px', borderTop: '1px solid #334155' }}>
          <div
            onClick={() => setActiveTab('subscription')}
            style={{
              background: '#0f172a', border: '1px solid #334155', borderRadius: '10px', padding: '12px',
              cursor: 'pointer', transition: 'border-color 0.2s'
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4px' }}>
              <span style={{ fontSize: '11px', color: '#94a3b8', textTransform: 'uppercase', fontWeight: 700 }}>Plan</span>
              <span className="badge badge-blue">{dashboardData?.subscriptionTier?.toUpperCase() || 'PRO'}</span>
            </div>
            <div style={{ fontSize: '13px', fontWeight: 700, color: '#f8fafc' }}>
              {dashboardData?.shopName || 'Connecting...'}
            </div>
            <div style={{ fontSize: '11px', color: '#10b981', display: 'flex', alignItems: 'center', gap: '4px', marginTop: '4px' }}>
              <span style={{ width: '6px', height: '6px', borderRadius: '50%', background: '#10b981' }}></span>
              72h Offline Grace Active
            </div>
          </div>
        </div>
      </div>

      {/* Main Workspace */}
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
        {/* Top Header */}
        <header style={{ height: '64px', background: '#1e293b', borderBottom: '1px solid #334155', padding: '0 24px', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
            <h2 style={{ fontSize: '18px', fontWeight: 800, textTransform: 'capitalize' }}>
              {activeTab === 'inbox' && 'Incoming Customer Submissions'}
              {activeTab === 'processing' && 'Active Work in Progress'}
              {activeTab === 'completed' && 'Completed Document Orders'}
              {activeTab === 'fileManager' && 'Local Document Organizer'}
              {activeTab === 'pdfTools' && 'PDF & Image Conversion Toolbox'}
              {activeTab === 'printers' && 'Installed Windows Printers & Settings'}
              {activeTab === 'history' && 'Local Audit Log & Actions'}
              {activeTab === 'settings' && 'Shop Preferences & Sync Settings'}
              {activeTab === 'subscription' && 'Subscription & Entitlements'}
            </h2>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            {/* Search Input (Ctrl+F) */}
            <div style={{ position: 'relative', width: '260px' }}>
              <Search size={16} style={{ position: 'absolute', left: '10px', top: '10px', color: '#94a3b8' }} />
              <input
                ref={searchInputRef}
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Search receipt, customer... (Ctrl+F)"
                style={{
                  width: '100%', background: '#0f172a', border: '1px solid #334155', borderRadius: '8px',
                  padding: '8px 12px 8px 34px', color: '#fff', fontSize: '13px', outline: 'none'
                }}
              />
            </div>

            <button onClick={loadData} className="btn-secondary" title="Refresh local queue (F5)">
              <RefreshCw size={15} />
            </button>

            {!dashboardData?.isPaired ? (
              <button onClick={() => setShowPairModal(true)} className="btn-primary" style={{ background: '#f59e0b' }}>
                Pair Device
              </button>
            ) : (
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '12px', color: '#94a3b8', background: '#0f172a', padding: '6px 12px', borderRadius: '8px', border: '1px solid #334155' }}>
                <span style={{ width: '8px', height: '8px', borderRadius: '50%', background: '#10b981' }}></span>
                <span>{dashboardData.shopName}</span>
              </div>
            )}
          </div>
        </header>

        {/* Content Body */}
        <div style={{ flex: 1, overflowY: 'auto', padding: '24px' }}>
          {/* INBOX, PROCESSING, COMPLETED VIEWS */}
          {['inbox', 'processing', 'completed'].includes(activeTab) && (
            <div>
              {submissions.length === 0 ? (
                <div style={{ textAlign: 'center', padding: '80px 20px', color: '#64748b' }}>
                  <Inbox size={48} style={{ margin: '0 auto 16px', opacity: 0.4 }} />
                  <h3 style={{ fontSize: '16px', color: '#94a3b8', marginBottom: '6px' }}>No submissions here</h3>
                  <p style={{ fontSize: '13px' }}>
                    {activeTab === 'inbox' && 'Customer uploads via QR will appear here instantly without opening a browser.'}
                    {activeTab === 'processing' && 'No documents are currently marked as processing.'}
                    {activeTab === 'completed' && 'No completed orders in the 24-hour retention window.'}
                  </p>
                </div>
              ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                  {submissions.map((sub: any) => {
                    const expiry = new Date(sub.expires_at);
                    const remainingHours = Math.max(0, (expiry.getTime() - Date.now()) / (1000 * 60 * 60)).toFixed(1);

                    return (
                      <div
                        key={sub.id}
                        style={{
                          background: '#1e293b', border: '1px solid #334155', borderRadius: '14px', padding: '18px',
                          display: 'flex', flexDirection: 'column', gap: '14px', transition: 'border-color 0.2s'
                        }}
                      >
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                            <span style={{ fontSize: '20px', fontWeight: '900', color: '#38bdf8', letterSpacing: '1px' }}>
                              {sub.id}
                            </span>
                            <span className="badge badge-blue">{sub.service_category || 'General Print'}</span>
                            <span style={{ fontSize: '13px', fontWeight: 600, color: '#f1f5f9' }}>
                              {sub.customer_name || 'Anonymous Customer'}
                            </span>
                          </div>

                          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <span style={{ fontSize: '11px', color: '#94a3b8', display: 'flex', alignItems: 'center', gap: '4px' }}>
                              <Clock size={12} />
                              {remainingHours}h retention left
                            </span>

                            {sub.status === 'incoming' && (
                              <button
                                onClick={() => handleUpdateStatus(sub.id, 'processing')}
                                className="btn-primary"
                                style={{ background: '#f59e0b', fontSize: '12px', padding: '6px 12px' }}
                              >
                                Start Processing
                              </button>
                            )}

                            {sub.status === 'processing' && (
                              <button
                                onClick={() => handleUpdateStatus(sub.id, 'completed')}
                                className="btn-primary"
                                style={{ background: '#10b981', fontSize: '12px', padding: '6px 12px' }}
                              >
                                Mark Completed
                              </button>
                            )}

                            <button
                              onClick={() => handleShowInFolder(sub.local_folder)}
                              className="btn-secondary"
                              style={{ fontSize: '12px', padding: '6px 10px' }}
                              title="Show in Windows Explorer"
                            >
                              <FolderOpen size={14} />
                            </button>
                          </div>
                        </div>

                        {sub.instructions && (
                          <div style={{ background: '#0f172a', padding: '8px 12px', borderRadius: '8px', fontSize: '12px', color: '#cbd5e1', borderLeft: '3px solid #38bdf8' }}>
                            <strong>Instructions:</strong> {sub.instructions}
                          </div>
                        )}

                        {/* Files List */}
                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '10px' }}>
                          {(sub.files || []).map((file: any) => {
                            const isPdf = file.file_extension === '.pdf';
                            const isImg = ['.jpg', '.jpeg', '.png'].includes(file.file_extension);

                            return (
                              <div
                                key={file.id}
                                style={{
                                  background: '#0f172a', border: '1px solid #334155', borderRadius: '10px', padding: '10px 14px',
                                  display: 'flex', alignItems: 'center', gap: '10px', minWidth: '280px'
                                }}
                              >
                                <div style={{ fontSize: '11px', fontWeight: 800, textTransform: 'uppercase', color: isPdf ? '#f87171' : isImg ? '#60a5fa' : '#34d399' }}>
                                  {file.file_extension.replace('.', '')}
                                </div>

                                <div style={{ flex: 1, minWidth: 0 }}>
                                  <div style={{ fontSize: '13px', fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: '#f1f5f9' }}>
                                    {file.original_name}
                                  </div>
                                  <div style={{ fontSize: '11px', color: '#94a3b8' }}>
                                    {(file.size_bytes / 1024).toFixed(1)} KB • SHA-256 Verified
                                  </div>
                                </div>

                                <div style={{ display: 'flex', gap: '4px' }}>
                                  <button
                                    onClick={() => setPreviewFile(file)}
                                    className="btn-secondary"
                                    style={{ padding: '6px 8px', fontSize: '11px' }}
                                    title="Safe Preview"
                                  >
                                    <Eye size={13} />
                                  </button>

                                  <button
                                    onClick={() => handleOpenFile(file, true)}
                                    className="btn-secondary"
                                    style={{ padding: '6px 8px', fontSize: '11px' }}
                                    title="Open Working Copy in Installed App"
                                  >
                                    <ExternalLink size={13} />
                                  </button>

                                  <button
                                    onClick={() => setPrintModalFile(file)}
                                    className="btn-primary"
                                    style={{ padding: '6px 8px', fontSize: '11px' }}
                                    title="Manual Print"
                                  >
                                    <Printer size={13} />
                                  </button>
                                </div>
                              </div>
                            );
                          })}
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          )}

          {/* PDF TOOLS VIEW */}
          {activeTab === 'pdfTools' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '16px' }}>
                <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '14px', padding: '20px' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '12px' }}>
                    <Layers size={22} color="#38bdf8" />
                    <h3 style={{ fontSize: '16px', fontWeight: 700 }}>Image-to-PDF Engine</h3>
                  </div>
                  <p style={{ fontSize: '13px', color: '#94a3b8', lineHeight: 1.5, marginBottom: '16px' }}>
                    Combine multiple customer photos or scans into a single print-ready PDF with paper formatting (A4, A5, 4x6).
                  </p>
                  <button onClick={() => setShowPdfConvertModal(true)} className="btn-primary">
                    Launch Image-to-PDF
                  </button>
                </div>

                <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '14px', padding: '20px' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '12px' }}>
                    <FileText size={22} color="#34d399" />
                    <h3 style={{ fontSize: '16px', fontWeight: 700 }}>Merge PDFs</h3>
                  </div>
                  <p style={{ fontSize: '13px', color: '#94a3b8', lineHeight: 1.5, marginBottom: '16px' }}>
                    Stitch multiple PDF documents into an ordered, single customer document ready for high-speed printing.
                  </p>
                  <button onClick={() => alert('Select PDFs from File Manager or Inbox to merge.')} className="btn-secondary">
                    Merge Documents
                  </button>
                </div>
              </div>

              {/* Derived Documents List */}
              <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '14px', padding: '20px' }}>
                <h3 style={{ fontSize: '15px', fontWeight: 700, marginBottom: '14px' }}>Derived & Converted Output Files</h3>
                {(dashboardData?.derivedFiles || []).length === 0 ? (
                  <p style={{ fontSize: '13px', color: '#64748b' }}>No derived or converted files created yet.</p>
                ) : (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    {(dashboardData?.derivedFiles || []).map((der: any) => (
                      <div key={der.id} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', background: '#0f172a', padding: '10px 14px', borderRadius: '8px' }}>
                        <div>
                          <strong>{der.display_name}</strong>
                          <div style={{ fontSize: '11px', color: '#94a3b8' }}>
                            {(der.size_bytes / 1024).toFixed(1)} KB • Derived copy
                          </div>
                        </div>
                        <div style={{ display: 'flex', gap: '6px' }}>
                          <button onClick={() => setPreviewFile(der)} className="btn-secondary" style={{ padding: '4px 8px', fontSize: '11px' }}>Preview</button>
                          <button onClick={() => setPrintModalFile(der)} className="btn-primary" style={{ padding: '4px 8px', fontSize: '11px' }}>Print</button>
                          <button onClick={() => window.scanDropApi.exportFile({ srcFilePath: der.local_path })} className="btn-secondary" style={{ padding: '4px 8px', fontSize: '11px' }}>Export</button>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          )}

          {/* PRINTERS VIEW */}
          {activeTab === 'printers' && (
            <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '14px', padding: '20px' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '18px' }}>
                <div>
                  <h3 style={{ fontSize: '16px', fontWeight: 700 }}>Detected Windows Printers</h3>
                  <p style={{ fontSize: '12px', color: '#94a3b8', marginTop: '4px' }}>Automatic printing is strictly disabled per PRD. All printing requires explicit staff authorization.</p>
                </div>
                <button onClick={loadPrinters} className="btn-secondary">Refresh Printers</button>
              </div>

              {printersList.length === 0 ? (
                <div style={{ padding: '40px', textAlign: 'center', color: '#64748b' }}>
                  No installed printers reported by Windows. Check printer drivers or USB connection.
                </div>
              ) : (
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', gap: '14px' }}>
                  {printersList.map((p, idx) => (
                    <div key={idx} style={{ background: '#0f172a', border: '1px solid #334155', borderRadius: '10px', padding: '14px' }}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                          <Printer size={18} color="#38bdf8" />
                          <strong style={{ fontSize: '14px' }}>{p.name}</strong>
                        </div>
                        {p.isDefault && <span className="badge badge-green">Default</span>}
                      </div>
                      <div style={{ fontSize: '12px', color: '#94a3b8', marginTop: '8px' }}>
                        Status: Ready • Driver: Windows Print Subsystem
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* HISTORY VIEW */}
          {activeTab === 'history' && (
            <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '14px', padding: '20px' }}>
              <h3 style={{ fontSize: '16px', fontWeight: 700, marginBottom: '16px' }}>Local Operational Audit Trail</h3>
              {historyLogs.length === 0 ? (
                <p style={{ fontSize: '13px', color: '#64748b' }}>No actions recorded yet.</p>
              ) : (
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px', textAlign: 'left' }}>
                  <thead>
                    <tr style={{ color: '#94a3b8', borderBottom: '1px solid #334155' }}>
                      <th style={{ padding: '10px' }}>Timestamp</th>
                      <th style={{ padding: '10px' }}>Action Type</th>
                      <th style={{ padding: '10px' }}>Description</th>
                    </tr>
                  </thead>
                  <tbody>
                    {historyLogs.map((log) => (
                      <tr key={log.id} style={{ borderBottom: '1px solid #243048' }}>
                        <td style={{ padding: '10px', color: '#94a3b8', fontSize: '11px' }}>{new Date(log.created_at).toLocaleTimeString()}</td>
                        <td style={{ padding: '10px' }}><code style={{ color: '#38bdf8' }}>{log.action_type}</code></td>
                        <td style={{ padding: '10px' }}>{log.description}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          )}

          {/* FILE MANAGER VIEW */}
          {activeTab === 'fileManager' && (
            <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '14px', padding: '20px' }}>
              <h3 style={{ fontSize: '16px', fontWeight: 700, marginBottom: '16px' }}>Organized Local Storage Folders</h3>
              <p style={{ fontSize: '13px', color: '#94a3b8', marginBottom: '16px' }}>
                Files are segregated by receipt ID: Incoming, Processing, Completed, Derived, and Quarantine.
              </p>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                {(dashboardData?.submissions || []).map((sub: any) => (
                  <div key={sub.id} style={{ background: '#0f172a', padding: '12px', borderRadius: '8px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div>
                      <strong>{sub.id}</strong> — {sub.customer_name || 'Customer'} ({sub.file_count} files)
                    </div>
                    <button onClick={() => handleShowInFolder(sub.local_folder)} className="btn-secondary" style={{ fontSize: '12px' }}>
                      Open Folder in Explorer
                    </button>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* SUBSCRIPTION VIEW */}
          {activeTab === 'subscription' && (
            <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '14px', padding: '24px', maxWidth: '700px' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                <div>
                  <h3 style={{ fontSize: '18px', fontWeight: 800 }}>Subscription & Entitlements</h3>
                  <p style={{ fontSize: '13px', color: '#94a3b8', marginTop: '4px' }}>Managed centrally by the founder SaaS console</p>
                </div>
                <span className="badge badge-blue" style={{ fontSize: '13px', padding: '4px 12px' }}>
                  {dashboardData?.subscriptionTier?.toUpperCase() || 'PRO'}
                </span>
              </div>

              <div style={{ background: '#0f172a', borderRadius: '10px', padding: '16px', border: '1px solid #334155', marginBottom: '20px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '8px' }}>
                  <span style={{ color: '#94a3b8', fontSize: '13px' }}>Shop Name:</span>
                  <strong>{dashboardData?.shopName}</strong>
                </div>
                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '8px' }}>
                  <span style={{ color: '#94a3b8', fontSize: '13px' }}>Subscription Status:</span>
                  <span style={{ color: '#34d399', fontWeight: 700, textTransform: 'capitalize' }}>
                    {dashboardData?.subscriptionStatus || 'Active'}
                  </span>
                </div>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ color: '#94a3b8', fontSize: '13px' }}>Offline Grace Period:</span>
                  <span style={{ color: '#f59e0b', fontWeight: 700 }}>72 Hours Supported</span>
                </div>
              </div>

              <h4 style={{ fontSize: '14px', fontWeight: 700, marginBottom: '10px' }}>Active Entitlements:</h4>
              <ul style={{ listStyle: 'none', display: 'flex', flexDirection: 'column', gap: '8px', fontSize: '13px' }}>
                <li style={{ color: '#34d399' }}>✓ Automatic QR File Receiving: Enabled</li>
                <li style={{ color: '#34d399' }}>✓ High-Speed Image-to-PDF Engine: Enabled</li>
                <li style={{ color: '#34d399' }}>✓ Batch PDF Manipulation & Merge: Enabled</li>
                <li style={{ color: '#34d399' }}>✓ Printer Profiles & Presets: Enabled</li>
                <li style={{ color: '#34d399' }}>✓ 24-Hour Privacy Purge Worker: Active</li>
              </ul>
            </div>
          )}

          {/* SETTINGS VIEW */}
          {activeTab === 'settings' && (
            <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '14px', padding: '24px', maxWidth: '600px' }}>
              <h3 style={{ fontSize: '18px', fontWeight: 800, marginBottom: '16px' }}>Shop & Application Preferences</h3>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                <div>
                  <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: '#94a3b8', marginBottom: '6px' }}>
                    Connected Cloud SaaS Server
                  </label>
                  <input
                    type="text"
                    defaultValue="http://localhost:3001"
                    readOnly
                    style={{ width: '100%', background: '#0f172a', border: '1px solid #334155', borderRadius: '8px', padding: '10px', color: '#fff', fontSize: '13px' }}
                  />
                </div>
                <div>
                  <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: '#94a3b8', marginBottom: '6px' }}>
                    Local Storage Root
                  </label>
                  <input
                    type="text"
                    defaultValue="%USERPROFILE%/ScanDrop"
                    readOnly
                    style={{ width: '100%', background: '#0f172a', border: '1px solid #334155', borderRadius: '8px', padding: '10px', color: '#fff', fontSize: '13px' }}
                  />
                </div>
                <div style={{ marginTop: '10px' }}>
                  <button onClick={() => setShowPairModal(true)} className="btn-secondary">
                    Re-Pair with Another Shop
                  </button>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* PAIR DEVICE MODAL */}
      {showPairModal && (
        <div className="modal-overlay">
          <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '16px', padding: '24px', width: '100%', maxWidth: '420px' }}>
            <h3 style={{ fontSize: '18px', fontWeight: 800, marginBottom: '8px' }}>Pair Windows PC</h3>
            <p style={{ fontSize: '13px', color: '#94a3b8', marginBottom: '16px' }}>
              Enter the pairing code generated by the founder from the Super Admin dashboard (e.g. <code>SD-PAIR-7890</code>).
            </p>
            <form onSubmit={handlePairSubmit}>
              <input
                type="text"
                value={pairingCodeInput}
                onChange={(e) => setPairingCodeInput(e.target.value)}
                placeholder="e.g. SD-PAIR-7890"
                required
                style={{
                  width: '100%', padding: '12px', background: '#0f172a', border: '1px solid #334155',
                  borderRadius: '10px', color: '#fff', fontSize: '15px', fontWeight: 700, letterSpacing: '1px', marginBottom: '16px', outline: 'none'
                }}
              />
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
                <button type="button" onClick={() => setShowPairModal(false)} className="btn-secondary">Cancel</button>
                <button type="submit" className="btn-primary">Connect & Authorize</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* FILE PREVIEW MODAL */}
      {previewFile && (
        <div className="modal-overlay">
          <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '16px', padding: '20px', width: '90%', maxWidth: '900px', height: '85vh', display: 'flex', flexDirection: 'column' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '14px' }}>
              <div>
                <strong style={{ fontSize: '15px' }}>{previewFile.original_name || previewFile.display_name}</strong>
                <span style={{ fontSize: '12px', color: '#94a3b8', marginLeft: '10px' }}>Safe Preview Mode</span>
              </div>
              <div style={{ display: 'flex', gap: '8px' }}>
                <button onClick={() => { setPrintModalFile(previewFile); }} className="btn-primary" style={{ padding: '6px 12px', fontSize: '12px' }}>
                  <Printer size={14} /> Print
                </button>
                <button onClick={() => setPreviewFile(null)} className="btn-secondary" style={{ padding: '6px 12px', fontSize: '12px' }}>
                  Close (Esc)
                </button>
              </div>
            </div>

            <div style={{ flex: 1, background: '#0f172a', borderRadius: '10px', overflow: 'hidden', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
              {previewFile.file_extension === '.pdf' || previewFile.output_format === 'pdf' ? (
                <iframe src={`file://${previewFile.local_path}`} style={{ width: '100%', height: '100%', border: 'none' }} />
              ) : ['.jpg', '.jpeg', '.png'].includes(previewFile.file_extension) ? (
                <img src={`file://${previewFile.local_path}`} alt="Preview" style={{ maxWidth: '100%', maxHeight: '100%', objectFit: 'contain' }} />
              ) : (
                <div style={{ textAlign: 'center', color: '#94a3b8' }}>
                  <FileText size={48} style={{ margin: '0 auto 12px' }} />
                  <p>Document Preview: Use "Open Working Copy" to view in Microsoft Word / Writer.</p>
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* PRINT MODAL */}
      {printModalFile && (
        <div className="modal-overlay">
          <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '16px', padding: '24px', width: '100%', maxWidth: '480px' }}>
            <h3 style={{ fontSize: '18px', fontWeight: 800, marginBottom: '6px' }}>Print Document</h3>
            <p style={{ fontSize: '13px', color: '#94a3b8', marginBottom: '16px' }}>
              {printModalFile.original_name || printModalFile.display_name}
            </p>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '14px', marginBottom: '20px' }}>
              <div>
                <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: '#94a3b8', marginBottom: '6px' }}>Select Printer</label>
                <select
                  value={selectedPrinter}
                  onChange={(e) => setSelectedPrinter(e.target.value)}
                  style={{ width: '100%', padding: '10px', background: '#0f172a', border: '1px solid #334155', borderRadius: '8px', color: '#fff', fontSize: '13px' }}
                >
                  {printersList.map((p, i) => (
                    <option key={i} value={p.name}>{p.name} {p.isDefault ? '(Default)' : ''}</option>
                  ))}
                </select>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                <div>
                  <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: '#94a3b8', marginBottom: '6px' }}>Number of Copies</label>
                  <input
                    type="number"
                    min="1"
                    max="99"
                    value={printCopies}
                    onChange={(e) => setPrintCopies(parseInt(e.target.value) || 1)}
                    style={{ width: '100%', padding: '10px', background: '#0f172a', border: '1px solid #334155', borderRadius: '8px', color: '#fff', fontSize: '13px' }}
                  />
                </div>
                <div>
                  <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: '#94a3b8', marginBottom: '6px' }}>Color Mode</label>
                  <select
                    value={printIsColor ? 'color' : 'bw'}
                    onChange={(e) => setPrintIsColor(e.target.value === 'color')}
                    style={{ width: '100%', padding: '10px', background: '#0f172a', border: '1px solid #334155', borderRadius: '8px', color: '#fff', fontSize: '13px' }}
                  >
                    <option value="bw">Black & White (Monochrome)</option>
                    <option value="color">Full Color</option>
                  </select>
                </div>
              </div>
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
              <button onClick={() => setPrintModalFile(null)} className="btn-secondary">Cancel</button>
              <button onClick={handleExecutePrint} className="btn-primary">Send to Printer</button>
            </div>
          </div>
        </div>
      )}

      {/* PDF CONVERT MODAL */}
      {showPdfConvertModal && (
        <div className="modal-overlay">
          <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '16px', padding: '24px', width: '100%', maxWidth: '520px' }}>
            <h3 style={{ fontSize: '18px', fontWeight: 800, marginBottom: '6px' }}>Image-to-PDF Converter</h3>
            <p style={{ fontSize: '13px', color: '#94a3b8', marginBottom: '16px' }}>
              Select paper format and combine images into one high-resolution PDF.
            </p>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '14px', marginBottom: '20px' }}>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                <div>
                  <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: '#94a3b8', marginBottom: '6px' }}>Paper Profile</label>
                  <select
                    value={pdfPaperSize}
                    onChange={(e: any) => setPdfPaperSize(e.target.value)}
                    style={{ width: '100%', padding: '10px', background: '#0f172a', border: '1px solid #334155', borderRadius: '8px', color: '#fff', fontSize: '13px' }}
                  >
                    <option value="A4">A4 (Standard Print)</option>
                    <option value="A5">A5 (Half Sheet)</option>
                    <option value="Letter">US Letter</option>
                    <option value="Legal">Legal Document</option>
                    <option value="Photo4x6">Photo 4×6 Inches</option>
                  </select>
                </div>

                <div>
                  <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: '#94a3b8', marginBottom: '6px' }}>Orientation</label>
                  <select
                    value={pdfOrientation}
                    onChange={(e: any) => setPdfOrientation(e.target.value)}
                    style={{ width: '100%', padding: '10px', background: '#0f172a', border: '1px solid #334155', borderRadius: '8px', color: '#fff', fontSize: '13px' }}
                  >
                    <option value="portrait">Portrait</option>
                    <option value="landscape">Landscape</option>
                  </select>
                </div>
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: '#94a3b8', marginBottom: '6px' }}>
                  Available Images in Inbox / Submissions:
                </label>
                <div style={{ maxHeight: '140px', overflowY: 'auto', background: '#0f172a', padding: '8px', borderRadius: '8px', border: '1px solid #334155', display: 'flex', flexDirection: 'column', gap: '4px' }}>
                  {(dashboardData?.submissions || []).flatMap((s: any) => s.files || []).filter((f: any) => ['.jpg', '.jpeg', '.png'].includes(f.file_extension)).map((f: any) => {
                    const isSelected = selectedImagesForPdf.includes(f.local_path);
                    return (
                      <div
                        key={f.id}
                        onClick={() => {
                          if (isSelected) {
                            setSelectedImagesForPdf(selectedImagesForPdf.filter(p => p !== f.local_path));
                          } else {
                            setSelectedImagesForPdf([...selectedImagesForPdf, f.local_path]);
                          }
                        }}
                        style={{
                          padding: '6px 10px', borderRadius: '6px', fontSize: '12px', cursor: 'pointer',
                          background: isSelected ? '#2563eb' : '#1e293b', color: '#fff', display: 'flex', justifyContent: 'space-between'
                        }}
                      >
                        <span>{f.original_name}</span>
                        <span>{isSelected ? '✓ Selected' : '+ Select'}</span>
                      </div>
                    );
                  })}
                </div>
              </div>
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
              <button onClick={() => setShowPdfConvertModal(false)} className="btn-secondary">Cancel</button>
              <button onClick={handleExecuteImageToPdf} className="btn-primary">Generate PDF</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

