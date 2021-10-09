import { CommonDictionaryModel, SiteDto } from 'src/app/common/models';

export class AreaModel {
  id: number;
  name: string;
  type: 1 | 2 | 3 | 4 | 5 | 6;
  languages: CommonDictionaryModel[] = [];
  availableWorkers: SiteDto[] = [];
  initialFields: AreaInitialFieldsModel;
}

export class AreaInitialFieldsModel {
  eformId?: number;
  eformName?: string;
  sendNotifications?: boolean;
  repeatEvery?: number;
  repeatType?: number;
  dayOfWeek?: number;
  type?: number;
  alarm?: number;
  endDate?: string;
}
