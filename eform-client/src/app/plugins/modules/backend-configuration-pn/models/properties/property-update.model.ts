export class PropertyUpdateModel {
  id: number;
  name: string;
  chr: string;
  cvr: string;
  address: string;
  languagesIds: number[] = [];
  workorderEnable: boolean = false;
  isFarm: boolean = false;
  industryCode: string;
}
