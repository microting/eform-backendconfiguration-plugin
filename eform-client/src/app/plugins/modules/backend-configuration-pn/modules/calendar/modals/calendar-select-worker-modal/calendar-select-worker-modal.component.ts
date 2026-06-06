import {Component, inject} from '@angular/core';
import {MatDialogRef} from '@angular/material/dialog';
import {CommonDictionaryModel} from 'src/app/common/models';

@Component({
  standalone: false,
  selector: 'app-calendar-select-worker-modal',
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
