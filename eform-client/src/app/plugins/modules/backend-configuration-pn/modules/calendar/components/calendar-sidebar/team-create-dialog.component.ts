import {Component} from '@angular/core';
import {MatDialogRef} from '@angular/material/dialog';

@Component({
  standalone: false,
  selector: 'app-team-create-dialog',
  template: `
    <h2 mat-dialog-title>
      <mat-icon style="vertical-align: middle; margin-right: 8px;">groups</mat-icon>
      {{ 'Opret team' | translate }}
    </h2>
    <mat-dialog-content>
      <label class="team-field-label">{{ 'Navn' | translate }}</label>
      <mat-form-field appearance="outline" style="width: 100%;">
        <input matInput [(ngModel)]="teamName" [placeholder]="'Indtast teamnavn' | translate" (keydown.enter)="onCreate()">
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'Annuller' | translate }}</button>
      <button mat-flat-button color="primary" [disabled]="!teamName.trim()" (click)="onCreate()">{{ 'Opret' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .team-field-label {
      font-size: 13px;
      color: #5f6368;
      margin-bottom: 4px;
      display: block;
    }
  `]
})
export class TeamCreateDialogComponent {
  teamName = '';

  constructor(private dialogRef: MatDialogRef<TeamCreateDialogComponent>) {}

  onCreate() {
    const name = this.teamName.trim();
    if (name) {
      this.dialogRef.close(name);
    }
  }
}
