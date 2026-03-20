export interface CalendarBoardModel {
  id: number;
  name: string;
  color: string;
  propertyId: number;
}

export const CALENDAR_COLORS: string[] = [
  '#4CAF50', '#2196F3', '#FF9800', '#E91E63',
  '#9C27B0', '#00BCD4', '#FF5722', '#607D8B',
  '#795548', '#F44336', '#3F51B5', '#009688',
];
