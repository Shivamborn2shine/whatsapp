import readline from 'node:readline';
import { ConverterEngine } from './converterEngine';

interface ConversionRequest {
  jobId: string;
  operation: 'image_to_pdf' | 'merge_pdf' | 'split_pdf' | 'rotate_pdf';
  inputPaths: string[];
  outputPath: string;
  options?: any;
}

const rl = readline.createInterface({
  input: process.stdin,
  output: process.stdout,
  terminal: false
});

console.error('[ScanDrop.Converter] Isolated Worker process started. Waiting for jobs.');

rl.on('line', async (line) => {
  if (!line.trim()) return;

  try {
    const req: ConversionRequest = JSON.parse(line);
    const startTime = Date.now();

    let result: any = null;

    if (req.operation === 'image_to_pdf') {
      result = await ConverterEngine.convertImagesToPdf(req.inputPaths, req.outputPath, req.options);
    } else if (req.operation === 'merge_pdf') {
      result = await ConverterEngine.mergePdfs(req.inputPaths, req.outputPath);
    } else if (req.operation === 'split_pdf') {
      result = await ConverterEngine.splitPdf(req.inputPaths[0], req.outputPath, req.options?.prefix);
    } else if (req.operation === 'rotate_pdf') {
      result = await ConverterEngine.rotatePdfPages(req.inputPaths[0], req.outputPath, req.options?.angle, req.options?.pageIndices);
    } else {
      throw new Error(`Unsupported conversion operation: ${req.operation}`);
    }

    const durationMs = Date.now() - startTime;
    console.log(JSON.stringify({
      jobId: req.jobId,
      success: true,
      result,
      durationMs
    }));
  } catch (err: any) {
    console.log(JSON.stringify({
      success: false,
      error: err.message || 'Conversion failed'
    }));
  }
});

