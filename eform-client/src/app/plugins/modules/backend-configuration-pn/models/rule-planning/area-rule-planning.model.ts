export class AreaRulePlanningModel {
  status: boolean;
  sendNotifications: boolean;
  startDate: string;
  assignedSites: AreaRuleAssignedSitesModel[] = [];
}

export class AreaRuleAssignedSitesModel {
  siteId: number;
  name: string;
  siteUId: number;
}



