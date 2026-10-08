import {TextDecoder, TextEncoder} from 'util';
import {PDFDocument, StandardFonts} from 'pdf-lib';
import {TailBiteLocationNode} from '../../../../models';
import * as QRCode from 'qrcode';
import {buildQrSheetPdf, fitTitle, LABELS_PER_PAGE, pdfSafeText, qrLabels, TailBiteQrLabel} from './tail-bite-qr-pdf';

jest.mock('qrcode', () => {
  const actual = jest.requireActual('qrcode');
  return {...actual, create: jest.fn(actual.create)};
});

// jsdom has no TextEncoder/TextDecoder; qrcode needs them at call time and every browser has them.
Object.assign(globalThis, {TextEncoder, TextDecoder});

const node = (id: number, parentId: number | null, name: string, depth: number): TailBiteLocationNode =>
  ({id, parentId, name, depth, sortOrder: 0, removed: false, qrCode: `tok_${id}_AbCdEfGhIjKlMnOpQr`});
const nodes = [node(1, null, 'Ejendom', 0), node(2, 1, 'Stald A', 1), node(3, 2, 'Sektion 4', 2), node(4, 3, 'Sti 301', 3)];
const label = (i: number): TailBiteQrLabel => ({qrCode: `token-${i}`, title: `Sti ${300 + i}`, lines: ['Sektion 4', 'Stald A']});

describe('tail-bite QR sheet', () => {
  it('labels a pen with its two nearest parents and never the property root', () => {
    expect(qrLabels([nodes[3]], nodes)).toEqual([{qrCode: nodes[3].qrCode, title: 'Sti 301', lines: ['Sektion 4', 'Stald A']}]);
    expect(qrLabels([nodes[1]], nodes)[0].lines).toEqual(['Ejendom']);
    expect(qrLabels([nodes[0]], nodes)[0].lines).toEqual([]);
  });

  it('writes a PDF with eight labels per A4 page', async () => {
    const bytes = await buildQrSheetPdf(Array.from({length: LABELS_PER_PAGE + 1}, (_v, i) => label(i + 1)), 'Ejendom. Klip langs stregerne.');
    expect(new TextDecoder().decode(bytes.slice(0, 5))).toBe('%PDF-');
    const doc = await PDFDocument.load(bytes);
    expect(doc.getPageCount()).toBe(2);
    expect(Math.round(doc.getPage(0).getWidth())).toBe(595);
  });

  it('draws Danish and German letters and replaces what the font cannot draw', async () => {
    const font = await (await PDFDocument.create()).embedFont(StandardFonts.Helvetica);
    expect(pdfSafeText('Sti æøå ÆØÅ äöüß', font)).toBe('Sti æøå ÆØÅ äöüß');
    expect(pdfSafeText('Sti 🐷 9', font)).toBe('Sti ? 9');
    await expect(buildQrSheetPdf([{qrCode: 't', title: 'Sti 🐷', lines: ['Stald Ø']}], 'Header')).resolves.toBeInstanceOf(Uint8Array);
  });

  it('refuses an empty sheet', async () => {
    await expect(buildQrSheetPdf([], 'Header')).rejects.toThrow('No labels');
  });

  it('puts exactly eight labels on one page and nine on two', async () => {
    const pages = async (n: number) =>
      (await PDFDocument.load(await buildQrSheetPdf(Array.from({length: n}, (_v, i) => label(i + 1)), 'H'))).getPageCount();
    expect(await pages(LABELS_PER_PAGE)).toBe(1);
    expect(await pages(LABELS_PER_PAGE + 1)).toBe(2);
  });

  it('shrinks a long title before truncating, and never overflows the column', async () => {
    const font = await (await PDFDocument.create()).embedFont(StandardFonts.HelveticaBold);
    const medium = fitTitle('Farestald 12', font, 120);
    expect(medium).toEqual({text: 'Farestald 12', size: expect.any(Number)});
    expect(medium.size).toBeLessThan(24);
    expect(medium.size).toBeGreaterThanOrEqual(14);
    const long = fitTitle('Meget lang sektion med mange sma sti numre 1234567', font, 150);
    expect(long.size).toBe(14);
    expect(long.text.endsWith('…')).toBe(true);
    expect(font.widthOfTextAtSize(long.text, long.size)).toBeLessThanOrEqual(150);
  });

  it('encodes the code unchanged at error correction M', () => {
    const code = 'tok_9_AbCdEfGhIjKlMnOpQr';
    return buildQrSheetPdf([{qrCode: code, title: 'Sti', lines: []}], 'H').then(() => {
      expect(QRCode.create).toHaveBeenCalledWith(code, {errorCorrectionLevel: 'M'});
    });
  });

  it('does not throw on a name outside WinAnsi', async () => {
    await expect(buildQrSheetPdf([{qrCode: 'c', title: 'Łódź 🐷', lines: ['Łódź 🐷']}], 'Łódź')).resolves.toBeInstanceOf(Uint8Array);
  });
});
