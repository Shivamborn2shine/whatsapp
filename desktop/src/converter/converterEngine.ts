import fs from 'node:fs';
import path from 'node:path';
import { PDFDocument, rgb, degrees, PageSizes } from 'pdf-lib';

export interface ImageToPdfOptions {
  paperSize?: 'A4' | 'A5' | 'Letter' | 'Legal' | 'Photo4x6';
  orientation?: 'portrait' | 'landscape';
  marginPt?: number;
  fitToPage?: boolean;
}

const PAPER_DIMENSIONS: Record<string, [number, number]> = {
  A4: PageSizes.A4, // [595.28, 841.89]
  A5: PageSizes.A5, // [419.53, 595.28]
  Letter: PageSizes.Letter, // [612.0, 792.0]
  Legal: [612.0, 1008.0],
  Photo4x6: [288.0, 432.0] // 4x6 inches @ 72pt/inch
};

export class ConverterEngine {
  /**
   * Convert one or multiple images into a single polished PDF document
   */
  static async convertImagesToPdf(
    imagePaths: string[],
    outputPath: string,
    options: ImageToPdfOptions = {}
  ): Promise<{ pageCount: number; outputPath: string }> {
    const pdfDoc = await PDFDocument.create();
    const paperName = options.paperSize || 'A4';
    let [baseWidth, baseHeight] = PAPER_DIMENSIONS[paperName] || PageSizes.A4;

    if (options.orientation === 'landscape') {
      const temp = baseWidth;
      baseWidth = baseHeight;
      baseHeight = temp;
    }

    const margin = options.marginPt !== undefined ? options.marginPt : 20;
    const printableWidth = baseWidth - margin * 2;
    const printableHeight = baseHeight - margin * 2;

    for (const imgPath of imagePaths) {
      if (!fs.existsSync(imgPath)) {
        throw new Error(`Source image not found: ${imgPath}`);
      }

      const imgBytes = fs.readFileSync(imgPath);
      const ext = path.extname(imgPath).toLowerCase();
      let embeddedImage;

      if (ext === '.png') {
        embeddedImage = await pdfDoc.embedPng(imgBytes);
      } else if (ext === '.jpg' || ext === '.jpeg') {
        embeddedImage = await pdfDoc.embedJpg(imgBytes);
      } else {
        throw new Error(`Unsupported image format: ${ext}. Supported: PNG, JPG, JPEG.`);
      }

      const page = pdfDoc.addPage([baseWidth, baseHeight]);
      const imgWidth = embeddedImage.width;
      const imgHeight = embeddedImage.height;

      let drawWidth = printableWidth;
      let drawHeight = printableHeight;

      if (options.fitToPage !== false) {
        // Calculate aspect-ratio fitted dimensions
        const scaleFactor = Math.min(printableWidth / imgWidth, printableHeight / imgHeight);
        drawWidth = imgWidth * scaleFactor;
        drawHeight = imgHeight * scaleFactor;
      }

      // Center the image on the page with margins
      const posX = margin + (printableWidth - drawWidth) / 2;
      const posY = margin + (printableHeight - drawHeight) / 2;

      page.drawImage(embeddedImage, {
        x: posX,
        y: posY,
        width: drawWidth,
        height: drawHeight
      });
    }

    const pdfBytes = await pdfDoc.save();
    fs.writeFileSync(outputPath, pdfBytes);

    return {
      pageCount: imagePaths.length,
      outputPath
    };
  }

  /**
   * Merge multiple PDF files into a single ordered PDF document
   */
  static async mergePdfs(pdfPaths: string[], outputPath: string): Promise<{ pageCount: number; outputPath: string }> {
    if (pdfPaths.length === 0) {
      throw new Error('At least one PDF file is required to merge');
    }

    const mergedPdf = await PDFDocument.create();
    let totalPages = 0;

    for (const pdfPath of pdfPaths) {
      if (!fs.existsSync(pdfPath)) {
        throw new Error(`PDF file not found: ${pdfPath}`);
      }
      const pdfBytes = fs.readFileSync(pdfPath);
      const srcDoc = await PDFDocument.load(pdfBytes, { ignoreEncryption: true });
      const copiedPages = await mergedPdf.copyPages(srcDoc, srcDoc.getPageIndices());
      for (const page of copiedPages) {
        mergedPdf.addPage(page);
        totalPages++;
      }
    }

    const mergedBytes = await mergedPdf.save();
    fs.writeFileSync(outputPath, mergedBytes);

    return {
      pageCount: totalPages,
      outputPath
    };
  }

  /**
   * Split a PDF into individual pages or extract page ranges
   */
  static async splitPdf(
    srcPdfPath: string,
    outputDir: string,
    prefix: string = 'page'
  ): Promise<string[]> {
    if (!fs.existsSync(srcPdfPath)) {
      throw new Error(`Source PDF not found: ${srcPdfPath}`);
    }

    if (!fs.existsSync(outputDir)) {
      fs.mkdirSync(outputDir, { recursive: true });
    }

    const srcBytes = fs.readFileSync(srcPdfPath);
    const srcDoc = await PDFDocument.load(srcBytes, { ignoreEncryption: true });
    const pageCount = srcDoc.getPageCount();
    const generatedFiles: string[] = [];

    for (let i = 0; i < pageCount; i++) {
      const newDoc = await PDFDocument.create();
      const [copiedPage] = await newDoc.copyPages(srcDoc, [i]);
      newDoc.addPage(copiedPage);

      const outName = `${prefix}_${i + 1}.pdf`;
      const outPath = path.join(outputDir, outName);
      const outBytes = await newDoc.save();
      fs.writeFileSync(outPath, outBytes);
      generatedFiles.push(outPath);
    }

    return generatedFiles;
  }

  /**
   * Rotate pages of a PDF (90, 180, 270 degrees)
   */
  static async rotatePdfPages(
    srcPdfPath: string,
    outputPath: string,
    angleDegrees: 90 | 180 | 270 = 90,
    pageIndices?: number[]
  ): Promise<{ pageCount: number; outputPath: string }> {
    const srcBytes = fs.readFileSync(srcPdfPath);
    const pdfDoc = await PDFDocument.load(srcBytes);
    const pages = pdfDoc.getPages();

    const targetIndices = pageIndices || pages.map((_, i) => i);
    for (const idx of targetIndices) {
      if (pages[idx]) {
        const currentAngle = pages[idx].getRotation().angle;
        pages[idx].setRotation(degrees((currentAngle + angleDegrees) % 360));
      }
    }

    const modifiedBytes = await pdfDoc.save();
    fs.writeFileSync(outputPath, modifiedBytes);

    return {
      pageCount: pages.length,
      outputPath
    };
  }
}

