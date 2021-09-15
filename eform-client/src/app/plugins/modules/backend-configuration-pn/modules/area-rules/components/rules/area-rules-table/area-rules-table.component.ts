import { Component, OnInit } from '@angular/core';
import { TableHeaderElementModel } from 'src/app/common/models';

@Component({
  selector: 'app-area-rules-table',
  templateUrl: './area-rules-table.component.html',
  styleUrls: ['./area-rules-table.component.scss'],
})
export class AreaRulesTableComponent implements OnInit {
  tableHeaders: TableHeaderElementModel[] = [
    { name: 'Name', elementId: 'nameTableHeader', sortable: false },
    { name: 'Eform', elementId: 'eformTableHeader', sortable: false },
    { name: 'Language', elementId: 'languageTableHeader', sortable: false },
    { name: 'Status', elementId: 'statusTableHeader', sortable: false },
    { name: 'Actions', elementId: '', sortable: false },
  ];

  constructor() {}

  ngOnInit(): void {}
}
