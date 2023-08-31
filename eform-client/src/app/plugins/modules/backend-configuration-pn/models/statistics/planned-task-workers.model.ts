export interface PlannedTaskWorkers {
  taskWorkers: PlannedTaskWorker[];
}

export interface PlannedTaskWorker {
  workerName: string;
  statValue: number;
}
