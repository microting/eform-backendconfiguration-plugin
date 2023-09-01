export interface PlannedTaskWorkers {
  taskWorkers: PlannedTaskWorker[];
}

export interface PlannedTaskWorker {
  workerId: number;
  workerName: string;
  statValue: number;
}
