import {PDFDocument, PDFFont, PDFPage, rgb, StandardFonts} from 'pdf-lib';
import * as QRCode from 'qrcode';
import {TailBiteLocationNode} from '../../../../models';
import {ancestorsAndSelf, nodeMap} from '../../shared/tail-bite-tree';

/** One label: the QR token, the location name in large type, and up to two parent names under it. */
export interface TailBiteQrLabel {
  qrCode: string;
  title: string;
  lines: string[];
}

const A4: [number, number] = [595.28, 841.89];
const [PAGE_W, PAGE_H] = A4;
const MARGIN = 28;
const GAP = 10;
const COLS = 2;
const ROWS = 4;
const HEADER = 30;
const QUIET_ZONE = 4; // modules of white around the code, as the QR standard asks
const HEADER_SIZE = 10;
const TITLE_SIZE = 24;
const TITLE_MIN_SIZE = 14;
const LINE_SIZE = 11;
const LINE_GAP = 18;
const QR_INSET = 8; // QR code from the cell's left edge
const TEXT_INSET = 16; // text from the QR code's right edge
const TEXT_PAD = 24; // total horizontal room the QR code and padding take from the text column
const QR_VERTICAL_PAD = 24;
const QR_MAX = 150;
export const LABELS_PER_PAGE = COLS * ROWS;

/**
 * Labels for the given locations, in the given order. Lines name the parents, nearest first, up to two and never
 * the property root; only a location directly under the root (or the root itself) falls back to the root's name.
 */
export function qrLabels(selected: TailBiteLocationNode[], allNodes: TailBiteLocationNode[]): TailBiteQrLabel[] {
  const byId = nodeMap(allNodes);
  return selected.map((n) => {
    const chain = ancestorsAndSelf(n.id, byId);
    const ancestors = chain.slice(0, -1);
    const parents = ancestors.filter((a) => a.parentId !== null).reverse();
    let lines: string[] = [];
    if (parents.length) {
      lines = parents.slice(0, 2).map((p) => p.name);
    } else if (ancestors.length) {
      lines = [ancestors[0].name];
    }
    return {qrCode: n.qrCode, title: n.name, lines};
  });
}

/**
 * Text the standard PDF font can draw. Helvetica covers WinAnsi (Latin-1 incl. æ ø å ä ö ü ß); anything else
 * (emoji, other scripts) becomes "?" instead of making pdf-lib throw and the whole sheet fail.
 */
export function pdfSafeText(text: string, font: PDFFont): string {
  return Array.from(text)
    .map((ch) => {
      try {
        font.encodeText(ch);
        return ch;
      } catch {
        return '?';
      }
    })
    .join('');
}

/** Shortens `text` and appends an ellipsis, measured with it, until it fits `maxWidth` at `size`. */
function fit(text: string, font: PDFFont, size: number, maxWidth: number): string {
  if (font.widthOfTextAtSize(text, size) <= maxWidth) {
    return text;
  }
  let t = text;
  while (t.length > 0 && font.widthOfTextAtSize(`${t}…`, size) > maxWidth) {
    t = t.slice(0, -1);
  }
  return `${t}…`;
}

function safeFit(text: string, font: PDFFont, size: number, maxWidth: number): string {
  return fit(pdfSafeText(text, font), font, size, maxWidth);
}

/** The title at the largest size from TITLE_SIZE down to TITLE_MIN_SIZE that fits; truncated only at the minimum. */
export function fitTitle(text: string, font: PDFFont, maxWidth: number): {text: string; size: number} {
  const safe = pdfSafeText(text, font);
  let size = TITLE_SIZE;
  while (size > TITLE_MIN_SIZE && font.widthOfTextAtSize(safe, size) > maxWidth) {
    size -= 1;
  }
  return {text: fit(safe, font, size, maxWidth), size};
}

function drawQr(page: PDFPage, token: string, x: number, yTop: number, size: number): void {
  const modules = QRCode.create(token, {errorCorrectionLevel: 'M'}).modules;
  const count = modules.size + QUIET_ZONE * 2;
  const unit = size / count;
  for (let r = 0; r < modules.size; r++) {
    for (let c = 0; c < modules.size; c++) {
      if (modules.get(r, c)) {
        page.drawRectangle({
          x: x + (c + QUIET_ZONE) * unit,
          y: yTop - (r + QUIET_ZONE + 1) * unit,
          width: unit,
          height: unit,
          color: rgb(0, 0, 0),
        });
      }
    }
  }
}

/**
 * An A4 PDF with eight labels per page (2 × 4), each with a thin cut line. The QR codes are drawn as vector
 * squares from the raw token, so they stay sharp on any printer. `header` is printed at the top of every page.
 */
export async function buildQrSheetPdf(labels: TailBiteQrLabel[], header: string): Promise<Uint8Array> {
  if (labels.length === 0) {
    throw new Error('No labels to print.');
  }
  const doc = await PDFDocument.create();
  const regular = await doc.embedFont(StandardFonts.Helvetica);
  const bold = await doc.embedFont(StandardFonts.HelveticaBold);
  const cellW = (PAGE_W - 2 * MARGIN - (COLS - 1) * GAP) / COLS;
  const cellH = (PAGE_H - 2 * MARGIN - HEADER - (ROWS - 1) * GAP) / ROWS;
  const qrSize = Math.min(cellH - QR_VERTICAL_PAD, QR_MAX);
  const textW = cellW - qrSize - TEXT_PAD;
  for (let start = 0; start < labels.length; start += LABELS_PER_PAGE) {
    const page = doc.addPage(A4);
    page.drawText(safeFit(header, regular, HEADER_SIZE, PAGE_W - 2 * MARGIN), {
      x: MARGIN, y: PAGE_H - MARGIN - HEADER_SIZE, size: HEADER_SIZE, font: regular, color: rgb(0.2, 0.2, 0.2),
    });
    labels.slice(start, start + LABELS_PER_PAGE).forEach((label, i) => {
      const x = MARGIN + (i % COLS) * (cellW + GAP);
      const yTop = PAGE_H - MARGIN - HEADER - Math.floor(i / COLS) * (cellH + GAP);
      page.drawRectangle({x, y: yTop - cellH, width: cellW, height: cellH, borderColor: rgb(0.6, 0.6, 0.6), borderWidth: 0.5});
      drawQr(page, label.qrCode, x + QR_INSET, yTop - (cellH - qrSize) / 2, qrSize);
      const textX = x + TEXT_INSET + qrSize;
      let y = yTop - cellH / 2 + 12;
      const title = fitTitle(label.title, bold, textW);
      page.drawText(title.text, {x: textX, y, size: title.size, font: bold});
      for (const line of label.lines) {
        y -= LINE_GAP;
        page.drawText(safeFit(line, regular, LINE_SIZE, textW), {x: textX, y, size: LINE_SIZE, font: regular});
      }
    });
  }
  return doc.save();
}
