export class PoolHoursModel {
  areaRuleId: number;
  parrings: PoolHourModel[];
}

export class PoolHourModel {
  constructor(i: number, j: number, b: boolean, name: string) {
    this.dayOfWeek = i;
    this.index = j;
    this.isActive = b;
    this.name = name;
  }

  areaRuleId: number;
  dayOfWeek: number;
  index: number;
  isActive: boolean;
  name: string;
}
