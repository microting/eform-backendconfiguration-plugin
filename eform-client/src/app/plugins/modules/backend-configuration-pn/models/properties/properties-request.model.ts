import {PagedEntityRequest} from 'src/app/common/models';

export class PropertiesRequestModel extends PagedEntityRequest {
  nameFilter: string;

  constructor() {
    super();
    this.nameFilter = '';
  }
}
