export interface CalendarBoardModel {
  id: number;
  name: string;
  color: string;
  propertyId: number;
}

export const CALENDAR_COLORS: string[] = [
  '#d04d4d', '#5ba8ff', '#fd9b6b', '#9588fd',
  '#fff286', '#e6cfff', '#ff88cf', '#bfd8ff',
];

const LIGHT_BOARD_COLORS = new Set(['#fff286', '#e6cfff', '#ff88cf', '#bfd8ff']);

export function boardTextColor(hex: string | undefined | null): string {
  return LIGHT_BOARD_COLORS.has((hex ?? '').toLowerCase()) ? '#1a1a1a' : 'white';
}
