import {Component, EventEmitter, Inject, OnInit, } from '@angular/core';
import {DocumentModel} from '../../../../../models';
import {BackendConfigurationPnDocumentsService} from '../../../../../services';
import {Subscription} from 'rxjs';
import {TemplateFilesService} from 'src/app/common/services';
import {applicationLanguagesTranslated, PdfIcon} from 'src/app/common/const';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {MatIconRegistry} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';

@Component({
  selector: 'app-documents-document-delete',
  templateUrl: './documents-document-delete.component.html',
  styleUrls: ['./documents-document-delete.component.scss']
})
export class DocumentsDocumentDeleteComponent implements OnInit {
  documentDeleted: EventEmitter<void> = new EventEmitter<void>();
  newDocumentModel: DocumentModel = new DocumentModel();
  pdfSub$: Subscription;
  documentSub$: Subscription;

  get languages() {
    return applicationLanguagesTranslated;
  }

  constructor(
    private templateFilesService: TemplateFilesService,
    private backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService,
    public dialogRef: MatDialogRef<DocumentsDocumentDeleteComponent>,
    @Inject(MAT_DIALOG_DATA) documentModel: DocumentModel,
    iconRegistry: MatIconRegistry,
    sanitizer: DomSanitizer,
  ) {
    iconRegistry.addSvgIconLiteral('file-pdf', sanitizer.bypassSecurityTrustHtml(PdfIcon));
    this.getDocument(documentModel.id);
  }

  ngOnInit(): void {}

  hide() {
    this.dialogRef.close();
  }

  submitCaseDelete() {
    this.backendConfigurationPnDocumentsService.deleteDocument(this.newDocumentModel.id).subscribe((data) => {
      if(data && data.success) {
        this.documentDeleted.emit();
        this.hide();
      }
    });
  }

  getDocument(documentId: number) {
    this.documentSub$ = this.backendConfigurationPnDocumentsService.getSingleDocument(documentId).subscribe((data) => {
      if (data && data.success) {
        this.newDocumentModel = data.model;
      }
    });
  }

  getFileNameByLanguage(languageId: number): string {
    const index = this.newDocumentModel.documentUploadedDatas.findIndex((x) => x.languageId === languageId);
    if (index !== -1) {
      const documentUploadedData = this.newDocumentModel.documentUploadedDatas[index];
      if (documentUploadedData.id) {
        return documentUploadedData.name;
      } else {
        const file = documentUploadedData.file;
        if (file) {
          return file.name;
        }
      }
    }
    return '';
  }

  getPdf(languageId: number) {
    const index = this.newDocumentModel.documentUploadedDatas.findIndex((x) => x.languageId === languageId);
    if (index !== -1) {
      const documentUploadedData = this.newDocumentModel.documentUploadedDatas[index];
      if (documentUploadedData.id) {
        this.pdfSub$ = this.templateFilesService.getImage(documentUploadedData.fileName).subscribe((blob) => {
          const fileURL = URL.createObjectURL(blob);
          window.open(fileURL, '_blank');
        });
      } else {
        window.open(URL.createObjectURL(documentUploadedData.file), '_blank');
      }
    }
  }
}
