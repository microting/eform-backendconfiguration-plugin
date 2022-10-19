import {ApiBaseService} from 'src/app/common/services';
import {ChemicalModel, ChemicalsRequestModel} from 'src/app/plugins/modules/backend-configuration-pn/modules';
import {OperationDataResult, Paged} from 'src/app/common/models';
import {Observable} from 'rxjs';
import {Injectable} from '@angular/core';


export let ChemicalPnChemicalsMethods = {
  Chemicals: 'api/chemicals-pn/chemicals',
  ChemicalsIndex: 'api/chemicals-pn/chemicals/index',
};

@Injectable({
  providedIn: 'root',
})
export class BackendConfigurationPnChemicalsService {
  constructor(private apiBaseService: ApiBaseService) {}

  getAllChemicals(
    model: ChemicalsRequestModel
  ): Observable<OperationDataResult<Paged<ChemicalModel>>> {
    return this.apiBaseService.post(
      ChemicalPnChemicalsMethods.ChemicalsIndex,
      model
    );
  }

  getSingleChemical(
    planningId: number
  ): Observable<OperationDataResult<ChemicalModel>> {
    return this.apiBaseService.get(ChemicalPnChemicalsMethods.Chemicals, {
      id: planningId,
    });
  }
}
