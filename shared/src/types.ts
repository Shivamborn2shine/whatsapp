export type SubscriptionTier = 'free' | 'pro' | 'business';
export type SubscriptionStatus = 'trial' | 'active' | 'suspended' | 'expired' | 'revoked';
export type SubmissionStatus = 'received' | 'downloading' | 'synced' | 'processing' | 'completed' | 'expired';
export type DeviceStatus = 'paired' | 'active' | 'offline' | 'revoked';
export type ConversionType = 'image_to_pdf' | 'docx_to_pdf' | 'merge_pdf' | 'split_pdf' | 'rotate_pdf' | 'crop_image';
export type ConversionStatus = 'pending' | 'processing' | 'completed' | 'failed';

export interface ShopEntitlements {
  max_devices: number;
  max_monthly_submissions: number;
  max_monthly_upload_bytes: number;
  max_file_size: number;
  batch_conversion_enabled: boolean;
  pdf_advanced_enabled: boolean;
  printer_profiles_enabled: boolean;
  multi_staff_enabled: boolean;
  receiving_enabled: boolean;
}

export interface Shop {
  id: string; // e.g., 'shop_abc123'
  slug: string; // e.g., 'quickprint-main'
  name: string;
  tagline?: string;
  ownerName: string;
  ownerEmail: string;
  ownerPhone?: string;
  pairingCode?: string;
  pairingCodeExpiresAt?: string;
  createdAt: string;
  updatedAt: string;
}

export interface SubscriptionPlan {
  id: string;
  name: string;
  tier: SubscriptionTier;
  billingInterval: 'month' | 'year' | 'lifetime';
  priceCents: number;
  currency: string;
  entitlements: ShopEntitlements;
}

export interface ShopSubscription {
  id: string;
  shopId: string;
  planId: string;
  status: SubscriptionStatus;
  startDate: string;
  renewalDate: string;
  expiryDate: string;
  isAutoRenew: boolean;
  notes?: string;
  createdAt: string;
  updatedAt: string;
}

export interface ReceiverDevice {
  id: string;
  shopId: string;
  deviceName: string;
  windowsVersion?: string;
  appVersion: string;
  status: DeviceStatus;
  deviceToken?: string;
  lastHeartbeatAt: string;
  lastIp?: string;
  createdAt: string;
}

export interface CustomerSubmission {
  id: string; // Receipt ID e.g. 'SD-1042'
  shopId: string;
  customerName?: string;
  serviceCategory?: string; // e.g. 'B&W Print', 'Color Print', 'Scan', 'Lamination'
  instructions?: string;
  fileCount: number;
  totalSizeBytes: number;
  status: SubmissionStatus;
  expiresAt: string; // 24 hours from creation
  createdAt: string;
  updatedAt: string;
}

export interface SubmissionFile {
  id: string;
  submissionId: string;
  shopId: string;
  originalName: string;
  mimeType: string;
  fileExtension: string;
  sizeBytes: number;
  sha256Hash: string;
  storagePath: string; // Cloud path or local relative path
  localPath?: string;
  status: 'pending' | 'synced' | 'quarantined' | 'deleted';
  createdAt: string;
  expiresAt: string;
}

export interface ConversionJob {
  id: string;
  shopId: string;
  submissionId?: string;
  sourceFileIds: string[];
  operation: ConversionType;
  options?: Record<string, any>;
  outputFilePath?: string;
  outputFormat: string;
  status: ConversionStatus;
  errorDetails?: string;
  durationMs?: number;
  createdAt: string;
  completedAt?: string;
}

export interface DerivedFile {
  id: string;
  shopId: string;
  originalFileId?: string;
  submissionId?: string;
  displayName: string;
  outputFormat: string;
  localPath: string;
  sizeBytes: number;
  createdAt: string;
  expiresAt: string; // Inherits submission expiry
}

export interface PrinterProfile {
  id: string;
  shopId: string;
  deviceId: string;
  profileName: string;
  printerName: string;
  isColor: boolean;
  paperSize: string; // 'A4' | 'A5' | 'Letter' | 'Legal' | 'Photo 4x6'
  orientation: 'portrait' | 'landscape';
  duplex: 'simplex' | 'duplexLong' | 'duplexShort';
  pagesPerSheet: number;
  scaleMode: 'fit' | 'actual' | 'shrink';
  createdAt: string;
}

export interface AuditEvent {
  id: string;
  shopId?: string;
  actorType: 'founder' | 'shop_staff' | 'customer' | 'system';
  actorId?: string;
  eventType: string;
  description: string;
  metadata?: Record<string, any>;
  createdAt: string;
}
