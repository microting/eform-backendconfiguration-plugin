import {Component, EventEmitter, OnInit, Output, ViewChild} from '@angular/core';
import {DocumentModel} from 'src/app/plugins/modules/backend-configuration-pn/models';
import {BackendConfigurationPnDocumentsService} from 'src/app/plugins/modules/backend-configuration-pn/services';
import {Subscription} from 'rxjs';
import {TemplateFilesService} from 'src/app/common/services';
import {applicationLanguagesTranslated} from 'src/app/common/const';

@Component({
  selector: 'app-documents-document-delete',
  templateUrl: './documents-document-delete.component.html',
  styleUrls: ['./documents-document-delete.component.scss']
})
export class DocumentsDocumentDeleteComponent implements OnInit {
  @ViewChild('frame') frame;
  newDocumentModel: DocumentModel = new DocumentModel();
  pdfSub$: Subscription;
  documentSub$: Subscription;
  @Output() documentDeleted: EventEmitter<void> = new EventEmitter<void>();

  get languages() {
    return applicationLanguagesTranslated;
  }

  constructor(
    private templateFilesService: TemplateFilesService,
    private backendConfigurationPnDocumentsService: BackendConfigurationPnDocumentsService) {}

  ngOnInit(): void {}

  show(documentModel: DocumentModel) {
    this.getDocument(documentModel.id);
  }

  submitCaseDelete() {
    this.backendConfigurationPnDocumentsService.deleteDocument(this.newDocumentModel.id).subscribe((data) => {
      this.frame.hide();
      this.documentDeleted.emit();
    });
    // this.casesService.deleteCase(this.selectedCaseModel.id, this.selectedTemplateId).subscribe((data => {
    //   if (data && data.success) {
    //     this.onCaseDeleted.emit();
    //     this.frame.hide();
    //   }
    // }));
  }


  getDocument(documentId: number) {
    this.documentSub$ = this.backendConfigurationPnDocumentsService.getSingleDocument(documentId).subscribe((data) => {
      if (data && data.success) {
        this.newDocumentModel = data.model;
        this.frame.show();
      }
    });
  }


  getFileNameByLanguage(languageId: number): string {
    if (this.newDocumentModel.documentUploadedDatas.length>0) {
      if (this.newDocumentModel.documentUploadedDatas.find((x) => x.languageId == languageId).id) {
        return this.newDocumentModel.documentUploadedDatas.find((x) => x.languageId == languageId).name;
      } else {
        // return '';
        const file = this.newDocumentModel.documentUploadedDatas.find((x) => x.languageId == languageId).file;
        if (file) {
          return file.name;
        }
      }
    }
  }

  getPdf(languageId: number) {
    // TODO: CHECK
    const fileName = this.newDocumentModel.documentUploadedDatas.find((x) => x.languageId == languageId).fileName;
    this.pdfSub$ = this.templateFilesService.getImage(fileName).subscribe((blob) => {
      const fileURL = URL.createObjectURL(blob);
      window.open(fileURL, '_blank');
    });
  }
}
