import {ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output,} from '@angular/core';
import {Paged, TableHeaderElementModel} from 'src/app/common/models';
import {PropertyModel} from '../../../../models/properties';
import {PropertiesStateService} from '../../store';
import {PropertyCompliancesColorBadgesEnum} from 'src/app/plugins/modules/backend-configuration-pn/enums';
import {AuthStateService} from 'src/app/common/store';
import {EntitySelectService} from 'src/app/common/services';
import {Sort} from '@angular/material/sort';
import {MtxGridColumn} from '@ng-matero/extensions/grid';
import {Subject} from 'rxjs';
import {MatIconRegistry} from '@angular/material/icon';
import {DomSanitizer} from '@angular/platform-browser';
import {WordIcon} from 'src/app/common/const';
import { ThemePalette } from '@angular/material/core';
import {selectAuthIsAuth} from 'src/app/state/auth/auth.selector';
import {Store} from '@ngrx/store';

@Component({
  selector: 'app-properties-table',
  templateUrl: './properties-table.component.html',
  styleUrls: ['./properties-table.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PropertiesTableComponent implements OnInit {
  @Input() nameSearchSubject = new Subject();
  @Input() propertiesModel: Paged<PropertyModel> = new Paged<PropertyModel>();

  @Input() tableHeaders: MtxGridColumn[];
  @Input() adminTableHeaders: MtxGridColumn[];
  @Output()
  showEditPropertyModal: EventEmitter<PropertyModel> = new EventEmitter<PropertyModel>();
  @Output()
  showEditPropertyAreasModal: EventEmitter<PropertyModel> = new EventEmitter<PropertyModel>();
  @Output()
  showPropertyAreasModal: EventEmitter<PropertyModel> = new EventEmitter<PropertyModel>();
  @Output()
  showDeletePropertyModal: EventEmitter<PropertyModel> = new EventEmitter<PropertyModel>();
  @Output()
  sortUpdated: EventEmitter<void> = new EventEmitter<void>();
  @Output()
  showDocxReportModal: EventEmitter<number> = new EventEmitter<number>();
  @Output()
  showEditEntityListModal: EventEmitter<PropertyModel> = new EventEmitter<PropertyModel>();
  public isAuth$ = this.store.select(selectAuthIsAuth);
  public selectAuthIsAdmin$ = this.store.select(selectAuthIsAuth);

  get propertyCompliancesColorBadgesEnum() {
    return PropertyCompliancesColorBadgesEnum;
  }

  constructor(
    private store: Store,
    public propertiesStateService: PropertiesStateService,
    private entitySelectService: EntitySelectService,
    public authStateService: AuthStateService,
    iconRegistry: MatIconRegistry,
    sanitizer: DomSanitizer,
    ) {
    iconRegistry.addSvgIconLiteral('file-word', sanitizer.bypassSecurityTrustHtml(WordIcon));
  }

  ngOnInit(): void { }

  onShowDeletePropertyModal(propertyModel: PropertyModel) {
    this.showDeletePropertyModal.emit(propertyModel);
  }

  onShowEditPropertyModal(propertyModel: PropertyModel) {
    this.showEditPropertyModal.emit(propertyModel);
  }

  sortTable(sort: Sort) {
    this.propertiesStateService.onSortTable(sort.active);
    this.sortUpdated.emit();
  }


  getColorBadge(compliance: PropertyCompliancesColorBadgesEnum): string {
    switch (compliance) {
      case PropertyCompliancesColorBadgesEnum.Success:
        return 'color: green';
      case PropertyCompliancesColorBadgesEnum.Danger:
        return 'color: red;';
      case PropertyCompliancesColorBadgesEnum.Warning:
        return 'warn';
      default:
        return 'primary';
    }
  }

  onShowDocxReportModal(propertyId: number){
    this.showDocxReportModal.emit(propertyId);
  }

  onShowEditEntityListModal(propertyModel: PropertyModel) {
    this.showEditEntityListModal.emit(propertyModel);
  }

  onNameFilterChanged(name: string) {
    this.nameSearchSubject.next(name);
  }
}
