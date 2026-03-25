import {Component, Inject} from '@angular/core';
import {MatDialogRef, MAT_DIALOG_DATA} from '@angular/material/dialog';

@Component({
  standalone: false,
  selector: 'app-team-delete-dialog',
  template: `
    <h3 mat-dialog-title>{{ 'Are you sure you want to delete' | translate }}?</h3>
    <div mat-dialog-content>
      <div class="d-flex flex-row justify-content-between">
        <div class="d-flex flex-column">
          <p>ID</p>
        </div>
        <div class="d-flex flex-column">
          <strong>{{ data.id }}</strong>
        </div>
      </div>
      <div class="d-flex flex-row justify-content-between">
        <div class="d-flex flex-column">
          <p>{{ 'Name' | translate }}</p>
        </div>
        <div class="d-flex flex-column">
          <strong>{{ data.name }}</strong>
        </div>
      </div>
    </div>
    <div mat-dialog-actions class="d-flex flex-row justify-content-end align-items-center gap-12">
      <button class="btn-delete" (click)="onDelete()">
        {{ 'Delete' | translate }}
      </button>
      <button class="btn-cancel" (click)="dialogRef.close()">
        {{ 'Cancel' | translate }}
      </button>
    </div>
  `,
})
export class TeamDeleteDialogComponent {
  constructor(
    public dialogRef: MatDialogRef<TeamDeleteDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: {id: number; name: string},
  ) {}

  onDelete() {
    this.dialogRef.close(this.data.id);
  }
}
