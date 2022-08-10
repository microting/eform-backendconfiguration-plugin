export class PropertyCreateModel {
  name: string;
  chr: string;
  cvr: string;
  address: string;
  workorderEnable: boolean = false;
  languagesIds: number[] = [];
  industryCode: string;
  isFarm: boolean = false;
  mainMailAddress: string;
}
