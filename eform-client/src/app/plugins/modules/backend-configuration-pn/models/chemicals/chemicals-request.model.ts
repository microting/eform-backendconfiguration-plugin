import {PagedEntityRequest} from 'src/app/common/models';

export class ChemicalsRequestModel extends PagedEntityRequest {
  nameFilter: string;
  descriptionFilter: string;
  tagIds: number[];
  propertyId: number;

  constructor() {
    super();
    this.nameFilter = '';
    this.descriptionFilter = '';
    this.tagIds = [];
  }
}
