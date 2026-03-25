import {Component} from '@angular/core';
import {MatDialogRef} from '@angular/material/dialog';

@Component({
  standalone: false,
  selector: 'app-tag-create-dialog',
  template: `
    <h2 mat-dialog-title>
      <mat-icon style="vertical-align: middle; margin-right: 8px;">label</mat-icon>
      {{ 'Opret tag' | translate }}
    </h2>
    <mat-dialog-content>
      <label class="team-field-label">{{ 'Navn' | translate }}</label>
      <mat-form-field appearance="outline" style="width: 100%;">
        <input matInput [(ngModel)]="tagName" [placeholder]="'Indtast tagnavn' | translate" (keydown.enter)="onCreate()">
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'Annuller' | translate }}</button>
      <button mat-flat-button color="primary" [disabled]="!tagName.trim()" (click)="onCreate()">{{ 'Opret' | translate }}</button>
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
export class TagCreateDialogComponent {
  tagName = '';

  constructor(private dialogRef: MatDialogRef<TagCreateDialogComponent>) {}

  onCreate() {
    const name = this.tagName.trim();
    if (name) {
      this.dialogRef.close(name);
    }
  }
}
