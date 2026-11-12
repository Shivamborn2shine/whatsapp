# SCAN DROP
## Product Requirements Document — Version 3.0
### Windows Desktop Edition + SaaS Subscription Platform

**Tagline:** Scan. Drop. Done.  
**Status:** Revised implementation specification  
**Platforms:** Windows EXE, Customer Web, Founder Web Dashboard  
**Architecture:** Cloud-first synchronization, locally executed file operations  
**Initial deployment:** One shop, expandable to multiple independent shops

---

# 1. Product Vision

Scan Drop will provide shopkeepers with a complete Windows workspace for receiving customer files through QR codes, organizing documents, converting formats, opening files in external applications, and performing staff-controlled printing.

Customers will use a mobile-first web interface, while shops will use an installed Windows desktop application.

Scan Drop will be offered as subscription-based software, centrally managed by the founder.

The product must operate without WhatsApp, without mandatory customer registration, and without requiring shopkeepers to keep a browser open.

## Primary product pillars

1. Effortless customer file submission.
2. Automatic delivery to shop PCs.
3. Flexible local file management.
4. Document and PDF processing.
5. Staff-controlled printing.
6. Centralized licensing and subscriptions.
7. Secure document handling and automatic retention.

---

# 2. Application Architecture

## 2.1 Windows application components

### ScanDrop.exe

Main shopkeeper desktop application.

Responsibilities:

- Authentication
- Dashboard
- File inbox
- Submission management
- File preview
- File operations
- Conversion controls
- Printing interface
- Subscription status
- Device settings

### ScanDrop.Service.exe

Background Windows service.

Responsibilities:

- Device authentication
- Cloud connectivity
- Receiving events
- File synchronization
- Download queue
- Integrity checking
- Local storage management
- Heartbeats
- Retention cleanup
- Subscription entitlement synchronization

### ScanDrop.Converter.exe

Restricted worker process.

Responsibilities:

- Format detection
- Conversion to PDF
- PDF merging and splitting
- Image transformations
- PDF page preparation
- Controlled invocation of optional conversion providers

The converter must run with limited privileges and resource constraints.

## 2.2 Recommended Windows stack

- C# / modern supported .NET
- WPF desktop shell
- WebView2 for selected modern UI surfaces
- SQLite local operational database
- Windows Service for background synchronization
- Windows notification integration
- Windows printing APIs and installed application handlers

WebView2 enables embedded web interfaces inside WPF. The desktop shell must still validate all messages crossing between web content and privileged Windows operations.

## 2.3 Windows installation

Deliver an installer named:

`ScanDropSetup.exe`

The installer must:

- Verify operating-system compatibility.
- Install the desktop application.
- Install the background service.
- Register required application components.
- Check conversion dependencies.
- Configure startup behavior.
- Create application data directories.
- Provide clean uninstall support.

Use a standard installer framework such as WiX or Inno Setup.

For commercial distribution, sign the installer and application binaries, and provide verified update packages. Traditional EXE/MSI distribution is supported by Microsoft's Windows deployment ecosystem.

---

# 3. Customer Upload Platform

The customer web interface retains the v2.0 design.

## Requirements

- Permanent shop QR.
- Shop-branded landing page.
- No mandatory customer login.
- Optional name.
- Multiple file selection.
- Optional service selection and instructions.
- Upload progress.
- File validation.
- Malware scanning.
- Submission receipt.
- Limited customer status tracking.

## Initial supported formats

- PDF
- JPG / JPEG
- PNG
- DOCX

Additional formats must be enabled only when their processing and security requirements are implemented.

## Delivery model

Customer → Private Cloud Storage → Scan Drop Windows Service → Local Shop Inbox.

Cloud receiving remains available when the PC is offline, subject to the 24-hour retention deadline.

No customer uploads may directly execute or automatically open files on Windows.

---

# 4. Desktop Workspace

## 4.1 Primary navigation

The Windows application must include:

### Inbox

New and pending customer submissions.

### Processing

Submissions currently being worked on.

### Completed

Completed submissions, while their files remain within retention.

### File Manager

Local files associated with accepted submissions.

### PDF Tools

Document conversion and manipulation.

### Printers

Connected printers and manual print settings.

### History

Operational records and job history, without retaining expired document contents.

### Settings

Shop settings, paths, application preferences, devices, and notifications.

### Subscription

Current plan, renewal status, entitlement usage, and account information.

## 4.2 Submission organization

Each submission must remain grouped by receipt ID.

Display:

- Receipt ID
- Optional customer name
- File count
- File size
- File types
- Instructions
- Service category
- Received time
- Remaining retention time
- Local sync status
- Processing status

Allow staff to open a submission and inspect each individual file.

## 4.3 File actions

Supported actions:

- Preview
- Open
- Open With
- Print
- Convert to PDF
- Merge
- Split
- Rotate
- Crop
- Resize
- Rename display label
- Copy
- Export
- Show in Explorer
- Mark completed

Actions must be context-aware.

For instance, image-cropping tools should appear for supported images, not arbitrary Word files.

## 4.4 External application integration

Implement Windows application association support.

Examples:

- DOCX → Microsoft Word / LibreOffice Writer
- PDF → Edge / Adobe Acrobat / other installed PDF reader
- JPG / PNG → Photos / Photoshop / Paint
- Other permitted formats → Compatible installed applications

Allow shopkeepers to:

- Use the Windows default application.
- Select another installed application.
- Set preferred applications for particular file types.
- Open files in their original applications for editing.

Only validated files may be opened. Show a warning when opening potentially active-content documents in external software.

Do not execute arbitrary application paths supplied by customers.

## 4.5 Editing and working copies

Original received files must be preserved under application control.

When editing through another application:

1. Create a working copy.
2. Open the copy using the selected application.
3. Allow the user to save changes normally.
4. Detect the revised file where supported.
5. Offer to register it as a derived output.

Do not automatically overwrite the original.

---

# 5. Document Processing Engine

## 5.1 Architecture

Build a modular conversion system with independent adapters.

Proposed interfaces:

- `IConversionProvider`
- `IImageProcessor`
- `IPdfProcessor`
- `IConversionJobQueue`
- `IConversionResultStore`

Each operation must return:

- Job ID
- Source file IDs
- Requested operation
- Output file path
- Output format
- Execution status
- Error details
- Processing duration

## 5.2 Image-to-PDF engine

Supported features:

- Convert a single image to PDF.
- Combine multiple images into one PDF.
- Rearrange image order.
- Rotate images.
- Crop images.
- Fit images to paper.
- Preserve or choose aspect ratio.
- Control margins.
- Select output page size.
- Select portrait or landscape.
- Generate high-quality PDFs for printing.

Paper profiles:

- A4
- A5
- Letter
- Legal
- 4×6 inches
- Custom dimensions

Provide sensible defaults without forcing staff to configure every parameter.

## 5.3 Office-to-PDF engine

Initial conversion:

- DOCX → PDF

Future:

- DOC → PDF
- XLSX → PDF
- PPTX → PDF
- ODT → PDF
- HTML → PDF, through a controlled rendering process

Prefer a locally installed document-rendering engine for conversions.

Potential implementation: LibreOffice headless conversion.

The integration must:

- Detect whether its supported engine is available.
- Display a clear dependency warning when missing.
- Isolate conversion processes.
- Disable macros and external content.
- Enforce timeouts.
- Prevent unauthorized network access.
- Handle malformed documents.
- Retain the source document.

Conversion accuracy must be tested, particularly for fonts, page breaks, tables, and image placement.

## 5.4 PDF manipulation tools

MVP:

- Merge PDFs
- Split PDFs
- Extract pages
- Rotate pages
- Reorder pages
- Delete selected pages from a derived copy
- Insert blank pages
- Convert images to PDF

Future:

- Compress PDF
- Add page numbers
- Add watermarks
- Extract embedded images
- OCR scanned documents
- Repair supported malformed PDFs

OCR is optional and should be performed locally with explicit staff action.

## 5.5 Batch processing

Allow staff to select multiple files and apply one supported operation.

Examples:

- Convert five images to one PDF.
- Convert three DOCX files into separate PDFs.
- Merge several PDFs in a chosen order.
- Resize multiple images to the same paper dimensions.

Display per-file progress and clear failure messages.

## 5.6 Processing safety

All conversions must run outside the main desktop UI process.

Apply:

- Worker isolation
- CPU limits
- Memory limits
- Execution timeouts
- Output size limits
- Input type validation
- Temporary directory isolation
- Controlled file access
- Cancellation and failure recovery

The converter must not have access to subscription administration credentials.

---

# 6. Manual Printing Workspace

## 6.1 Printing policy

Automatic printing remains disabled.

The shopkeeper explicitly initiates all print operations.

## 6.2 Printer discovery

Display Windows-installed printers and relevant details:

- Printer name
- Driver
- Default status
- Connection type, when available
- Paper settings
- Supported color modes, where reported
- Printer queue
- Availability reported by Windows

The software should not assume that all printers expose identical capabilities.

## 6.3 Print interface

Provide:

- Printer selection
- Copies
- Color / Black & White
- Paper size
- Page orientation
- Page range
- Single-sided / Duplex, where supported
- Pages per sheet
- Scale / Fit to page
- Preview when available

Printing behavior must respect the selected printer's capabilities.

## 6.4 Print strategies

### Strategy A — Open With and Print

Open the document in a compatible installed application and let staff use its print dialog.

### Strategy B — PDF Print

Convert the document to PDF, display a preview, and initiate staff-approved printing through a selected supported renderer.

### Strategy C — Image Printing

Allow image-specific print layouts, including paper sizing and multiple images per sheet.

## 6.5 Printer profiles

Allow optional saved presets:

- A4 Black & White
- A4 Color
- Photo 4×6
- Legal Document
- Two Pages per Sheet

Presets must never silently trigger printing.

---

# 7. File Management and Convenience Features

## 7.1 Smart local organization

Use submission-based directories:

```text
ScanDrop/
  Incoming/
    SD-1042/
    SD-1043/
  Processing/
  Completed/
  Derived/
  Temporary/
  Quarantine/
```

Maintain a local database mapping each file to its submission and current location.

## 7.2 Bulk actions

Support:

- Select all files in a submission.
- Export selected files.
- Convert multiple files.
- Merge compatible PDFs.
- Move submissions between workflow statuses.
- Open a customer-specific folder.

## 7.3 Drag and drop

Support dragging compatible files:

- Into the converter.
- Into PDF merge tools.
- Into local document preparation areas.

Customer-submitted originals must remain protected from accidental overwriting.

## 7.4 Keyboard shortcuts

Suggested:

- Ctrl+F — Search
- Ctrl+O — Open selected file
- Ctrl+P — Open print dialog
- Ctrl+Shift+C — Convert to PDF
- Ctrl+E — Export
- F5 — Refresh/reconcile
- Escape — Close preview

## 7.5 File preview

Provide supported previews for:

- Images
- PDFs
- Documents through a safe preview/conversion process

Avoid executing active document content inside the preview.

## 7.6 Notifications

Display Windows notifications for:

- New submission
- Download completion
- Download failure
- File processing failure
- Receiver offline
- Subscription nearing expiry
- Required application update

Notifications must respect privacy settings.

---

# 8. Founder Super Admin Platform

## 8.1 Purpose

A founder-controlled SaaS management dashboard independent of the shopkeeper application.

Suggested route:

`/superadmin`

This route must require privileged server-side authorization.

## 8.2 Shop management

Each shop record includes:

- Shop ID
- Shop name
- Owner/contact details
- Assigned subscription plan
- Subscription dates
- Active device count
- Device limits
- Receiving status
- Application version
- Last heartbeat
- Usage summary

## 8.3 License management

Founder actions:

- Create a shop
- Activate a trial
- Assign a paid plan
- Renew a subscription
- Extend a subscription
- Revoke a license
- Suspend a shop
- Reactivate a shop
- Allow a device transfer
- Reset device pairing
- View entitlement usage

All actions must be audited.

## 8.4 Subscription plans

Proposed commercial structure, subject to pricing validation:

### Free / Trial

- Limited file receiving
- Limited monthly usage
- One Windows device
- Basic dashboard
- Basic PDF conversion

### Pro

- Higher monthly limits
- Full file handling
- Extended PDF toolbox
- Batch processing
- Printer presets
- Advanced notifications
- Higher operational quotas

### Business

- Multiple PCs
- Multiple staff accounts
- Advanced device management
- Shop analytics
- Priority support
- Higher capacity limits

Do not equate a paid plan with unlimited storage or unrestricted uploads.

## 8.5 Subscription entitlements

Maintain centrally controlled entitlements, such as:

- `max_devices`
- `max_monthly_submissions`
- `max_monthly_upload_bytes`
- `max_file_size`
- `batch_conversion_enabled`
- `pdf_advanced_enabled`
- `printer_profiles_enabled`
- `multi_staff_enabled`
- `receiving_enabled`

The backend is the authority for entitlements.

The desktop application must not be able to grant itself paid features through local configuration edits.

## 8.6 Activation flow

1. Shopkeeper installs the software.
2. Opens Scan Drop.
3. Signs in or enters an authorized pairing code.
4. Selects the assigned shop.
5. Device registers with the backend.
6. Backend checks subscription and device limits.
7. Backend issues a device-scoped credential.
8. Application downloads current entitlements.
9. Receiver begins synchronization.

Avoid using hardware fingerprints as the sole device identity.

## 8.7 License verification

Use:

- Server-side subscription records.
- Device-specific credentials.
- Short-lived signed entitlement snapshots for limited offline UI use.
- Regular online refresh.
- Server-side enforcement of cloud operations.

Suggested configurable offline grace period: 72 hours for local licensed features.

This grace period does not authorize new cloud uploads beyond the server-enforced subscription state.

## 8.8 Expired subscription behavior

At expiry:

- Notify shopkeeper before expiration.
- Disable new customer uploads if receiving entitlement expires.
- Preserve access to still-valid local submissions during the permitted retention window.
- Allow local export and privacy cleanup.
- Maintain necessary deletion jobs.
- Permit renewal and account recovery.

Never delete customer files immediately as a subscription penalty.

Never block privacy cleanup merely because a subscription expired.

## 8.9 Manual and automated billing

Initial MVP:

- Founder creates subscription.
- Founder records payment externally.
- Founder activates or renews the license.
- System logs payment reference and subscription dates.

Future:

- Supported payment gateway integration
- Webhook verification
- Automated renewals
- Invoice generation
- Payment failure notifications
- Subscription cancellation

Never trust a payment success message from the client without server-side verification.

---

# 9. Device and Update Management

## 9.1 Device inventory

Track:

- Device ID
- Shop ID
- Device name
- Windows version
- Scan Drop version
- Last heartbeat
- Online/offline status
- Receiver version
- License status
- Sync health

Do not collect arbitrary personal files or unrelated device information.

## 9.2 Software updates

Implement:

- Versioned release channels
- Update notifications
- Signed update packages
- Integrity verification
- Safe restart
- Rollback or recovery strategy
- Mandatory security update capability

Avoid silently replacing executable files without verification.

## 9.3 Remote administration

Founder may:

- View health
- View non-sensitive diagnostics
- Revoke a device
- Require an update
- Modify subscriptions
- Change cloud-side receiving entitlement

Founder must not have unrestricted remote control of the shop PC or customer files.

---

# 10. File Transfer and Tunnel Strategy

## 10.1 MVP transfer method

Use cloud storage and the Windows background receiver.

Customers upload to private cloud storage.

The receiver uses authenticated outbound HTTPS connections to fetch files.

## 10.2 Why not require Cloudflare Tunnel?

A tunnel requires the shop PC and the relevant local service to remain online during a direct transfer.

Cloud receiving provides a durable temporary queue and removes the need for a public local receiving endpoint.

## 10.3 Optional tunnel compatibility

Retain the ability to add a Cloudflare Tunnel adapter in a later phase.

Requirements:

- Explicit device authorization
- Authenticated transfer sessions
- Strong access controls
- Validation before file availability
- No unrestricted local file access
- Benchmark-based performance assessment
- Secure fallback to cloud mode

The permanent customer QR must not contain a temporary tunnel address.

---

# 11. Database Expansion

Retain the previous core tables:

- `shops`
- `staff_members`
- `submissions`
- `submission_files`
- `receiver_devices`
- `file_deliveries`
- `receipt_access`
- `notifications`
- `cleanup_jobs`
- `audit_events`

Add:

### `subscription_plans`

Plan name, billing interval, limits, and feature entitlements.

### `shop_subscriptions`

Shop ID, plan ID, status, start date, renewal date, expiry date, and cancellation state.

### `subscription_payments`

Shop, subscription, amount, currency, payment reference, status, and timestamps.

### `device_entitlements`

Device assignment, entitlement version, permitted capabilities, credential status, and expiry.

### `conversion_jobs`

Submission/file references, conversion type, status, duration, and result.

### `derived_files`

Original file reference, output format, storage location, creation time, and expiry.

### `printer_profiles`

Shop/device reference, printer identity, settings, and preset name.

### `app_releases`

Version, release channel, package checksum, signature information, and minimum supported version.

Cloud and local-only records must be distinguished explicitly.

---

# 12. Data Retention

## Default policy

Customer submission files expire 24 hours after successful submission acceptance.

This applies to managed cloud and local files.

## Derived files

Converted files, previews, thumbnails, and managed temporary outputs inherit the source submission's expiry by default.

Creating a PDF must not silently restart the 24-hour retention clock.

## Exports

Files deliberately exported outside Scan Drop's managed storage may remain on the shop PC.

The software must explain that exported and externally edited copies are not covered by automatic cleanup.

## Deletion

At expiry:

- Block further authorized retrieval.
- Delete managed cloud objects.
- Schedule local deletion.
- Remove managed previews and derived files.
- Record success or failure.
- Retry deletion when a receiver reconnects.

Subscription expiration must not interrupt these obligations.

---

# 13. Security Requirements

The desktop product has additional attack surfaces compared with the earlier web-only design.

Mandatory requirements:

- Signed software packages
- Secure device pairing
- OS-protected device credentials
- Private storage
- Role-based authorization
- Malware scanning
- Safe preview architecture
- Restricted conversion workers
- No automatic execution of uploaded files
- Validated paths
- Safe application launching
- Network and filesystem restrictions
- Rate limiting
- Audit records
- Remote license revocation
- Verified software updates

Conversion workers must treat documents as potentially hostile, not as trusted input simply because the cloud scanner accepted them.

---

# 14. Recommended Implementation Roadmap

## Phase 1 — Desktop Foundation

- Windows shell
- Installer
- Staff login
- Shop pairing
- Background receiver service
- Local SQLite state
- Subscription verification
- Local settings and application logging

## Phase 2 — Complete File Receiving

- Customer QR upload
- Secure storage
- Cloud submission records
- Automatic Windows downloads
- Live inbox
- Windows notifications
- Offline recovery

## Phase 3 — Desktop File Operations

- Preview
- Open With
- Explorer integration
- Drag and drop
- Manual printing
- Copy/export
- Submission workflow statuses

## Phase 4 — PDF Engine

- Image-to-PDF
- DOCX-to-PDF
- Merge PDFs
- Split PDFs
- Rotate/reorder pages
- Page size selection
- Batch conversion
- Restricted worker execution

## Phase 5 — Subscription SaaS

- Founder Super Admin
- Plans
- Trial activation
- Subscription dates
- Device limits
- Manual renewal
- Feature entitlements
- License validation
- Audit history

Build the foundational shop/device/entitlement tables in Phase 1, even if the full founder UI arrives in Phase 5.

## Phase 6 — Production Hardening

- Security testing
- Malware scanning validation
- Retention enforcement
- Cleanup failure recovery
- Device revocation tests
- Installer signing
- Auto-update security
- Performance and load testing

Production rollout is not permitted until the relevant security, retention, and recovery requirements are implemented.

## Phase 7 — Advanced Convenience

- Advanced PDF compression
- OCR
- Printer profiles
- Image layout templates
- Advanced batch operations
- Improved diagnostics
- Multi-device support
- Optional PrintAway integration

## Phase 8 — Multi-Shop Commercial Rollout

- Public registration and onboarding
- Automated billing
- Shop-specific plan management
- Support portal
- Multiple staff accounts
- Usage reporting
- Subscription analytics
- Founder revenue dashboard

---

# 15. Acceptance Criteria

The product is ready for its first commercial pilot when:

1. A shop installs Scan Drop through an EXE installer.
2. The shop pairs its device to a centrally controlled account.
3. The subscription can be activated, renewed, or revoked from the founder dashboard.
4. The Windows receiver runs without an open browser.
5. Customer files arrive from the permanent QR website.
6. Each submission appears in the desktop inbox.
7. Files download automatically and pass integrity verification.
8. Staff receive desktop notifications.
9. Staff can preview files.
10. Staff can open supported files in compatible installed applications.
11. Staff can convert images and DOCX files to PDF.
12. Staff can merge and split PDFs.
13. Staff can initiate printing manually.
14. Original files remain preserved.
15. Failed conversions and downloads are recoverable.
16. Expiry and deletion work independently of subscription status.
17. A revoked device cannot obtain new cloud files.
18. Application binaries and updates are verified.
19. Staff can manage files without WhatsApp.
20. The founder can manage shops without access to private customer documents.

---

# 16. Final Product Identity

**Scan Drop**

**Scan. Drop. Done.**

A subscription-powered Windows desktop workspace that connects customer phones to shop computers and makes received files immediately useful.

The platform must handle the technical work of receiving, organizing, converting, and preparing documents while keeping shopkeepers in control of editing and printing.
