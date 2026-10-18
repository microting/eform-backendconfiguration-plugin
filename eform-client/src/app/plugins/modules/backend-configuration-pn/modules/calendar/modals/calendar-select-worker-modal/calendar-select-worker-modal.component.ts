import {Component, inject} from '@angular/core';
import {MatDialogRef, MatDialogTitle, MatDialogContent, MatDialogActions} from '@angular/material/dialog';
import {CommonDictionaryModel} from 'src/app/common/models';
import {FormsModule} from '@angular/forms';
import {TranslatePipe} from '@ngx-translate/core';
import {MtxSelect} from '@ng-matero/extensions/select';

@Component({
  selector: 'app-calendar-select-worker-modal',
  imports: [FormsModule, TranslatePipe, MtxSelect, MatDialogTitle, MatDialogContent, MatDialogActions],
  templateUrl: './calendar-select-worker-modal.component.html',
  styleUrls: ['./calendar-select-worker-modal.component.scss'],
})
export class CalendarSelectWorkerModalComponent {
  dialogRef = inject(MatDialogRef<CalendarSelectWorkerModalComponent>);

  sites: CommonDictionaryModel[] = [];
  selectedSite: CommonDictionaryModel | null = null;

  hide() {
    this.dialogRef.close(null);
  }

  confirm() {
    this.dialogRef.close(this.selectedSite);
  }
}
