import {Component, OnInit, inject, ViewChild, ElementRef} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import * as QRCode from 'qrcode';

export interface QrModalData {
  customerNo: number;
  otpCode: number;
}

@Component({
  selector: 'app-property-worker-qr-modal',
  templateUrl: './property-worker-qr-modal.component.html',
  styleUrls: ['./property-worker-qr-modal.component.scss'],
  standalone: false,
})
export class PropertyWorkerQrModalComponent implements OnInit {
  public dialogRef = inject(MatDialogRef<PropertyWorkerQrModalComponent>);
  public data = inject<QrModalData>(MAT_DIALOG_DATA);

  @ViewChild('qrCanvas', {static: true}) qrCanvas!: ElementRef<HTMLCanvasElement>;

  qrValue = '';
  copied = false;

  ngOnInit() {
    this.qrValue = `${this.data.customerNo} / ${this.data.otpCode}`;
    QRCode.toCanvas(this.qrCanvas.nativeElement, this.qrValue, {
      width: 300,
      margin: 2,
      errorCorrectionLevel: 'M',
    });
  }

  close() {
    this.dialogRef.close();
  }

  async copyToClipboard() {
    const canvas = this.qrCanvas.nativeElement;
    try {
      const blob = await new Promise<Blob>((resolve, reject) => {
        canvas.toBlob((b) => {
          if (b) {
            resolve(b);
          } else {
            reject(new Error('Failed to create blob'));
          }
        }, 'image/png');
      });
      await navigator.clipboard.write([
        new ClipboardItem({'image/png': blob}),
      ]);
      this.copied = true;
      setTimeout(() => (this.copied = false), 2000);
    } catch (err) {
      // Clipboard write failed silently
    }
  }

  downloadQr() {
    const canvas = this.qrCanvas.nativeElement;
    const link = document.createElement('a');
    link.download = `qr-${this.data.customerNo}-${this.data.otpCode}.png`;
    link.href = canvas.toDataURL('image/png');
    link.click();
  }
}

