import {DocumentTranslationModel} from 'src/app/plugins/modules/backend-configuration-pn/models/documents/document-translation.model';
import {DocumentPropertyModel} from 'src/app/plugins/modules/backend-configuration-pn/models/documents/document-property.model';
import {DocumentUploadedDataModel} from 'src/app/plugins/modules/backend-configuration-pn/models/documents/document-uploaded-data.model';

export class DocumentModel {
  id?: number;
  documentTranslations: DocumentTranslationModel[] = [];
  folderId?: number;
  endDate?: string;
  status: string;
  propertyNames: string;
  documentProperties: DocumentPropertyModel[] = [];
  documentUploadedDatas: DocumentUploadedDataModel[] = [];
}
