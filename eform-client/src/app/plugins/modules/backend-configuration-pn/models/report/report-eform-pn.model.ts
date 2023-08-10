import {ReportEformItemModel} from './report-eform-item-pn.model';
import {ReportEformPostModel} from './report-eform-post-pn.model';

export interface ReportEformPnModel extends BaseReportModel {
  textHeaders: ReportEformTextHeaderModel;
  descriptionBlocks: string[];
  tableName: string;
}

interface ReportEformTextHeaderModel {
  header1: string;
  header2: string;
  header3: string;
  header4: string;
  header5: string;
}

export interface NewReportEformPnModel {
  groupTagName: string;
  groupEform: GroupEformModel[];
}

interface GroupEformModel {
  items: ReportEformItemModel[];
  itemHeaders: { key: string, value: string }[];
  imageNames: { key: { key: number, value: string }, value: { key: string, value: string } }[];
  checkListId: number;
  checkListName: string;
}

export interface BaseReportModel {
  templateName: string;
  items: ReportEformItemModel[];
  itemHeaders: { key: string, value: string }[];
  imageNames: { key: { key: number, value: string }, value: { key: string, value: string } }[];
  posts: ReportEformPostModel[];
}
