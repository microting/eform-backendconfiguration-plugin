import {Component, Inject, OnInit} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {saveAs} from 'file-saver';

export interface CompliancePdfPreviewDialogData {
  /** The PDF bytes the export endpoint answered with. */
  blob: Blob;
  /** The server-chosen file name (from `Content-Disposition`), also the subtitle. */
  fileName: string;
}

/**
 * "PDF-forhåndsvisning" (#1189): shows the PDF the export endpoint returned
 * and lets the user save it or close without saving.
 *
 * DISPLAY ONLY. The document is generated server-side (#1160 decision 4 —
 * no client-side html2pdf); this dialog renders the bytes it was handed with
 * `ng2-pdf-viewer`, the renderer the Files module already uses, and `Gem`
 * saves THOSE SAME BYTES through `file-saver`. It never re-POSTs the export:
 * the preview and the saved file are one and the same response.
 *
 * `pdf-viewer` wants a `Uint8Array` (or a URL), not a `Blob`, so the blob is
 * read once in `ngOnInit`; the spinner covers the read.
 */
@Component({
  standalone: false,
  selector: 'app-compliance-pdf-preview-dialog',
  templateUrl: './compliance-pdf-preview-dialog.component.html',
  styleUrls: ['./compliance-pdf-preview-dialog.component.scss'],
})
export class CompliancePdfPreviewDialogComponent implements OnInit {
  bytes: Uint8Array | null = null;
  /**
   * Set when the blob could not be read for the in-browser preview. The bytes
   * DID arrive, so `Gem` still works — it saves the blob itself, not the
   * decoded preview; only the on-screen rendering is replaced by a message.
   */
  loadFailed = false;

  constructor(
    public dialogRef: MatDialogRef<CompliancePdfPreviewDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: CompliancePdfPreviewDialogData,
  ) {}

  ngOnInit(): void {
    this.data.blob
      .arrayBuffer()
      .then((buffer) => {
        this.bytes = new Uint8Array(buffer);
      })
      .catch(() => {
        this.loadFailed = true;
      });
  }

  cancel(): void {
    this.dialogRef.close(false);
  }

  save(): void {
    saveAs(this.data.blob, this.data.fileName);
    this.dialogRef.close(true);
  }
}
