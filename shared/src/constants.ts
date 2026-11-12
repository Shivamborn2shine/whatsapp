export const ALLOWED_MIME_TYPES: Record<string, string[]> = {
  pdf: ['application/pdf'],
  jpg: ['image/jpeg'],
  jpeg: ['image/jpeg'],
  png: ['image/png'],
  docx: ['application/vnd.openxmlformats-officedocument.wordprocessingml.document']
};

export const RETENTION_HOURS = 24;
export const OFFLINE_GRACE_PERIOD_HOURS = 72;

export const DEFAULT_ENTITLEMENTS = {
  free: {
    max_devices: 1,
    max_monthly_submissions: 50,
    max_monthly_upload_bytes: 100 * 1024 * 1024, // 100 MB
    max_file_size: 15 * 1024 * 1024, // 15 MB
    batch_conversion_enabled: false,
    pdf_advanced_enabled: false,
    printer_profiles_enabled: false,
    multi_staff_enabled: false,
    receiving_enabled: true
  },
  pro: {
    max_devices: 1,
    max_monthly_submissions: 1000,
    max_monthly_upload_bytes: 5 * 1024 * 1024 * 1024, // 5 GB
    max_file_size: 50 * 1024 * 1024, // 50 MB
    batch_conversion_enabled: true,
    pdf_advanced_enabled: true,
    printer_profiles_enabled: true,
    multi_staff_enabled: false,
    receiving_enabled: true
  },
  business: {
    max_devices: 5,
    max_monthly_submissions: 10000,
    max_monthly_upload_bytes: 25 * 1024 * 1024 * 1024, // 25 GB
    max_file_size: 100 * 1024 * 1024, // 100 MB
    batch_conversion_enabled: true,
    pdf_advanced_enabled: true,
    printer_profiles_enabled: true,
    multi_staff_enabled: true,
    receiving_enabled: true
  }
};
